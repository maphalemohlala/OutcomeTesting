using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    public class CommandHelpersTests
    {
        private static FaultException<OrganizationServiceFault> Fault(int code, string message)
        {
            return new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = code, Message = message }, message);
        }

        [Fact]
        public void Recognises_the_platforms_version_mismatch_by_code()
        {
            // 0x80060882 is ConcurrencyVersionMismatch. The matcher carried 0x80060892, one
            // digit out, and a real edit conflict on case 254398988 surfaced as UNEXPECTED
            // with the raw fault instead of the CONFLICT reload prompt (2026-09-13).
            Assert.True(CommandHelpers.IsConcurrencyFault(Fault(unchecked((int)0x80060882), "any")));
        }

        [Fact]
        public void Recognises_the_platforms_version_mismatch_by_its_own_words()
        {
            Assert.True(CommandHelpers.IsConcurrencyFault(Fault(
                -1, "The version of the existing record doesn't match the RowVersion property provided.")));
        }

        [Fact]
        public void Does_not_read_an_unrelated_fault_as_a_conflict()
        {
            Assert.False(CommandHelpers.IsConcurrencyFault(Fault(-1, "The user does not have access.")));
        }
    }
}
