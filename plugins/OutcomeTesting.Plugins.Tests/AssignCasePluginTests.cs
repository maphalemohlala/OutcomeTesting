using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AssignCasePlugin identity resolution and key construction (OD-029, AD-040).
    ///
    /// Identity resolution is the part worth testing hardest. The two front ends read
    /// different identity systems - the Code App reads ownerid, Power Pages resolves
    /// through Contact (AD-047) - so an allocation that resolves only one of them leaves a
    /// case that looks allocated in one front end and is invisible in the other. The
    /// command refuses rather than half-assigning, and these tests are what hold it to that.
    /// </summary>
    public class AssignCasePluginTests
    {
        private const string Email = "checker@example.com";

        private static readonly Guid UserId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
        private static readonly Guid ContactId = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222");

        private static FakeOrganizationService Both()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", UserId, "internalemailaddress", Email, "fullname", "Ada Checker", "isdisabled", false);
            svc.Seed("contact", ContactId, "emailaddress1", Email, "fullname", "Ada Checker");
            svc.Seed("systemuser", UserId, "internalemailaddress", Email, "fullname", "Ada Checker");
            svc.Seed("systemuser", OtherUser, "internalemailaddress", "bo@example.com", "fullname", "Bo Checker");
            return svc;
        }

        [Fact]
        public void Resolves_both_identities_from_one_work_email()
        {
            var assignee = AssignCasePlugin.ResolveAssignee(Both(), Email);

            Assert.Equal(UserId, assignee.UserId);
            Assert.Equal(ContactId, assignee.ContactId);
            Assert.Equal("Ada Checker", assignee.UserName);
        }

        [Fact]
        public void Matches_a_user_on_domainname_when_internalemailaddress_is_unset()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", UserId, "domainname", Email, "fullname", "Ada Checker", "isdisabled", false);
            svc.Seed("contact", ContactId, "emailaddress1", Email);

            Assert.Equal(UserId, AssignCasePlugin.ResolveAssignee(svc, Email).UserId);
        }

        [Fact]
        public void Refuses_when_no_dataverse_user_holds_the_email()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "emailaddress1", Email);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => AssignCasePlugin.ResolveAssignee(svc, Email));

            Assert.Contains("PRECONDITION:", ex.Message);
            Assert.Contains("No Dataverse user", ex.Message);
        }

        [Fact]
        public void Refuses_when_no_portal_contact_holds_the_email()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", UserId, "internalemailaddress", Email, "isdisabled", false);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => AssignCasePlugin.ResolveAssignee(svc, Email));

            Assert.Contains("PRECONDITION:", ex.Message);
            Assert.Contains("portal contact", ex.Message);
        }

        /// <summary>AD-058: deactivation is what withdraws access, so it must withdraw work too.</summary>
        [Fact]
        public void Refuses_a_disabled_user()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", UserId, "internalemailaddress", Email, "isdisabled", true);
            svc.Seed("contact", ContactId, "emailaddress1", Email);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => AssignCasePlugin.ResolveAssignee(svc, Email));

            Assert.Contains("disabled", ex.Message);
        }

        [Fact]
        public void Assignment_code_is_stable_for_the_same_allocation()
        {
            var caseId = Guid.NewGuid();
            var reviewId = Guid.NewGuid();

            Assert.Equal(
                AssignCasePlugin.BuildAssignmentCode(caseId, reviewId, UserId),
                AssignCasePlugin.BuildAssignmentCode(caseId, reviewId, UserId));
        }

        [Fact]
        public void Assignment_code_differs_per_assignee_so_a_reassignment_is_a_new_row()
        {
            var caseId = Guid.NewGuid();
            var reviewId = Guid.NewGuid();

            Assert.NotEqual(
                AssignCasePlugin.BuildAssignmentCode(caseId, reviewId, UserId),
                AssignCasePlugin.BuildAssignmentCode(caseId, reviewId, ContactId));
        }

        [Fact]
        public void Assignment_code_fits_the_hundred_character_column()
        {
            var code = AssignCasePlugin.BuildAssignmentCode(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

            Assert.True(code.Length <= 100, "code was " + code.Length + " characters");
        }

        [Fact]
        public void Assignment_name_survives_a_case_with_no_reference()
        {
            Assert.Equal("Case -> Ada Checker", AssignCasePlugin.BuildAssignmentName(null, "Ada Checker"));
        }

        [Fact]
        public void Assignment_name_fits_the_hundred_character_column()
        {
            var name = AssignCasePlugin.BuildAssignmentName(new string('r', 90), new string('n', 90));

            Assert.True(name.Length <= 100, "name was " + name.Length + " characters");
        }

        /// <summary>
        /// The work email column on systemuser is <c>internalemailaddress</c>. The filter
        /// named <c>internalemailid</c>, which does not exist, so Dataverse rejected the
        /// query outright and EVERY allocation failed — the al_AssignCase command and the
        /// portal claim alike. It went unnoticed because no case had ever been allocated in
        /// DEV, and because these tests seeded the same wrong name: the fake has no metadata
        /// to contradict it, so the test agreed with the bug.
        ///
        /// This case seeds the real shape a Dataverse user has — a mailbox address and a
        /// separate UPN — so resolution has to work off the mailbox column alone.
        /// </summary>
        [Fact]
        public void Resolves_a_user_whose_mailbox_and_upn_differ()
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                "systemuser", UserId,
                "internalemailaddress", Email,
                "domainname", "ada.checker@tenant.onmicrosoft.com",
                "fullname", "Ada Checker",
                "isdisabled", false);
            svc.Seed("contact", ContactId, "emailaddress1", Email, "fullname", "Ada Checker");

            var assignee = AssignCasePlugin.ResolveAssignee(svc, Email);

            Assert.Equal(UserId, assignee.UserId);
            Assert.Equal(ContactId, assignee.ContactId);
        }

        [Fact]
        public void Stamps_the_allocated_checker_onto_the_case()
        {
            // The checklist header and the case detail read al_checkername, not the
            // assignment row. Before 2026-09-10 only the portal self-claim wrote it, so a
            // case allocated from the app read as unchecked everywhere but the history.
            var caseId = Guid.Parse("cccccccc-3333-4333-8333-333333333333");
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", caseId, "al_casereference", "IO-TEST-010");

            AssignCasePlugin.StampCheckerName(svc, caseId, "Ada Checker");

            Assert.Equal(
                "Ada Checker",
                svc.Row("al_outcomecase", caseId).GetAttributeValue<string>("al_checkername"));
        }

        [Fact]
        public void Replaces_the_checker_name_a_previous_allocation_left()
        {
            // Reallocation is the case this exists for: the header must name whoever holds
            // the check now, including after a check has started.
            var caseId = Guid.Parse("dddddddd-4444-4444-8444-444444444444");
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", caseId, "al_checkername", "Prior Checker");

            AssignCasePlugin.StampCheckerName(svc, caseId, "Ada Checker");

            Assert.Equal(
                "Ada Checker",
                svc.Row("al_outcomecase", caseId).GetAttributeValue<string>("al_checkername"));
        }

        // --- Reallocating to someone who held the check before -------------------------

        private static readonly Guid AllocCase = Guid.Parse("eeeeeeee-5555-4555-8555-555555555555");
        private static readonly Guid AllocReview = Guid.Parse("ffffffff-6666-4666-8666-666666666666");
        private static readonly Guid OtherUser = Guid.Parse("11111111-7777-4777-8777-777777777777");

        private static AssignCasePlugin.Assignee Person(Guid userId, string name)
        {
            return new AssignCasePlugin.Assignee { UserId = userId, ContactId = ContactId, UserName = name };
        }

        /// <summary>A case and a contact, so the allocation notification can be built.</summary>
        private static FakeOrganizationService AllocationWorld()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", AllocCase, "al_casereference", "IO-TEST-011");
            svc.Seed("contact", ContactId, "emailaddress1", Email, "fullname", "Ada Checker");
            svc.Seed("systemuser", UserId, "internalemailaddress", Email, "fullname", "Ada Checker");
            svc.Seed("systemuser", OtherUser, "internalemailaddress", "bo@example.com", "fullname", "Bo Checker");
            return svc;
        }

        private static int NotificationRows(FakeOrganizationService svc)
        {
            return svc.Creates.Where(c => c.LogicalName == "al_notification").Count();
        }

        private static int AssignmentRows(FakeOrganizationService svc)
        {
            return svc.RetrieveMultiple(new Microsoft.Xrm.Sdk.Query.QueryExpression("al_caseassignment"))
                .Entities.Count;
        }

        [Fact]
        public void Writes_an_assignment_row_for_a_first_allocation()
        {
            var svc = AllocationWorld();

            var id = AssignCasePlugin.AllocateAssignment(
                svc, AllocCase, AllocReview, Person(UserId, "Ada Checker"), "IO-TEST-011", null, null);

            var row = svc.Row("al_caseassignment", id);
            Assert.True(row.GetAttributeValue<bool>("al_isactive"));
            Assert.Equal(
                AssignCasePlugin.BuildAssignmentCode(AllocCase, AllocReview, UserId),
                row.GetAttributeValue<string>("al_caseassignmentcode"));
        }

        [Fact]
        public void Reallocating_to_a_previous_holder_reuses_their_row()
        {
            // The alternate key is unique per (case, review, person), so a second create
            // for the same three faulted: "Assignment code key violated". Allocating back
            // to someone who held the check before is ordinary - a manager moves work to
            // cover an absence and moves it back - and it was impossible.
            var svc = AllocationWorld();
            var ada = Person(UserId, "Ada Checker");

            var first = AssignCasePlugin.AllocateAssignment(
                svc, AllocCase, AllocReview, ada, "IO-TEST-011", null, null);

            // Moved to someone else, which releases Ada's row.
            AssignCasePlugin.AllocateAssignment(
                svc, AllocCase, AllocReview, Person(OtherUser, "Bo Checker"), "IO-TEST-011", null, null);
            AssignCasePlugin.ReleasePriorAssignments(svc, AllocCase);

            var again = AssignCasePlugin.AllocateAssignment(
                svc, AllocCase, AllocReview, ada, "IO-TEST-011", null, "Cover ended");

            Assert.Equal(first, again);
            Assert.Equal(2, AssignmentRows(svc));

            var row = svc.Row("al_caseassignment", again);
            Assert.True(row.GetAttributeValue<bool>("al_isactive"));
            Assert.Null(row.GetAttributeValue<DateTime?>("al_releasedon"));
            Assert.Equal("Cover ended", row.GetAttributeValue<string>("al_assignmentreason"));
        }

        [Fact]
        public void Tells_the_checker_again_when_the_check_comes_back_to_them()
        {
            // Project owner, 2026-09-10: "they need to get the email each time even if it's
            // a reallocation". Reusing the row means no create fires, so the notification is
            // queued explicitly - and the outbox code carries al_assignedon so the second
            // allocation is a new notification rather than one already queued.
            var svc = AllocationWorld();
            var ada = Person(UserId, "Ada Checker");

            AssignCasePlugin.AllocateAssignment(
                svc, AllocCase, AllocReview, ada, "IO-TEST-011", null, null);
            var afterFirst = NotificationRows(svc);

            AssignCasePlugin.ReleasePriorAssignments(svc, AllocCase);
            AssignCasePlugin.AllocateAssignment(
                svc, AllocCase, AllocReview, ada, "IO-TEST-011", null, null, Guid.NewGuid());

            Assert.Equal(afterFirst + 1, NotificationRows(svc));
        }

        [Fact]
        public void A_different_person_gets_their_own_row()
        {
            var svc = AllocationWorld();

            var ada = AssignCasePlugin.AllocateAssignment(
                svc, AllocCase, AllocReview, Person(UserId, "Ada Checker"), "IO-TEST-011", null, null);
            var bo = AssignCasePlugin.AllocateAssignment(
                svc, AllocCase, AllocReview, Person(OtherUser, "Bo Checker"), "IO-TEST-011", null, null);

            Assert.NotEqual(ada, bo);
            Assert.Equal(2, AssignmentRows(svc));
        }
    }
}
