using System;
using System.Linq;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Owner decision, 2026-11-17: every error tells the user what failed and why, in plain
    /// words. A fault that is not one of our own refusals reads as
    /// "PREFIX: action could not be completed because reason. What to do." - no class name,
    /// exception type, table name or id - and the technical detail goes to the trace only.
    /// </summary>
    public class FaultMessagesFixTests
    {
        private sealed class ThrowingPlugin : PluginBase
        {
            private readonly Exception _error;

            public ThrowingPlugin(Exception error) : base(typeof(ThrowingPlugin))
            {
                _error = error;
            }

            protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
            {
                throw _error;
            }
        }

        private const string RecordId = "1f0e3cf3-2ea1-f111-b8dd-e4fade069307";

        private static FaultException<OrganizationServiceFault> Fault(int code, string message)
        {
            return new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = code, Message = message },
                new FaultReason(message));
        }

        private static InvalidPluginExecutionException Run(Exception thrown, FakeServiceProvider provider = null)
        {
            provider = provider ?? new FakeServiceProvider();
            return Assert.Throws<InvalidPluginExecutionException>(
                () => new ThrowingPlugin(thrown).Execute(provider));
        }

        private static void AssertNoInternals(string message)
        {
            Assert.DoesNotContain("OutcomeTesting.Plugins", message);
            Assert.DoesNotContain("ThrowingPlugin", message);
            Assert.DoesNotContain("Exception", message);
            Assert.DoesNotContain("OrganizationServiceFault", message);
            Assert.DoesNotContain("al_", message);
            Assert.DoesNotContain("1f0e3cf3", message);
            Assert.DoesNotContain("With Id", message);
            // The app and the portal cut a server sentence at the first quote or backslash.
            Assert.DoesNotContain("\"", message);
            Assert.DoesNotContain("\\", message);
        }

        [Theory]
        [InlineData(unchecked((int)0x80040220), "UNAUTHORIZED: ", "your Dataverse security role does not allow it")]
        [InlineData(unchecked((int)0x80040231), "UNAUTHORIZED: ", "your Dataverse security role does not allow it")]
        [InlineData(unchecked((int)0x80040217), "UNEXPECTED: ", "a record it needs no longer exists")]
        [InlineData(unchecked((int)0x80060891), "UNEXPECTED: ", "a record it needs no longer exists")]
        [InlineData(unchecked((int)0x80040237), "CONFLICT: ", "it has already been recorded")]
        [InlineData(unchecked((int)0x80060892), "CONFLICT: ", "it has already been recorded")]
        [InlineData(unchecked((int)0x80060882), "CONFLICT: ", "someone else changed it first")]
        [InlineData(unchecked((int)0x80044151), "UNEXPECTED: ", "it took too long")]
        [InlineData(unchecked((int)0x80044172), "UNEXPECTED: ", "it took too long")]
        [InlineData(unchecked((int)0x80041222), "UNEXPECTED: ", "it took too long")]
        [InlineData(0, "UNEXPECTED: ", "of a problem on the server")]
        [InlineData(unchecked((int)0x80040265), "UNEXPECTED: ", "of a problem on the server")]
        public void Maps_a_platform_fault_to_a_plain_reason(int code, string prefix, string reason)
        {
            var raw = "Entity 'al_reviewinstance' With Id = " + RecordId + " failed";
            var provider = new FakeServiceProvider();

            var thrown = Run(Fault(code, raw), provider);

            Assert.StartsWith(prefix + "Saving your change could not be completed because " + reason + ".",
                thrown.Message, StringComparison.Ordinal);
            AssertNoInternals(thrown.Message);

            // The detail is kept for diagnosis - on the trace and as the inner exception.
            Assert.Contains(provider.Traces, t => t.Contains("ThrowingPlugin") && t.Contains(raw));
            Assert.IsType<FaultException<OrganizationServiceFault>>(thrown.InnerException);
        }

        [Fact]
        public void Tells_the_user_what_to_do_for_each_reason()
        {
            Assert.EndsWith("Nothing was saved. Reload the page and try again.",
                Run(Fault(unchecked((int)0x80040217), "x")).Message);
            Assert.EndsWith("Reload the page to see it.",
                Run(Fault(unchecked((int)0x80060892), "x")).Message);
            Assert.EndsWith("Nothing was saved. Reload the page and try again.",
                Run(Fault(unchecked((int)0x80060882), "x")).Message);
            Assert.EndsWith("Nothing was saved. Please try again.",
                Run(Fault(unchecked((int)0x80044151), "x")).Message);
            Assert.EndsWith("Nothing was saved. Please try again, and if it happens again, report it with the time it happened.",
                Run(Fault(0, "x")).Message);
        }

        [Fact]
        public void A_privilege_denial_found_only_by_its_text_still_reads_as_a_role_problem()
        {
            var thrown = Run(Fault(0, "SecLib::CheckPrivilege failed. User: " + RecordId + ", PrivilegeName: prvReadal_case"));

            Assert.StartsWith("UNAUTHORIZED: Saving your change could not be completed because your Dataverse security role",
                thrown.Message, StringComparison.Ordinal);
            AssertNoInternals(thrown.Message);
        }

        [Fact]
        public void A_timeout_exception_reads_as_too_long()
        {
            var thrown = Run(new TimeoutException("The operation has timed out after 120000 ms"));

            Assert.Equal(
                "UNEXPECTED: Saving your change could not be completed because it took too long. Nothing was saved. Please try again.",
                thrown.Message);
        }

        [Theory]
        [InlineData(typeof(NullReferenceException))]
        [InlineData(typeof(InvalidOperationException))]
        [InlineData(typeof(FormatException))]
        public void Any_other_exception_reads_as_a_server_problem_with_its_detail_traced(Type errorType)
        {
            var error = (Exception)Activator.CreateInstance(errorType, new object[] { "Entity 'al_outcome' With Id = " + RecordId });
            var provider = new FakeServiceProvider();

            var thrown = Run(error, provider);

            Assert.StartsWith("UNEXPECTED: Saving your change could not be completed because of a problem on the server",
                thrown.Message, StringComparison.Ordinal);
            AssertNoInternals(thrown.Message);
            Assert.Contains(provider.Traces, t => t.Contains("ThrowingPlugin") && t.Contains(errorType.Name) && t.Contains(RecordId));
            Assert.Same(error, thrown.InnerException);
        }

        [Theory]
        [InlineData("VALIDATION: Enter a reason before saving.")]
        [InlineData("PRECONDITION: Complete all questions marked Required before submitting.")]
        [InlineData("UNAUTHORIZED: Only the adviser who owns this action can complete it.")]
        [InlineData("CONFLICT: Somebody else changed this case while you were working on it.")]
        [InlineData("NOTFOUND: That case does not exist.")]
        public void Passes_our_own_prefixed_refusals_through_unchanged(string refusal)
        {
            Assert.Equal(refusal, Run(new InvalidPluginExecutionException(refusal)).Message);
            Assert.Equal(refusal, Run(Fault(unchecked((int)0x80040265), refusal)).Message);
        }

        [Fact]
        public void A_friendly_unexpected_message_from_a_called_command_is_carried_not_rewrapped()
        {
            var inner = "UNEXPECTED: Allocating the case could not be completed because it took too long. Nothing was saved. Please try again.";

            Assert.Equal(inner, Run(Fault(unchecked((int)0x80040265), inner)).Message);
        }

        [Fact]
        public void Names_the_action_of_the_plug_in_that_failed()
        {
            var message = FaultMessages.ForException(typeof(SubmitReviewPlugin), new InvalidOperationException("x"));

            Assert.StartsWith("UNEXPECTED: Submitting the check could not be completed because", message, StringComparison.Ordinal);
        }

        /// <summary>
        /// Two sign-off decisions racing on the same round collide on the al_SignoffCodeKey
        /// alternate key. The loser's decision is already recorded by the winner, so it reads
        /// as that, not as a crash.
        /// </summary>
        [Fact]
        public void A_losing_sign_off_racer_is_told_it_is_already_recorded()
        {
            var raw = "Entity Key al_SignoffCodeKey violated. A record with the same value for al_signoffcode already exists.";

            var message = FaultMessages.ForFault(typeof(SignoffRequestPlugin), Fault(unchecked((int)0x80060892), raw));

            Assert.Equal(
                "CONFLICT: Signing off the remediation could not be completed because it has already been recorded. Reload the page to see it.",
                message);
        }

        /// <summary>A new plug-in cannot ship without saying, in plain words, what it does.</summary>
        [Fact]
        public void Every_plug_in_in_the_assembly_has_an_action_phrase()
        {
            var missing = typeof(PluginBase).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(PluginBase).IsAssignableFrom(t))
                .Where(t => !FaultMessages.Actions.ContainsKey(t))
                .Select(t => t.Name)
                .ToArray();

            Assert.True(missing.Length == 0, "No action phrase in FaultMessages.Actions for: " + string.Join(", ", missing));
            Assert.True(FaultMessages.Actions.Count >= 58);
            Assert.All(FaultMessages.Actions.Values, phrase =>
            {
                Assert.False(string.IsNullOrWhiteSpace(phrase));
                Assert.True(char.IsUpper(phrase[0]), phrase);
                AssertNoInternals(phrase);
            });
        }
    }
}
