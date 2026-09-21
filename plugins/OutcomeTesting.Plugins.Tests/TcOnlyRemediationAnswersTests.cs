using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// "Recheck required?" and "Do the remedial actions change the advice?" are the T&amp;C
    /// Manager's answers (project owner, 2026-09-21), not the adviser's.
    ///
    /// Two halves, and both are tested here because either one alone is the defect. The
    /// refusal without the write leaves nobody able to answer; the write without the refusal
    /// leaves the adviser still able to, which is the state this replaces - and
    /// <c>al_recheckrequired</c> is what SignoffProgressPlugin reads to decide whether the
    /// case closes on the approval or waits at Awaiting Recheck (AD-138), so the adviser was
    /// deciding whether their own remediation needed looking at again.
    /// </summary>
    public class TcOnlyRemediationAnswersTests
    {
        private static readonly Guid ContactId = Guid.Parse("dddddddd-4444-4444-8444-444444444444");
        private static readonly Guid CaseId = Guid.Parse("cccccccc-1111-4111-8111-111111111111");
        private static readonly Guid ReviewId = Guid.Parse("eeeeeeee-2222-4222-8222-222222222222");
        private static readonly Guid ActionId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
        private static readonly Guid SiblingId = Guid.Parse("aaaaaaaa-2222-4222-8222-222222222222");
        private static readonly Guid OtherLegId = Guid.Parse("aaaaaaaa-3333-4333-8333-333333333333");

        private static Entity Update(params string[] columns)
        {
            var update = new Entity("al_remediationaction", ActionId);
            foreach (var column in columns)
            {
                update[column] = new OptionSetValue(Remediation.RecheckRequiredNo);
            }

            return update;
        }

        // --- The refusal -----------------------------------------------------------------

        [Theory]
        [InlineData("al_recheckrequired")]
        [InlineData("al_changesadvice")]
        public void An_adviser_patching_the_action_directly_is_refused(string column)
        {
            // The adviser's page PATCHes /_api/al_remediationactions(id). That reaches
            // Dataverse with no parent context at all, which is what fromContactPipeline
            // false means here.
            var refusal = RemediationResponseGuardPlugin.TcOnlyRefusal(Update(column), false);

            Assert.NotNull(refusal);
            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal);
            Assert.Contains("T&C Manager", refusal);
        }

        [Fact]
        public void The_refusal_does_not_depend_on_the_value_changing()
        {
            // Presence, not change - deliberately unlike the Completed freeze next door,
            // where a restated value is a dropped-response retry. Allowing a restatement
            // here would let the column be probed for what the supervisor answered.
            var restating = new Entity("al_remediationaction", ActionId)
            {
                ["al_recheckrequired"] = new OptionSetValue(Remediation.RecheckRequiredYes),
            };

            Assert.NotNull(RemediationResponseGuardPlugin.TcOnlyRefusal(restating, false));
        }

        [Fact]
        public void The_advisers_own_three_columns_still_pass()
        {
            var response = new Entity("al_remediationaction", ActionId)
            {
                ["al_adviserresponse"] = "Re-issued the suitability report.",
                ["al_evidencereference"] = "IO-99",
                ["al_clientcontactrequired"] = new OptionSetValue(120910794),
            };

            Assert.Null(RemediationResponseGuardPlugin.TcOnlyRefusal(response, false));
        }

        [Fact]
        public void The_sign_off_path_is_let_through()
        {
            // SignoffRequestPlugin.Apply runs inside the Update of the supervisor's own
            // contact row, and has checked their web role before it writes.
            Assert.Null(RemediationResponseGuardPlugin.TcOnlyRefusal(Update("al_recheckrequired"), true));
        }

        [Fact]
        public void The_two_column_lists_do_not_overlap()
        {
            // A column in both would be frozen at completion AND refused outright, which is
            // two rules arguing. The split is the whole change.
            Assert.DoesNotContain("al_recheckrequired", RemediationResponseGuardPlugin.ResponseColumns);
            Assert.DoesNotContain("al_changesadvice", RemediationResponseGuardPlugin.ResponseColumns);
            Assert.Contains("al_clientcontactrequired", RemediationResponseGuardPlugin.ResponseColumns);
        }

        // --- The write -------------------------------------------------------------------

        private static FakeOrganizationService Signing(params string[] roleNames)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, SignoffRequestPlugin.RequestAttr, "{}");

            svc.Seed(
                "al_remediationaction", ActionId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "statecode", new OptionSetValue(0));
            svc.Seed(
                "al_remediationaction", SiblingId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "statecode", new OptionSetValue(0));

            var rows = new List<Entity>();
            foreach (var name in roleNames)
            {
                var row = new Entity("contact", ContactId);
                row["role.name"] = new AliasedValue("powerpagecomponent", "name", name);
                rows.Add(row);
            }

            svc.FetchResults.Enqueue(new EntityCollection(rows));
            return svc;
        }

        private static string Request(int recheck, int changesAdvice)
        {
            return "{\"actionId\":\"" + ActionId.ToString("D") + "\",\"decision\":120910720"
                + ",\"recheckRequired\":" + recheck
                + ",\"changesAdvice\":" + changesAdvice + "}";
        }

        private static List<Entity> ActionWrites(FakeOrganizationService svc)
        {
            return svc.Updates.FindAll(u => u.LogicalName == "al_remediationaction");
        }

        [Fact]
        public void A_supervisor_signing_off_records_both_answers_on_the_whole_check()
        {
            // The paper form asks each question once, under a table of numbered issues, and
            // RecheckWaived already reads them across the check.
            var svc = Signing(WebRoleRegistry.TcSupervisorRole);

            SignoffRequestPlugin.Apply(
                svc, ContactId, Request(Remediation.RecheckRequiredNo, 120910799));

            var writes = ActionWrites(svc);
            Assert.Equal(2, writes.Count);
            foreach (var write in writes)
            {
                Assert.Equal(
                    Remediation.RecheckRequiredNo,
                    write.GetAttributeValue<OptionSetValue>("al_recheckrequired").Value);
                Assert.Equal(
                    120910799, write.GetAttributeValue<OptionSetValue>("al_changesadvice").Value);
            }
        }

        [Fact]
        public void The_answers_are_written_before_the_sign_off_is_created()
        {
            // SignoffProgressPlugin runs on the al_signoff Create and reads
            // al_recheckrequired to route the case. Written afterwards, the case would be
            // routed on the previous answer.
            var svc = Signing(WebRoleRegistry.TcSupervisorRole);

            SignoffRequestPlugin.Apply(
                svc, ContactId, Request(Remediation.RecheckRequiredYes, 0));

            Assert.NotEmpty(ActionWrites(svc));
            Assert.Single(svc.Creates);
            // The clear-down of the request column is the last write before the create, so
            // every action write precedes it.
            Assert.Equal("contact", svc.Updates[svc.Updates.Count - 1].LogicalName);
        }

        [Fact]
        public void A_contact_without_the_role_writes_no_answers_at_all()
        {
            var svc = Signing(WebRoleRegistry.AqsReviewerRole);

            Assert.Throws<InvalidPluginExecutionException>(
                () => SignoffRequestPlugin.Apply(
                    svc, ContactId, Request(Remediation.RecheckRequiredNo, 120910799)));

            Assert.Empty(ActionWrites(svc));
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void A_rejection_that_answers_nothing_leaves_the_stored_answers_alone()
        {
            // Zero is "the page sent none". A rejection does not ask these questions, and it
            // must not erase what a previous approval attempt recorded.
            var svc = Signing(WebRoleRegistry.TcSupervisorRole);

            SignoffRequestPlugin.Apply(svc, ContactId, Request(0, 0));

            Assert.Empty(ActionWrites(svc));
            Assert.Single(svc.Creates);
        }

        [Fact]
        public void The_other_legs_actions_are_left_alone()
        {
            // The two legs of a Tax-then-AQS route are separate remediations, and the Tax
            // leg's answer is not this check's - the same scoping RecheckWaived uses.
            var svc = Signing(WebRoleRegistry.TcSupervisorRole);
            svc.Seed(
                "al_remediationaction", OtherLegId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid",
                new EntityReference("al_reviewinstance", Guid.Parse("ffffffff-9999-4999-8999-999999999999")),
                "statecode", new OptionSetValue(0));

            SignoffRequestPlugin.Apply(
                svc, ContactId, Request(Remediation.RecheckRequiredNo, 0));

            var writes = ActionWrites(svc);
            Assert.Equal(2, writes.Count);
            Assert.DoesNotContain(writes, w => w.Id == OtherLegId);
        }
    }
}
