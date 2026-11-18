using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Audit 2026-11-17: the "no security role" refusal quoted the role name with double
    /// quotes. The app ends a refusal sentence at the first double quote, because that is
    /// where the JSON body's machine detail starts, so the user read "...Ask an
    /// administrator to assign" and nothing more.
    /// </summary>
    public class AssignCaseQuoteFixTests
    {
        private const string Email = "checker@example.com";
        private static readonly Guid UserId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
        private static readonly Guid BasicUserRole = Guid.Parse("cccccccc-3333-4333-8333-333333333331");

        [Fact]
        public void The_no_role_refusal_names_the_role_without_double_quotes()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", UserId, "internalemailaddress", Email, "fullname", "Ada Checker", "isdisabled", false);
            svc.Seed("contact", Guid.NewGuid(), "emailaddress1", Email, "fullname", "Ada Checker");
            svc.Seed("role", BasicUserRole, "name", "Basic User");
            svc.Seed("systemuserroles", Guid.NewGuid(), "systemuserid", UserId, "roleid", BasicUserRole);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => AssignCasePlugin.ResolveAssignee(svc, Email));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, ex.Message);
            Assert.DoesNotContain("\"", ex.Message);
            Assert.Contains("assign the Outcome Testing App User role alongside Basic User, then allocate again.", ex.Message);
        }
    }
}
