using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The contact step reconciles every remediated case carrying an adviser email inside the
    /// contact save. What does not depend on the case - the product name, the two teams, the
    /// AQS queue account, and who the shared adviser email resolves to - is read once for the
    /// whole run, not once per case, and the answer per case is the same either way.
    /// </summary>
    public class CaseAccessLookupsTests
    {
        private const string Email = "ann@example.com";
        private static readonly Guid RouteId = Guid.Parse("abababab-0000-4000-8000-000000000001");
        private static readonly Guid TaxTeam = Guid.Parse("abababab-0000-4000-8000-000000000002");
        private static readonly Guid AqsTeam = Guid.Parse("abababab-0000-4000-8000-000000000003");
        private static readonly Guid QueueAccount = Guid.Parse("abababab-0000-4000-8000-000000000004");
        private static readonly Guid Adviser = Guid.Parse("abababab-0000-4000-8000-000000000005");
        private static readonly Guid Supervisor = Guid.Parse("abababab-0000-4000-8000-000000000006");
        private static readonly DateTime Now = new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);

        private static readonly Guid[] Cases =
        {
            Guid.Parse("abababab-0000-4000-8000-0000000000a1"),
            Guid.Parse("abababab-0000-4000-8000-0000000000a2"),
            Guid.Parse("abababab-0000-4000-8000-0000000000a3"),
        };

        // Three released cases on one adviser email: each has a submitted AQS review and a
        // remedial action, the adviser is one active contact, and the email is mapped to a
        // T&C Manager.
        private static FakeOrganizationService ReleasedWorld(int status = CaseLifecycle.AwaitingRemediation, bool remediation = true)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("team", TaxTeam, "name", ProductName.TaxTeam(ProductName.Default));
            svc.Seed("team", AqsTeam, "name", ProductName.AqsTeam(ProductName.Default));
            svc.Seed("account", QueueAccount, "name", ProductName.AqsQueueAccount(ProductName.Default));
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", false, "al_requiresaqsreview", true);
            svc.Seed("contact", Adviser, "fullname", "Ann Adviser", "emailaddress1", Email, "statecode", new OptionSetValue(0));
            svc.Seed("al_advisermapping", Guid.NewGuid(),
                "al_adviseremail", Email,
                "al_tcmanagerid", new EntityReference("contact", Supervisor),
                "statecode", new OptionSetValue(0));

            foreach (var caseId in Cases)
            {
                svc.Seed("al_outcomecase", caseId,
                    "al_casestatus", new OptionSetValue(status),
                    "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId),
                    "al_advisername", "Ann Adviser",
                    "al_adviseremail", Email);
                if (status != CaseLifecycle.Queued)
                {
                    svc.Seed("al_reviewinstance", Guid.NewGuid(),
                        "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                        "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                        "al_submittedon", Now.AddDays(-1),
                        "al_sequence", 1,
                        "statecode", new OptionSetValue(0));
                }

                if (remediation)
                {
                    svc.Seed("al_remediationaction", Guid.NewGuid(),
                        "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                        "al_actionstatus", new OptionSetValue(Remediation.StatusOpen));
                }
            }

            return svc;
        }

        [Fact]
        public void Shared_lookups_read_the_invariants_once_for_several_cases()
        {
            var fake = ReleasedWorld();
            var counted = new CountingOrganizationService(fake);
            var lookups = new CaseAccessLookups(counted);

            foreach (var caseId in Cases)
            {
                CaseAccessReconciler.Reconcile(counted, caseId, Now, lookups);
            }

            Assert.Equal(1, counted.QueriesOf("environmentvariabledefinition"));
            Assert.Equal(2, counted.QueriesOf("team"));
            Assert.Equal(1, counted.QueriesOf("contact"));
            Assert.Equal(1, counted.QueriesOf("al_advisermapping"));

            // Still per case, because they are about the case.
            Assert.Equal(Cases.Length, counted.QueriesOf("al_reviewinstance"));
            Assert.Equal(Cases.Length, counted.QueriesOf("al_remediationaction"));
        }

        [Fact]
        public void The_queue_account_is_read_once_for_several_queued_cases()
        {
            var fake = ReleasedWorld(CaseLifecycle.Queued, remediation: false);
            var counted = new CountingOrganizationService(fake);
            var lookups = new CaseAccessLookups(counted);

            foreach (var caseId in Cases)
            {
                CaseAccessReconciler.Reconcile(counted, caseId, Now, lookups);
            }

            Assert.Equal(1, counted.QueriesOf("account"));
            foreach (var caseId in Cases)
            {
                Assert.Equal(QueueAccount, fake.Row("al_outcomecase", caseId)
                    .GetAttributeValue<EntityReference>(CaseAccessReconciler.QueueAccountAttr).Id);
            }
        }

        [Fact]
        public void The_single_case_reconcile_reads_its_own_lookups_every_time()
        {
            // Its other callers (CaseAccessPlugin, al_ReconcileCaseAccess) reconcile one case
            // per call, and each call reads afresh - nothing is carried between them.
            var fake = ReleasedWorld();
            var counted = new CountingOrganizationService(fake);

            CaseAccessReconciler.Reconcile(counted, Cases[0], Now);
            CaseAccessReconciler.Reconcile(counted, Cases[1], Now);

            Assert.Equal(4, counted.QueriesOf("team"));
            Assert.Equal(2, counted.QueriesOf("contact"));
        }

        [Theory]
        [InlineData(CaseLifecycle.AwaitingRemediation, true)]
        [InlineData(CaseLifecycle.Closed, true)]
        [InlineData(CaseLifecycle.Queued, false)]
        [InlineData(CaseLifecycle.Submitted, false)]
        public void Shared_lookups_give_each_case_what_a_single_case_reconcile_gives(int status, bool remediation)
        {
            var single = ReleasedWorld(status, remediation);
            var singleChanges = Cases.Select(c => CaseAccessReconciler.Reconcile(single, c, Now)).ToList();

            var batched = ReleasedWorld(status, remediation);
            var lookups = new CaseAccessLookups(batched);
            var batchedChanges = Cases.Select(c => CaseAccessReconciler.Reconcile(batched, c, Now, lookups)).ToList();

            for (var i = 0; i < Cases.Length; i++)
            {
                Assert.Equal(Describe(single.Row("al_outcomecase", Cases[i])), Describe(batched.Row("al_outcomecase", Cases[i])));
                Assert.Equal(singleChanges[i].ChangedColumns, batchedChanges[i].ChangedColumns);
                Assert.Equal(singleChanges[i].Released, batchedChanges[i].Released);
                Assert.Equal(singleChanges[i].AdviserUnmatched, batchedChanges[i].AdviserUnmatched);
                Assert.Equal(singleChanges[i].SupervisorUnmatched, batchedChanges[i].SupervisorUnmatched);
            }

            Assert.Equal(Shares(single), Shares(batched));
        }

        [Fact]
        public void A_contact_write_over_several_cases_reads_the_teams_once()
        {
            var fake = ReleasedWorld();
            var counted = new CountingOrganizationService(fake);

            AdviserContactPlugin.Follow(counted, Email, Guid.NewGuid());

            Assert.Equal(2, counted.QueriesOf("team"));
            Assert.Equal(1, counted.QueriesOf("al_advisermapping"));
            foreach (var caseId in Cases)
            {
                Assert.Equal(Adviser, fake.Row("al_outcomecase", caseId)
                    .GetAttributeValue<EntityReference>(CaseAccessReconciler.AdviserAttr).Id);
                Assert.Equal(Supervisor, fake.Row("al_outcomecase", caseId)
                    .GetAttributeValue<EntityReference>(CaseAccessReconciler.SupervisorAttr).Id);
            }
        }

        [Fact]
        public void A_missing_team_is_still_refused_through_shared_lookups()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", Cases[0], "al_casestatus", new OptionSetValue(CaseLifecycle.Queued));

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseAccessReconciler.Reconcile(svc, Cases[0], Now, new CaseAccessLookups(svc)));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal.Message);
        }

        [Fact]
        public void Lookups_read_through_another_service_are_refused()
        {
            var svc = ReleasedWorld();
            var other = new CountingOrganizationService(svc);

            Assert.Throws<ArgumentException>(
                () => CaseAccessReconciler.Reconcile(svc, Cases[0], Now, new CaseAccessLookups(other)));
            Assert.Empty(svc.Updates);
        }

        private static string Describe(Entity row)
        {
            var columns = new[]
            {
                CaseAccessReconciler.TaxCheckerAttr, CaseAccessReconciler.AqsCheckerAttr,
                CaseAccessReconciler.QueueAccountAttr, CaseAccessReconciler.AdviserAttr,
                CaseAccessReconciler.SupervisorAttr,
            };
            var parts = columns.Select(c =>
            {
                var value = row.GetAttributeValue<EntityReference>(c);
                return c + "=" + (value == null ? "-" : value.Id.ToString("D"));
            }).ToList();
            parts.Add(CaseAccessReconciler.QueuedOnAttr + "=" + row.GetAttributeValue<DateTime?>(CaseAccessReconciler.QueuedOnAttr));
            return string.Join(";", parts);
        }

        private static List<string> Shares(FakeOrganizationService svc)
        {
            return svc.Requests.Select(r =>
            {
                var grant = r as GrantAccessRequest;
                if (grant != null)
                {
                    return "grant " + grant.Target.Id + " " + grant.PrincipalAccess.Principal.Id + " " + grant.PrincipalAccess.AccessMask;
                }

                var revoke = r as RevokeAccessRequest;
                return revoke != null ? "revoke " + revoke.Target.Id + " " + revoke.Revokee.Id : r.RequestName;
            }).ToList();
        }
    }
}
