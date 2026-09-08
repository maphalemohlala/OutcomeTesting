using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    public class AnswerRequestTests
    {
        [Fact]
        public void Parses_a_single_choice_answer()
        {
            var payload = AnswerRequest.Parse(
                "{\"questionVersionId\":\"3ce90bf2-64a1-f111-b8dd-e4fade069307\"," +
                "\"answerChoice\":120910305}");

            Assert.Equal(
                Guid.Parse("3ce90bf2-64a1-f111-b8dd-e4fade069307"),
                AnswerRequest.RequireQuestionVersion(payload));
            Assert.Equal(120910305, payload.AnswerChoice);
            Assert.Null(payload.AnswerText);
        }

        [Fact]
        public void Refuses_a_payload_with_no_question_version()
        {
            var payload = AnswerRequest.Parse("{\"answerChoice\":120910305}");
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AnswerRequest.RequireQuestionVersion(payload));
            Assert.StartsWith("PRECONDITION: ", error.Message);
        }

        [Fact]
        public void Refuses_text_that_is_not_json()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AnswerRequest.Parse("not json"));
            Assert.StartsWith("PRECONDITION: ", error.Message);
        }
    }
}
