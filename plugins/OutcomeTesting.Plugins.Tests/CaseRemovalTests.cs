using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Removing a case added in error (2026-11-06). Case data is kept, so removal is the move
    /// to No Check Required: it must say why, and nobody may go on holding the case.
    /// </summary>
    public class CaseRemovalTests
    {
        private const string CaseEntity = "al_outcomecase";
        private const string StatusAttr = "al_casestatus";
        private const string AssignmentEntity = "al_caseassignment";
        private static readonly Guid CaseId = new Guid("11111111-1111-4111-8111-111111111111");
        private static readonly Guid AssignmentId = new Guid("33333333-3333-4333-8333-333333333333");

        private static Entity Save(int status)
        {
            var update = new Entity(CaseEntity, CaseId);
            update[StatusAttr] = new OptionSetValue(status);
            return update;
        }

        private static FakeOrganizationService WithActiveAssignment()
        {
            var service = new FakeOrganizationService();
            service.Seed(
                AssignmentEntity, AssignmentId,
                "al_outcomecaseid", new EntityReference(CaseEntity, CaseId),
                "al_isactive", true);
            return service;
        }

        [Fact]
        public void A_removal_without_a_reason_is_refused()
        {
            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => UpdateCaseDetailsPlugin.EnsureRemovalReason(Save(CaseLifecycle.NoCheckRequired), false));

            Assert.Contains("Give a reason", refusal.Message);
        }

        [Fact]
        public void A_removal_with_a_reason_is_accepted()
        {
            UpdateCaseDetailsPlugin.EnsureRemovalReason(Save(CaseLifecycle.NoCheckRequired), true);
        }

        [Fact]
        public void Any_other_status_still_needs_no_reason()
        {
            UpdateCaseDetailsPlugin.EnsureRemovalReason(Save(CaseLifecycle.Queued), false);
        }

        [Fact]
        public void A_removal_releases_the_allocation_and_keeps_its_row()
        {
            var service = WithActiveAssignment();
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.ReleaseIfRemoved(service, CaseId, Save(CaseLifecycle.NoCheckRequired), changes);

            var row = service.Row(AssignmentEntity, AssignmentId);
            Assert.False(row.GetAttributeValue<bool>("al_isactive"));
            Assert.NotNull(row.GetAttributeValue<DateTime?>("al_releasedon"));
            Assert.Contains(changes, c => c.StartsWith("Allocation released", StringComparison.Ordinal));
        }

        [Fact]
        public void Any_other_save_leaves_the_allocation_alone()
        {
            var service = WithActiveAssignment();
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.ReleaseIfRemoved(service, CaseId, Save(CaseLifecycle.Queued), changes);

            Assert.True(service.Row(AssignmentEntity, AssignmentId).GetAttributeValue<bool>("al_isactive"));
            Assert.Empty(changes);
        }

        // A checker may still have the review open when the case is removed.
        [Theory]
        [InlineData(CaseLifecycle.Closed)]
        [InlineData(CaseLifecycle.AwaitingRemediation)]
        [InlineData(CaseLifecycle.Queued)]
        public void A_removed_case_refuses_the_move_a_submit_makes(int destination)
        {
            var service = new FakeOrganizationService();
            service.Seed(CaseEntity, CaseId, StatusAttr, new OptionSetValue(CaseLifecycle.NoCheckRequired));

            Assert.Throws<InvalidPluginExecutionException>(() => CaseTransitions.MoveThrough(
                service,
                CaseId,
                OutcomeRules.HopsFor(CaseLifecycle.NoCheckRequired, destination)));

            Assert.Equal(
                CaseLifecycle.NoCheckRequired,
                service.Row(CaseEntity, CaseId).GetAttributeValue<OptionSetValue>(StatusAttr).Value);
        }

        [Theory]
        [InlineData(CaseLifecycle.Submitted)]
        [InlineData(CaseLifecycle.Closed)]
        public void A_graded_case_cannot_be_removed(int status)
        {
            Assert.False(CaseLifecycle.IsAllowed(status, CaseLifecycle.NoCheckRequired));
        }
    }
}
