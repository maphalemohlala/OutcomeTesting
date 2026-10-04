using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// An edit names a person by name AND email (AD-228). A name alone is refused; an email that
    /// is not one is refused; changing only the label moves nothing.
    /// </summary>
    public class CasePeopleEditTests
    {
        private static Entity Before(string name = "Sam Adviser", string email = "sam@example.com")
        {
            return new Entity("al_outcomecase", Guid.NewGuid())
            {
                ["al_advisername"] = name,
                ["al_adviseremail"] = email,
                ["al_paraplanner"] = "Pip Planner",
                ["al_paraplanneremail"] = "pip@example.com",
            };
        }

        private static Entity Update(params object[] pairs)
        {
            var update = new Entity("al_outcomecase", Guid.NewGuid());
            for (var i = 0; i < pairs.Length; i += 2)
            {
                update[(string)pairs[i]] = pairs[i + 1];
            }

            return update;
        }

        [Fact]
        public void A_name_sent_without_its_email_is_refused()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                CasePeople.EnsureEmails(Before(), Update("al_advisername", "Adam Strumidlo")));
            Assert.Contains("adviser's email", error.Message);
        }

        [Fact]
        public void A_paraplanner_name_sent_without_its_email_is_refused()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                CasePeople.EnsureEmails(Before(), Update("al_paraplanner", "New Planner")));
            Assert.Contains("paraplanner's email", error.Message);
        }

        // A cleared name is not "a name without its email": nobody is named, so there is no
        // one to identify, and the email already on the case stays the identity.
        [Theory]
        [InlineData("al_advisername", null)]
        [InlineData("al_advisername", "   ")]
        [InlineData("al_paraplanner", null)]
        [InlineData("al_paraplanner", "")]
        public void Clearing_a_name_alone_is_allowed(string attribute, string value)
        {
            CasePeople.EnsureEmails(Before(), Update(attribute, value));
        }

        [Fact]
        public void A_name_sent_without_its_email_is_refused_on_a_case_with_no_email_either()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                CasePeople.EnsureEmails(Before(email: null), Update("al_advisername", "Sam Adviser")));
            Assert.Contains("adviser's email", error.Message);
        }

        [Fact]
        public void A_name_with_a_valid_email_is_accepted()
        {
            CasePeople.EnsureEmails(Before(), Update(
                "al_advisername", "Adam Strumidlo",
                "al_adviseremail", "adam.strumidlo@example.com"));
        }

        [Fact]
        public void A_name_in_the_email_column_is_refused()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                CasePeople.EnsureEmails(Before(), Update(
                    "al_advisername", "Adam Strumidlo",
                    "al_adviseremail", "Adam Strumidlo")));
            Assert.Contains("not an email address", error.Message);
        }

        [Fact]
        public void Clearing_the_email_while_a_name_stays_is_refused()
        {
            Assert.Throws<InvalidPluginExecutionException>(() =>
                CasePeople.EnsureEmails(Before(), Update("al_adviseremail", null)));
        }

        [Fact]
        public void Clearing_both_name_and_email_is_allowed()
        {
            CasePeople.EnsureEmails(Before(), Update("al_advisername", null, "al_adviseremail", null));
        }

        [Fact]
        public void A_legacy_case_with_no_email_can_get_one_with_its_name()
        {
            CasePeople.EnsureEmails(Before(email: null), Update(
                "al_advisername", "Sam Adviser",
                "al_adviseremail", "sam@example.com"));
        }

        [Fact]
        public void An_unrelated_edit_on_a_legacy_case_with_no_email_is_not_refused()
        {
            CasePeople.EnsureEmails(Before(email: null), Update("al_clientname", "Mr Client"));
        }

        [Theory]
        [InlineData("sam@example.com", "SAM@example.com ", false)]
        [InlineData("sam@example.com", "other@example.com", true)]
        [InlineData(null, "sam@example.com", true)]
        public void AdviserEmailChanged_ignores_case_and_spaces(string was, string now, bool changed)
        {
            Assert.Equal(changed, CasePeople.AdviserEmailChanged(
                Before(email: was), Update("al_adviseremail", now)));
        }

        [Fact]
        public void A_name_only_change_is_not_an_email_change()
        {
            Assert.False(CasePeople.AdviserEmailChanged(
                Before(), Update("al_advisername", "Sam A", "al_adviseremail", "sam@example.com")));
        }
    }
}
