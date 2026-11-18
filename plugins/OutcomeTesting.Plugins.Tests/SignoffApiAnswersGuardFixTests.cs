using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// al_SignOffRemediation now takes the supervisor's recheck answers (2026-11-17). The
    /// response guard refused those two columns unless a portal sign-off was in flight, so the
    /// API's own write was rolled back. It writes as SYSTEM, and a SYSTEM write is let through.
    /// </summary>
    public class SignoffApiAnswersGuardFixTests
    {
        private static readonly Guid ActionId = Guid.Parse("aaaaaaaa-9999-4999-8999-999999999999");

        private static void RunGuard(bool asSystem)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_remediationaction", ActionId,
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted));

            var update = new Entity("al_remediationaction", ActionId)
            {
                ["al_recheckrequired"] = new OptionSetValue(Remediation.RecheckRequiredNo),
            };

            var provider = new FakeServiceProvider(svc);
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "al_remediationaction";
            provider.Context.PrimaryEntityId = ActionId;
            provider.Context.InputParameters["Target"] = update;
            if (asSystem)
            {
                // As DEV traced it on 2026-11-17: a CreateOrganizationService(null) write names SYSTEM in both.
                provider.Context.UserId = FakeOrganizationService.SystemUserId;
                provider.Context.InitiatingUserId = FakeOrganizationService.SystemUserId;
            }

            new RemediationResponseGuardPlugin(null, null).Execute(provider);
        }

        [Fact]
        public void The_sign_off_commands_system_write_of_the_recheck_answer_is_allowed()
        {
            RunGuard(asSystem: true);
        }

        [Fact]
        public void An_impersonating_caller_is_not_taken_for_system()
        {
            var context = new FakePluginExecutionContext { UserId = Guid.NewGuid(), InitiatingUserId = Guid.NewGuid() };
            Assert.False(WriteProvenance.IsSystemWrite(new FakeOrganizationService(), context));
        }

        [Fact]
        public void The_same_write_as_the_caller_with_no_portal_sign_off_is_still_refused()
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(() => RunGuard(asSystem: false));
            Assert.Contains("T&C Manager", ex.Message);
        }
    }
}
