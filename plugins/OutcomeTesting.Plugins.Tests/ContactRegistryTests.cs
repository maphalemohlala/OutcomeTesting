using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// contact.fullname is calculated and cannot be written, so every command that used to
    /// set al_name has to decide how a display name splits into firstname and lastname.
    /// Getting it wrong is silent: the row saves and the name comes back different.
    /// </summary>
    public class ContactRegistryTests
    {
        [Theory]
        [InlineData("Sims Rad", "Sims", "Rad")]
        [InlineData("Mary Jane Watson", "Mary Jane", "Watson")]
        [InlineData("  Dev   Account  ", "Dev", "Account")]
        public void SplitsAForenameFromASurname(string full, string first, string last)
        {
            Assert.Equal(first, ContactRegistry.FirstNameOf(full));
            Assert.Equal(last, ContactRegistry.LastNameOf(full));
        }

        [Fact]
        public void TreatsASingleWordAsTheSurname()
        {
            // lastname is the required half, so a mononym has to land there or the create fails.
            Assert.Equal(string.Empty, ContactRegistry.FirstNameOf("Cher"));
            Assert.Equal("Cher", ContactRegistry.LastNameOf("Cher"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void YieldsEmptyPartsForAnEmptyName(string full)
        {
            Assert.Equal(string.Empty, ContactRegistry.FirstNameOf(full));
            Assert.Equal(string.Empty, ContactRegistry.LastNameOf(full));
        }

        [Theory]
        [InlineData("Sims Rad")]
        [InlineData("Mary Jane Watson")]
        [InlineData("Cher")]
        public void RoundTripsTheNameThePlatformWillCompose(string full)
        {
            Assert.Equal(full, ContactRegistry.ComposeName(full));
        }

        [Fact]
        public void WritesTheNameAsThePartsFullnameIsBuiltFrom()
        {
            var contact = new Entity(ContactRegistry.Entity);

            ContactRegistry.SetName(contact, "Sims Rad");

            Assert.Equal("Sims", contact[ContactRegistry.FirstNameAttr]);
            Assert.Equal("Rad", contact[ContactRegistry.LastNameAttr]);
            Assert.False(contact.Contains(ContactRegistry.FullNameAttr));
        }

        [Fact]
        public void ReadsAnAbsentStatecodeAsActive()
        {
            // Only an explicit deactivation withdraws access (OD-010), so a row that did not
            // return statecode must not be read as a leaver.
            Assert.True(ContactRegistry.IsActive(new Entity(ContactRegistry.Entity)));
        }

        [Fact]
        public void ReadsInactiveAsInactive()
        {
            var contact = new Entity(ContactRegistry.Entity)
            {
                [ContactRegistry.StateCodeAttr] = new OptionSetValue(ContactRegistry.StateInactive),
            };

            Assert.False(ContactRegistry.IsActive(contact));
        }

        [Fact]
        public void PrefersFullnameButFallsBackToThePartsThenTheEmail()
        {
            var full = new Entity(ContactRegistry.Entity) { [ContactRegistry.FullNameAttr] = "Sims Rad" };
            Assert.Equal("Sims Rad", ContactRegistry.NameOf(full));

            var parts = new Entity(ContactRegistry.Entity)
            {
                [ContactRegistry.FirstNameAttr] = "Sims",
                [ContactRegistry.LastNameAttr] = "Rad",
            };
            Assert.Equal("Sims Rad", ContactRegistry.NameOf(parts));

            var emailOnly = new Entity(ContactRegistry.Entity)
            {
                [ContactRegistry.EmailAttr] = "sims@example.com",
            };
            Assert.Equal("sims@example.com", ContactRegistry.NameOf(emailOnly));
        }
    }
}
