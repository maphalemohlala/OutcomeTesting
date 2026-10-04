using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The email a case's adviser is known by (AD-228). The stored al_adviseremail, and only
    /// that - never derived from al_advisername.
    ///
    /// <para>
    /// PROD case 256497798 named "Adam Strumidlo" as its adviser; his contact was onboarded as
    /// "Adam Strumdlio", a typo, so a name match found nobody and his remediation was raised
    /// unassigned. His email was right everywhere, which is why name resolution was removed
    /// rather than fixed (owner, 2026-10-02: "two people can have the same name").
    /// </para>
    /// </summary>
    public class CaseAdviserTests
    {
        [Fact]
        public void Uses_the_stored_adviser_email_trimmed()
        {
            Assert.Equal("stored@example.com", CaseAdviser.EmailFor("  stored@example.com "));
        }

        [Fact]
        public void Never_falls_back_to_a_contact_the_adviser_name_resolves_to()
        {
            // The removed behaviour, inverted. A case with no stored email has no adviser
            // email at all, even though the name on the case matches exactly one contact.
            var service = new FakeOrganizationService();
            service.Seed("contact", System.Guid.NewGuid(),
                "fullname", "Adam Strumidlo", "emailaddress1", "adam@example.com",
                "statecode", new OptionSetValue(0));

            Assert.Null(CaseAdviser.EmailFor(null));
            Assert.Null(CaseAdviser.EmailFor("   "));
        }
    }
}
