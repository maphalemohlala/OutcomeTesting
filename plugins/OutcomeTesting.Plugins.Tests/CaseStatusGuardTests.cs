using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// F53. The AD-057 ordering lived only in <see cref="CaseTransitions.MoveThrough"/>,
    /// which only the commands call - so a direct write to <c>al_casestatus</c> moved a case
    /// from Awaiting Remediation straight to Closed in one PATCH, skipping sign-off and
    /// recheck. Not an administrator-only path either: the shipped app user role grants
    /// <c>prvWriteal_OutcomeCase</c> at Global.
    ///
    /// These cover the rule AND the plug-in that applies it. Covering only the rule is how
    /// F50 shipped a correct helper nothing called.
    /// </summary>
    public class CaseStatusGuardTests
    {
        private static readonly Guid CaseId = Guid.Parse("c5c5c5c5-0001-4001-8001-c5c5c5c5c5c5");

        private static FakeOrganizationService At(int status)
        {
            var service = new FakeOrganizationService();
            service.Seed("al_outcomecase", CaseId, "al_casestatus", new OptionSetValue(status));
            return service;
        }

        private static Entity Moving(int to)
        {
            return new Entity("al_outcomecase", CaseId) { ["al_casestatus"] = new OptionSetValue(to) };
        }

        [Fact]
        public void Refuses_the_jump_that_was_found_in_dev()
        {
            // The exact call made on 2026-09-21: Awaiting Remediation -> Closed, one PATCH,
            // which returned 204 before this guard existed.
            var service = At(CaseLifecycle.AwaitingRemediation);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseStatusGuardPlugin.Guard(service, Moving(CaseLifecycle.Closed)));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, error.Message);
        }

        [Fact]
        public void Allows_a_hop_the_lifecycle_describes()
        {
            // The guard must not break the commands: MoveThrough only ever makes legal hops,
            // so every command write has to pass straight through it.
            var service = At(CaseLifecycle.AwaitingRemediation);

            CaseStatusGuardPlugin.Guard(service, Moving(CaseLifecycle.RemediationInProgress));
        }

        [Fact]
        public void Allows_a_write_that_leaves_the_status_where_it_is()
        {
            // An update carrying the status unchanged beside other columns is not a
            // transition and must not be refused.
            var service = At(CaseLifecycle.AwaitingRemediation);

            CaseStatusGuardPlugin.Guard(service, Moving(CaseLifecycle.AwaitingRemediation));
        }

        [Fact]
        public void Ignores_an_update_that_does_not_touch_the_status()
        {
            var service = At(CaseLifecycle.AwaitingRemediation);

            CaseStatusGuardPlugin.Guard(
                service,
                new Entity("al_outcomecase", CaseId) { ["al_clientname"] = "Someone" });
        }

        [Fact]
        public void Refuses_a_status_being_cleared()
        {
            var service = At(CaseLifecycle.AwaitingRemediation);

            var target = new Entity("al_outcomecase", CaseId);
            target["al_casestatus"] = null;

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseStatusGuardPlugin.Guard(service, target));

            Assert.Contains("no status", error.Message);
        }

        [Fact]
        public void The_plug_in_itself_applies_the_guard()
        {
            // The wiring, not the rule. Reverting the Guard call in ExecuteDataversePlugin
            // leaves every test above green and reddens this one alone - which is the whole
            // reason it exists.
            var service = At(CaseLifecycle.AwaitingRemediation);
            var provider = new FakeServiceProvider(service);

            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "al_outcomecase";
            provider.Context.InputParameters["Target"] = Moving(CaseLifecycle.Closed);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => new CaseStatusGuardPlugin(null, null).Execute(provider));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, error.Message);
        }

        [Fact]
        public void The_plug_in_lets_a_legal_hop_through()
        {
            var service = At(CaseLifecycle.AwaitingRemediation);
            var provider = new FakeServiceProvider(service);

            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "al_outcomecase";
            provider.Context.InputParameters["Target"] = Moving(CaseLifecycle.RemediationInProgress);

            new CaseStatusGuardPlugin(null, null).Execute(provider);
        }

        [Fact]
        public void The_plug_in_ignores_a_target_that_is_not_a_case()
        {
            var provider = new FakeServiceProvider(At(CaseLifecycle.AwaitingRemediation));

            provider.Context.MessageName = "Update";
            provider.Context.InputParameters["Target"] =
                new Entity("al_reviewinstance", Guid.NewGuid()) { ["al_casestatus"] = new OptionSetValue(CaseLifecycle.Closed) };

            new CaseStatusGuardPlugin(null, null).Execute(provider);
        }
    }
}
