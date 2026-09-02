using System;
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
            svc.Seed("systemuser", UserId, "internalemailid", Email, "fullname", "Ada Checker", "isdisabled", false);
            svc.Seed("contact", ContactId, "emailaddress1", Email, "fullname", "Ada Checker");
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
        public void Matches_a_user_on_domainname_when_internalemailid_is_unset()
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
            svc.Seed("systemuser", UserId, "internalemailid", Email, "isdisabled", false);

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
            svc.Seed("systemuser", UserId, "internalemailid", Email, "isdisabled", true);
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
    }
}
