using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    public class CaseAccessReconcilerTests
    {
        private static readonly Guid CaseId = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
        private static readonly Guid RouteId = Guid.Parse("dddddddd-0000-4000-8000-000000000002");
        private static readonly Guid TaxTeam = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");
        private static readonly Guid AqsTeam = Guid.Parse("eeeeeeee-0000-4000-8000-000000000002");
        private static readonly Guid QueueAccount = Guid.Parse("eeeeeeee-0000-4000-8000-000000000003");
        private static readonly Guid Checker = Guid.Parse("ffffffff-0000-4000-8000-000000000001");
        private static readonly DateTime Now = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

        private static FakeOrganizationService Environment(int status, bool tax, bool aqs)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("team", TaxTeam, "name", CaseAccessReconciler.TaxTeamName);
            svc.Seed("team", AqsTeam, "name", CaseAccessReconciler.AqsTeamName);
            svc.Seed("account", QueueAccount, "name", CaseAccessReconciler.AqsQueueAccountName);
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", tax, "al_requiresaqsreview", aqs);
            svc.Seed(
                "al_outcomecase", CaseId,
                "al_casestatus", new OptionSetValue(status),
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId),
                "al_advisername", "Ann Adviser",
                "al_adviseremail", "ann@example.com");
            return svc;
        }

        [Fact]
        public void A_queued_aqs_case_is_put_on_the_queue_and_shared_with_the_aqs_team()
        {
            var svc = Environment(CaseLifecycle.Queued, false, true);

            var change = CaseAccessReconciler.Reconcile(svc, CaseId, Now);

            var row = svc.Row("al_outcomecase", CaseId);
            Assert.Equal(QueueAccount, row.GetAttributeValue<EntityReference>(CaseAccessReconciler.QueueAccountAttr).Id);
            Assert.Equal(Now, row.GetAttributeValue<DateTime>(CaseAccessReconciler.QueuedOnAttr));
            Assert.Contains(CaseAccessReconciler.QueueAccountAttr, change.ChangedColumns);

            var grants = svc.Requests.OfType<GrantAccessRequest>().ToList();
            Assert.Single(grants);
            Assert.Equal(AqsTeam, grants[0].PrincipalAccess.Principal.Id);

            var revokes = svc.Requests.OfType<RevokeAccessRequest>().ToList();
            Assert.Single(revokes);
            Assert.Equal(TaxTeam, revokes[0].Revokee.Id);
        }

        [Fact]
        public void The_share_lets_a_manager_allocate()
        {
            // al_AssignCase writes the review and changes its owner as the CALLER, so a
            // read-only share would let a manager see work they could not allocate.
            var svc = Environment(CaseLifecycle.Queued, false, true);
            CaseAccessReconciler.Reconcile(svc, CaseId, Now);

            var mask = svc.Requests.OfType<GrantAccessRequest>().Single().PrincipalAccess.AccessMask;
            Assert.True(mask.HasFlag(AccessRights.ReadAccess));
            Assert.True(mask.HasFlag(AccessRights.WriteAccess));
            Assert.True(mask.HasFlag(AccessRights.AssignAccess));
        }

        [Fact]
        public void A_second_run_writes_nothing()
        {
            var svc = Environment(CaseLifecycle.Queued, false, true);
            CaseAccessReconciler.Reconcile(svc, CaseId, Now);
            svc.ClearUpdates();

            var change = CaseAccessReconciler.Reconcile(svc, CaseId, Now.AddHours(1));

            Assert.Empty(change.ChangedColumns);
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void The_allocated_checker_is_written_to_the_case()
        {
            var svc = Environment(CaseLifecycle.Assigned, true, true);
            svc.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_assignedcontactid", new EntityReference("contact", Checker),
                "al_sequence", 1,
                "statecode", new OptionSetValue(0));

            CaseAccessReconciler.Reconcile(svc, CaseId, Now);

            Assert.Equal(Checker, svc.Row("al_outcomecase", CaseId)
                .GetAttributeValue<EntityReference>(CaseAccessReconciler.TaxCheckerAttr).Id);
        }

        [Fact]
        public void A_missing_team_is_refused_rather_than_skipped()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId, "al_casestatus", new OptionSetValue(CaseLifecycle.Queued));

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseAccessReconciler.Reconcile(svc, CaseId, Now));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal.Message);
            Assert.Contains(CaseAccessReconciler.TaxTeamName, refusal.Message);
        }

        [Fact]
        public void An_ambiguous_adviser_is_reported_not_guessed()
        {
            var svc = Environment(CaseLifecycle.AwaitingRemediation, false, true);
            svc.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_submittedon", Now.AddDays(-1),
                "al_sequence", 1,
                "statecode", new OptionSetValue(0));
            svc.Seed("al_remediationaction", Guid.NewGuid(), "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            svc.Seed("contact", Guid.NewGuid(), "fullname", "Ann Adviser", "statecode", new OptionSetValue(0));
            svc.Seed("contact", Guid.NewGuid(), "fullname", "Ann Adviser", "statecode", new OptionSetValue(0));

            var change = CaseAccessReconciler.Reconcile(svc, CaseId, Now);

            Assert.True(change.Released);
            Assert.True(change.AdviserUnmatched);
            Assert.Null(svc.Row("al_outcomecase", CaseId).GetAttributeValue<EntityReference>(CaseAccessReconciler.AdviserAttr));
        }

        [Fact]
        public void A_released_case_names_the_supervisor_from_the_adviser_mapping()
        {
            var svc = Environment(CaseLifecycle.AwaitingRemediation, false, true);
            svc.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_submittedon", Now.AddDays(-1),
                "al_sequence", 1,
                "statecode", new OptionSetValue(0));
            svc.Seed("al_remediationaction", Guid.NewGuid(), "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            var adviser = Guid.NewGuid();
            var supervisor = Guid.NewGuid();
            svc.Seed("contact", adviser, "fullname", "Ann Adviser", "statecode", new OptionSetValue(0));
            svc.Seed(
                "al_advisermapping", Guid.NewGuid(),
                "al_adviseremail", "ann@example.com",
                "al_tcmanagerid", new EntityReference("contact", supervisor),
                "statecode", new OptionSetValue(0));

            var change = CaseAccessReconciler.Reconcile(svc, CaseId, Now);

            var row = svc.Row("al_outcomecase", CaseId);
            Assert.Equal(adviser, row.GetAttributeValue<EntityReference>(CaseAccessReconciler.AdviserAttr).Id);
            Assert.Equal(supervisor, row.GetAttributeValue<EntityReference>(CaseAccessReconciler.SupervisorAttr).Id);
            Assert.False(change.AdviserUnmatched);
            Assert.False(change.SupervisorUnmatched);
        }
    }
}
