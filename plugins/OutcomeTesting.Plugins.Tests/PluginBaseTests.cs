using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What a caller is told when a plug-in fails.
    ///
    /// This is not a detail of the pipeline. Power Pages returns an
    /// <see cref="InvalidPluginExecutionException"/>'s message to the browser, and reports
    /// anything else as the opaque "A Common Data Service error occurred" with error code
    /// 9004010D and no detail at all. So an exception type that escapes here is the
    /// difference between a portal user reading what went wrong and reading nothing.
    ///
    /// Reported 2026-09-10: a submit refused with exactly that code, and the page could only
    /// fall back to "We could not submit this review... quoting status 400", which names
    /// neither the cause nor anything support could act on.
    /// </summary>
    public class PluginBaseTests
    {
        /// <summary>A plug-in whose only behaviour is to throw what the test asks for.</summary>
        private sealed class ThrowingPlugin : PluginBase
        {
            private readonly Exception _error;

            public ThrowingPlugin(Exception error)
                : base(typeof(ThrowingPlugin))
            {
                _error = error;
            }

            protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
            {
                throw _error;
            }
        }

        private static InvalidPluginExecutionException Run(Exception thrown)
        {
            var provider = new FakeServiceProvider();
            return Assert.Throws<InvalidPluginExecutionException>(
                () => new ThrowingPlugin(thrown).Execute(provider));
        }

        [Fact]
        public void Carries_a_business_message_out_unchanged()
        {
            // The prefixed messages the pages branch on. Wrapping one of these would put
            // another sentence in front of the prefix and break every client that reads it.
            var message = "PRECONDITION: Complete all questions marked Required before submitting.";

            var thrown = Run(new InvalidPluginExecutionException(message));

            Assert.Equal(message, thrown.Message);
        }

        /// <summary>
        /// A privilege denial is an authorisation ANSWER, and reads like one (2026-09-21).
        ///
        /// APP-003 found what the platform actually says: "SecLib::CheckPrivilege failed.
        /// User: 1fae3cf3-..., PrivilegeName: prvReadEntity, PrivilegeId: a3311f47-...",
        /// delivered to a browser behind "UNEXPECTED: OutcomeTesting.Plugins.ImportCasesPlugin
        /// could not complete." - a plug-in class name, two GUIDs, a privilege name and a
        /// platform stack phrase, with "occoured" misspelled by the platform. Nobody shown
        /// that can act on it, and it reads as a broken product rather than as a permission
        /// they do not have.
        ///
        /// Nothing is swallowed. It is still thrown, the transaction still aborts (AD-109 is
        /// about ABSORBING a fault and carrying on, which this does not do), and the raw
        /// fault still reaches the trace log and InnerException where an administrator looks.
        /// </summary>
        [Fact]
        public void Turns_a_privilege_denial_into_something_a_person_can_act_on()
        {
            var raw = "SecLib::CheckPrivilege failed. User: 1fae3cf3-2ea1-f111-b8dd-e4fade069307, "
                + "PrivilegeName: prvReadEntity, PrivilegeId: a3311f47-0f2c-4b6e-9b1f-3f2d0a7c1e55";
            var fault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = raw },
                new FaultReason(raw));

            var thrown = Run(fault);

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, thrown.Message, StringComparison.Ordinal);

            // None of the internals the raw fault leaked.
            Assert.DoesNotContain("ThrowingPlugin", thrown.Message);
            Assert.DoesNotContain("SecLib", thrown.Message);
            Assert.DoesNotContain("prvReadEntity", thrown.Message);
            Assert.DoesNotContain("1fae3cf3", thrown.Message);
            Assert.DoesNotContain("UNEXPECTED", thrown.Message);

            // And it still says which of the two role systems refused them, which is the
            // distinction APP-003 turned on: the account held the application role and not
            // the Dataverse one.
            Assert.Contains("Dataverse security role", thrown.Message);

            // The detail is kept where diagnosis happens, not on the screen.
            Assert.Same(fault, thrown.InnerException);
        }

        /// <summary>
        /// The other phrasing the platform uses for the same thing.
        /// </summary>
        [Fact]
        public void Recognises_the_missing_privilege_phrasing_too()
        {
            var raw = "Principal user (Id=1fae3cf3-2ea1-f111-b8dd-e4fade069307, type=8) "
                + "is missing prvCreateal_outcomecase privilege";
            var fault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = raw },
                new FaultReason(raw));

            var thrown = Run(fault);

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, thrown.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("prvCreateal_outcomecase", thrown.Message);
        }

        /// <summary>
        /// A refusal thrown by one command reaches a command that CALLED it as a platform
        /// fault, not as an InvalidPluginExecutionException - and it is still a refusal.
        ///
        /// PRT-093, 2026-09-21: signing off an already-approved action answered "UNEXPECTED:
        /// OutcomeTesting.Plugins.SignoffRequestPlugin could not complete.
        /// OrganizationServiceFault: PRECONDITION: This remediation action has already been
        /// approved." The rule was enforced correctly and reported as a crash. Worse, the
        /// message no longer STARTED with the prefix, so the clients that branch on
        /// PRECONDITION/CONFLICT/UNAUTHORIZED could not see it.
        /// </summary>
        [Theory]
        [InlineData("PRECONDITION: This remediation action has already been approved.")]
        [InlineData("CONFLICT: Somebody else changed this case while you were working on it.")]
        [InlineData("UNAUTHORIZED: Only the adviser who owns this remediation action can complete it.")]
        [InlineData("VALIDATION: Enter a reason before saving.")]
        [InlineData("NOTFOUND: That case does not exist.")]
        public void Carries_a_refusal_out_unchanged_even_when_it_crossed_a_service_call(string refusal)
        {
            var fault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = refusal },
                new FaultReason(refusal));

            var thrown = Run(fault);

            Assert.Equal(refusal, thrown.Message);
        }

        /// <summary>
        /// Two commands deep wrapped it twice. Taking the message from the first prefix
        /// onwards collapses any depth of packaging back to the sentence that was thrown.
        /// </summary>
        [Fact]
        public void Unwraps_a_refusal_however_many_layers_are_around_it()
        {
            var refusal = "PRECONDITION: This remediation action has already been approved.";
            var nested = "UNEXPECTED: OutcomeTesting.Plugins.OuterPlugin could not complete. "
                + "OrganizationServiceFault: UNEXPECTED: OutcomeTesting.Plugins.InnerPlugin "
                + "could not complete. OrganizationServiceFault: " + refusal;
            var fault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = nested },
                new FaultReason(nested));

            var thrown = Run(fault);

            Assert.Equal(refusal, thrown.Message);
        }

        /// <summary>
        /// And a fault that is genuinely unexpected still says so, naming the plug-in and the
        /// fault. Cleaning up refusals must not quietly swallow the diagnostics that make an
        /// unforeseen failure findable - that would trade one silent failure for another.
        /// </summary>
        [Fact]
        public void Still_names_the_plug_in_for_a_genuinely_unexpected_fault()
        {
            var raw = "Entity 'al_remediationaction' With Id = 0000 Does Not Exist";
            var fault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = raw },
                new FaultReason(raw));

            var thrown = Run(fault);

            Assert.StartsWith("UNEXPECTED:", thrown.Message, StringComparison.Ordinal);
            Assert.Contains("ThrowingPlugin", thrown.Message);
            Assert.Contains(raw, thrown.Message);
        }

        [Theory]
        [InlineData(typeof(NullReferenceException))]
        [InlineData(typeof(InvalidOperationException))]
        [InlineData(typeof(FormatException))]
        [InlineData(typeof(ArgumentException))]
        public void Names_the_plug_in_and_the_error_for_anything_else(Type errorType)
        {
            // The gap this closes. Only FaultException<OrganizationServiceFault> used to be
            // caught, so every one of these left the pipeline as a system fault and reached
            // the browser as 9004010D with nothing in it.
            var error = (Exception)Activator.CreateInstance(errorType, new object[] { "the underlying detail" });

            var thrown = Run(error);

            Assert.StartsWith("UNEXPECTED:", thrown.Message);
            Assert.Contains("ThrowingPlugin", thrown.Message);
            Assert.Contains(errorType.Name, thrown.Message);
            Assert.Contains("the underlying detail", thrown.Message);
        }

        [Fact]
        public void Keeps_the_original_error_for_the_trace_log()
        {
            // The message is what the caller reads; the exception itself still has to reach
            // the platform's log, or the stack trace is lost to everyone including support.
            var error = new InvalidOperationException("the underlying detail");

            var thrown = Run(error);

            Assert.Same(error, thrown.InnerException);
        }

        [Fact]
        public void Traces_the_error_it_wrapped()
        {
            var provider = new FakeServiceProvider();
            var plugin = new ThrowingPlugin(new InvalidOperationException("the underlying detail"));

            Assert.Throws<InvalidPluginExecutionException>(() => plugin.Execute(provider));

            Assert.Contains(provider.Traces, trace => trace.Contains("the underlying detail"));
        }
    }
}
