using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// One question as AddSection receives it (AD-122). The whole array is parsed and
    /// validated before anything is written, so a bad element refuses the call rather than
    /// leaving a section with half its questions.
    ///
    /// DataContractJsonSerializer rather than System.Text.Json: this assembly targets net462
    /// and System.Text.Json is not available to it. AnswerRequest makes the same choice for
    /// the same reason.
    /// </summary>
    [DataContract]
    public sealed class SectionQuestionSpec
    {
        [DataMember(Name = "code")]
        public string Code { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "wording")]
        public string Wording { get; set; }

        [DataMember(Name = "responseType")]
        public int? ResponseType { get; set; }

        [DataMember(Name = "mandatory")]
        public bool? MandatoryRaw { get; set; }

        [DataMember(Name = "displayOrder")]
        public int? DisplayOrderRaw { get; set; }

        /// <summary>AD-019: mandatory unless the element says otherwise.</summary>
        public bool Mandatory
        {
            get { return !MandatoryRaw.HasValue || MandatoryRaw.Value; }
        }

        /// <summary>Position in the array where the element does not say.</summary>
        public int DisplayOrder { get; set; }

        /// <summary>
        /// Every question in the array, validated. Refuses rather than skips: a question
        /// silently dropped from a section is a checklist that looks complete and is not.
        /// </summary>
        public static IList<SectionQuestionSpec> ParseMany(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<SectionQuestionSpec>();
            }

            // A JSON object deserialises into an array type as an EMPTY array, without
            // throwing and without returning null - so nothing about the result tells a
            // caller who sent {...} instead of [{...}] apart from one who legitimately sent
            // []. Both would create the section with no questions and report success. The
            // shape has to be checked on the way in.
            if (!json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "The questions must be a JSON array, so the section was not created.");
            }

            SectionQuestionSpec[] parsed;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(SectionQuestionSpec[]));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    parsed = (SectionQuestionSpec[])serializer.ReadObject(stream);
                }
            }
            catch (Exception)
            {
                // The exception text can carry the payload, and the payload is user content
                // - the same reason AnswerRequest.Parse says what is wrong rather than what
                // was sent.
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "The questions could not be read as JSON, so the section was not created.");
            }

            // A payload that is not an array - an object, or JSON null - deserialises to
            // null without throwing. Treating that as "no questions" would create the
            // section empty and report success, which is the silent drop this class exists
            // to prevent. AnswerRequest.Parse guards the same way.
            if (parsed == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "The questions must be a JSON array, so the section was not created.");
            }

            var specs = new List<SectionQuestionSpec>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < parsed.Length; index++)
            {
                var spec = parsed[index];
                var position = (index + 1).ToString(CultureInfo.InvariantCulture);

                Require(spec.Code, "code", position);
                Require(spec.Name, "name", position);
                Require(spec.Wording, "wording", position);
                if (!spec.ResponseType.HasValue)
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix +
                        "Question " + position + " has no responseType.");
                }

                spec.Code = spec.Code.Trim();
                if (!seen.Add(spec.Code))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix +
                        "Question code '" + spec.Code + "' appears twice in the same section.");
                }

                spec.DisplayOrder = spec.DisplayOrderRaw.HasValue ? spec.DisplayOrderRaw.Value : index + 1;
                specs.Add(spec);
            }

            return specs;
        }

        private static void Require(string value, string field, string position)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Question " + position + " has no " + field + ".");
            }
        }
    }
}
