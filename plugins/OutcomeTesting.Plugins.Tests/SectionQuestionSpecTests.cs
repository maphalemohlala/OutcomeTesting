using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-122: a section may be created with its questions in one call. The array is
    /// validated in full before anything is written, so a bad element cannot leave a
    /// section half-populated.
    /// </summary>
    public class SectionQuestionSpecTests
    {
        [Fact]
        public void An_absent_or_empty_array_yields_no_questions()
        {
            Assert.Empty(SectionQuestionSpec.ParseMany(null));
            Assert.Empty(SectionQuestionSpec.ParseMany("   "));
            Assert.Empty(SectionQuestionSpec.ParseMany("[]"));
        }

        [Fact]
        public void A_well_formed_element_is_read_in_full()
        {
            var specs = SectionQuestionSpec.ParseMany(
                "[{\"code\":\"Q-CD2-01\",\"name\":\"Fair value\",\"wording\":\"Is fair value evidenced?\",\"responseType\":120910009,\"mandatory\":true,\"displayOrder\":2}]");

            var only = Assert.Single(specs);
            Assert.Equal("Q-CD2-01", only.Code);
            Assert.Equal("Fair value", only.Name);
            Assert.Equal("Is fair value evidenced?", only.Wording);
            Assert.Equal(120910009, only.ResponseType);
            Assert.True(only.Mandatory);
            Assert.Equal(2, only.DisplayOrder);
        }

        [Fact]
        public void Order_defaults_to_the_position_in_the_array()
        {
            var specs = SectionQuestionSpec.ParseMany(
                "[{\"code\":\"Q-A\",\"name\":\"A\",\"wording\":\"A?\",\"responseType\":120910000}," +
                "{\"code\":\"Q-B\",\"name\":\"B\",\"wording\":\"B?\",\"responseType\":120910000}]");

            Assert.Equal(new[] { 1, 2 }, specs.Select(s => s.DisplayOrder).ToArray());
        }

        [Fact]
        public void Mandatory_defaults_to_true()
        {
            // AD-019: every displayed answerable question is mandatory unless its section
            // is optional. Defaulting to false here would quietly invert that.
            var specs = SectionQuestionSpec.ParseMany(
                "[{\"code\":\"Q-A\",\"name\":\"A\",\"wording\":\"A?\",\"responseType\":120910000}]");

            Assert.True(specs[0].Mandatory);
        }

        [Theory]
        [InlineData("[{\"name\":\"A\",\"wording\":\"A?\",\"responseType\":120910000}]", "code")]
        [InlineData("[{\"code\":\"Q-A\",\"wording\":\"A?\",\"responseType\":120910000}]", "name")]
        [InlineData("[{\"code\":\"Q-A\",\"name\":\"A\",\"responseType\":120910000}]", "wording")]
        [InlineData("[{\"code\":\"Q-A\",\"name\":\"A\",\"wording\":\"A?\"}]", "responseType")]
        public void A_missing_required_field_is_refused_by_name(string json, string field)
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SectionQuestionSpec.ParseMany(json));

            Assert.Contains(field, error.Message);
        }

        [Fact]
        public void A_duplicate_code_within_the_array_is_refused()
        {
            // The alternate key would catch this at the second Create, after the section
            // and the first question were already written.
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SectionQuestionSpec.ParseMany(
                    "[{\"code\":\"Q-A\",\"name\":\"A\",\"wording\":\"A?\",\"responseType\":120910000}," +
                    "{\"code\":\"Q-A\",\"name\":\"B\",\"wording\":\"B?\",\"responseType\":120910000}]"));

            Assert.Contains("Q-A", error.Message);
        }

        [Fact]
        public void Malformed_json_is_refused_rather_than_ignored()
        {
            Assert.Throws<InvalidPluginExecutionException>(
                () => SectionQuestionSpec.ParseMany("{not json"));
        }


        [Fact]
        public void A_payload_that_is_not_an_array_is_refused()
        {
            // DataContractJsonSerializer reads a JSON object into an array type as an
            // EMPTY array - no exception, not null - so sending {...} instead of [{...}]
            // would otherwise create the section with no questions and report success.
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SectionQuestionSpec.ParseMany("{\"code\":\"Q-A\",\"name\":\"A\"}"));

            Assert.Contains("must be a JSON array", error.Message);
        }

        [Fact]
        public void A_malformed_payload_is_not_echoed_back_in_the_refusal()
        {
            // The house rule from AnswerRequest: the exception text can carry the payload,
            // and a payload is user content. The refusal says what is wrong, not what was
            // sent.
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SectionQuestionSpec.ParseMany("{\"secret\":\"do-not-echo-me\""));

            Assert.DoesNotContain("do-not-echo-me", error.Message);
        }
    }
}
