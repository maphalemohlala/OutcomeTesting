using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Closing an import exception (BR-002, FR-002/FR-003). The rules here are the ones
    /// that stop a closure destroying the record of what happened to the row: only an open
    /// exception can be closed, and only into a status the deployed option set carries.
    /// </summary>
    public class ResolveImportExceptionPluginTests
    {
        private static Entity Exception(int? status)
        {
            var record = new Entity("al_importexception");
            if (status.HasValue)
            {
                record["al_exceptionstatus"] = new OptionSetValue(status.Value);
            }

            return record;
        }

        [Fact]
        public void Allows_an_open_exception_to_be_closed()
        {
            ResolveImportExceptionPlugin.EnsureOpen(Exception(120910740));
        }

        [Fact]
        public void Refuses_an_exception_that_is_already_resolved()
        {
            // A second closure would overwrite the first one's note and timestamp, and
            // AD-037 forbids deleting the row to put it back.
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => ResolveImportExceptionPlugin.EnsureOpen(Exception(120910741)));

            Assert.Contains("PRECONDITION:", error.Message);
            Assert.Contains("already resolved", error.Message);
        }

        [Fact]
        public void Refuses_an_exception_that_was_ignored()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => ResolveImportExceptionPlugin.EnsureOpen(Exception(120910742)));

            Assert.Contains("already ignored", error.Message);
        }

        [Fact]
        public void Refuses_an_exception_with_no_status_recorded()
        {
            Assert.Throws<InvalidPluginExecutionException>(
                () => ResolveImportExceptionPlugin.EnsureOpen(Exception(null)));
        }

        [Theory]
        [InlineData("Resolved", 120910741)]
        [InlineData("resolved", 120910741)]
        [InlineData(" Ignored ", 120910742)]
        public void Accepts_the_two_closures_the_option_set_carries(string label, int expected)
        {
            Assert.Equal(expected, ResolveImportExceptionPlugin.ParseResolution(label));
        }

        [Theory]
        [InlineData("Open")]
        [InlineData("Returned")]
        [InlineData("")]
        [InlineData(null)]
        public void Refuses_a_status_that_is_not_a_closure(string label)
        {
            // "Returned" reads like the BR-002 wording but is not a value the option set
            // holds. Coercing it to something would write a status nothing else recognises.
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => ResolveImportExceptionPlugin.ParseResolution(label));

            Assert.Contains("Resolved or Ignored", error.Message);
        }

        [Fact]
        public void Names_each_status_for_the_audit_trail()
        {
            Assert.Equal("Open", ResolveImportExceptionPlugin.ResolutionLabel(120910740));
            Assert.Equal("Resolved", ResolveImportExceptionPlugin.ResolutionLabel(120910741));
            Assert.Equal("Ignored", ResolveImportExceptionPlugin.ResolutionLabel(120910742));
            Assert.Equal("Unknown", ResolveImportExceptionPlugin.ResolutionLabel(999));
        }
    }
}
