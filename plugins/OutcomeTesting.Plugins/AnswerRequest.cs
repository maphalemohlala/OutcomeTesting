using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The answer payload the review page PATCHes onto al_reviewinstance.al_answerrequest.
    ///
    /// The browser cannot create an al_response directly: Power Pages refuses the
    /// @odata.bind association with 90040106, and no table permission value fixes it
    /// (2026-09-08 record, sections 20-35). So the page sends this instead and
    /// AnswerWriter does the write server-side.
    ///
    /// DataContractJsonSerializer rather than System.Text.Json: this assembly targets
    /// net462 and System.Text.Json is not available to it.
    /// </summary>
    [DataContract]
    public sealed class AnswerRequestPayload
    {
        [DataMember(Name = "questionVersionId")]
        public string QuestionVersionId { get; set; }

        [DataMember(Name = "answerText")]
        public string AnswerText { get; set; }

        [DataMember(Name = "answerChoice")]
        public int? AnswerChoice { get; set; }

        [DataMember(Name = "answerChoices")]
        public int[] AnswerChoices { get; set; }

        [DataMember(Name = "answerDate")]
        public string AnswerDate { get; set; }

        [DataMember(Name = "failReasons")]
        public string[] FailReasons { get; set; }
    }

    public static class AnswerRequest
    {
        private const string PreconditionPrefix = "PRECONDITION: ";

        public static AnswerRequestPayload Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "That answer arrived empty and was not saved.");
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(AnswerRequestPayload));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var payload = (AnswerRequestPayload)serializer.ReadObject(stream);
                    if (payload == null)
                    {
                        throw new InvalidPluginExecutionException(
                            PreconditionPrefix + "That answer could not be read and was not saved.");
                    }

                    return payload;
                }
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception)
            {
                // The exception text can carry the payload, and the payload is an answer.
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "That answer could not be read and was not saved.");
            }
        }

        public static Guid RequireQuestionVersion(AnswerRequestPayload payload)
        {
            Guid id;
            if (payload == null
                || string.IsNullOrWhiteSpace(payload.QuestionVersionId)
                || !Guid.TryParse(payload.QuestionVersionId, out id))
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "An answer must say which question it answers.");
            }

            return id;
        }
    }
}
