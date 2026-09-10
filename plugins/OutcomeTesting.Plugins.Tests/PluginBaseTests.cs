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

        [Fact]
        public void Names_the_plug_in_and_the_fault_when_the_platform_refuses()
        {
            var fault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = "SecLib::CheckPrivilege failed" },
                new FaultReason("SecLib::CheckPrivilege failed"));

            var thrown = Run(fault);

            Assert.Contains("ThrowingPlugin", thrown.Message);
            Assert.Contains("SecLib::CheckPrivilege failed", thrown.Message);
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
