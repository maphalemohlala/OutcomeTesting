using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What the audit trail says about who was named as carrying a fail.
    ///
    /// F21, found in DEV on 2026-09-20, immediately after F15 was fixed. The line read
    /// "FQ named 5c18a790-caad-f111-aaac-e4fade069307" — the contact's GUID and nothing
    /// else. An audit event is a compliance artifact that outlives the session that wrote
    /// it, and the whole point of this command is to record a judgement about a person, so
    /// an auditor reading it back should not have to resolve an id to find out who was
    /// blamed.
    ///
    /// It was invisible until F15: the parameter never arrived, so every line said
    /// "(the case's own)" and the branch that prints an id was never reached in the product.
    ///
    /// The id is kept alongside the name. Names are not unique and contacts get renamed,
    /// so the name alone would be readable but not evidence.
    /// </summary>
    public class AccountabilityAuditLineTests
    {
        private static readonly Guid ContactId = new Guid("5c18a790-caad-f111-aaac-e4fade069307");

        private static EntityReference Contact()
        {
            return new EntityReference("contact", ContactId);
        }

        [Fact]
        public void SaysTheCasesOwnWhenNobodyIsNamed()
        {
            Assert.Equal(
                "(the case's own)",
                SetFailAccountabilityPlugin.Describe(null, null));
        }

        [Fact]
        public void NamesThePersonWhenTheNameIsKnown()
        {
            var line = SetFailAccountabilityPlugin.Describe(Contact(), "Zoe Ramwell");

            Assert.Contains("Zoe Ramwell", line);
        }

        [Fact]
        public void KeepsTheIdBesideTheNameSoTheLineIsStillEvidence()
        {
            var line = SetFailAccountabilityPlugin.Describe(Contact(), "Zoe Ramwell");

            Assert.Equal("Zoe Ramwell (" + ContactId.ToString("D") + ")", line);
        }

        [Fact]
        public void FallsBackToTheIdWhenTheNameCannotBeRead()
        {
            // A contact the caller may not read, or one deleted between the write and here.
            // The id alone is worse to read but it is still true, which a blank would not be.
            Assert.Equal(ContactId.ToString("D"), SetFailAccountabilityPlugin.Describe(Contact(), null));
            Assert.Equal(ContactId.ToString("D"), SetFailAccountabilityPlugin.Describe(Contact(), "   "));
        }
    }
}
