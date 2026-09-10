using System;
using System.Text;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The adviser-facing wording for the three emails the project owner supplied
    /// (2026-09-10): the clean pass, and the two remedial letters that differ only by the
    /// grading that earned them.
    ///
    /// Deliberately free of Dataverse types, as <see cref="OutcomeRules"/> and
    /// <see cref="ResponseRules"/> are: the words an adviser actually reads can then be
    /// asserted without a fake organisation service, and the copy has one definition rather
    /// than living inline in whichever plug-in happens to send it.
    ///
    /// Two departures from the supplied copy, both deliberate:
    ///
    /// - "The attached Case Summary details the remedial actions" points at the portal
    ///   instead. Delivery is Dataverse server-side email from a queued subject and body
    ///   (<see cref="NotificationOutbox"/>) with no attachment mechanism, and no case
    ///   summary document is generated anywhere in the solution. An email that names an
    ///   attachment which is not there sends the reader looking for it.
    /// - The obvious typos are corrected ("a AQS" -> "an AQS", "the link for to confirm" ->
    ///   "the link to confirm"). Nothing else about the wording is changed.
    /// </summary>
    public static class NotificationBodies
    {
        // What a letter says when the case names nobody. AdviserContact refuses to guess
        // between two people of the same name, so an unaddressed letter is a real state and
        // not a defect - see Remediation.AdviserContact.
        private const string UnknownAdviser = "Adviser";

        /// <summary>
        /// "Case check - Pass". Sent when a submit closes the case having raised no
        /// remediation, which is the only moment "no further action is required" is true.
        /// </summary>
        public static string Pass(string adviserName, string clientName, string caseLink)
        {
            var body = new StringBuilder();
            body.Append("Dear ").Append(Salutation(adviserName)).AppendLine(",");
            body.AppendLine();
            body.Append(Subject(clientName, "has been checked and graded a Pass."));
            body.AppendLine();
            body.AppendLine();
            body.AppendLine("No further action is required.");
            AppendLink(body, "You can view the case here: ", caseLink);
            body.AppendLine();
            body.Append("Kind regards");
            return body.ToString();
        }

        /// <summary>
        /// "Remedial needed", in the letter the grading earned, or null when the grading is
        /// one the supplied copy does not cover.
        ///
        /// Null is the useful answer for a flagged Pass (a Pass the checker nonetheless
        /// marked as needing remedial work) and for a Tax non-pass. Neither received a
        /// "pass with issues" or an "insufficient evidence / potential harm" grading, so
        /// either letter would misreport the check. The caller keeps the wording those
        /// cases already had.
        /// </summary>
        /// <param name="outcome">al_outcome.al_initialoutcome, or null when unknown.</param>
        /// <param name="dueText">The sentence naming the due date, already formatted, or null.</param>
        public static string Remediation(int? outcome, string adviserName, string clientName, string caseLink, string dueText)
        {
            var grading = Grading(outcome);
            if (grading == null)
            {
                return null;
            }

            var harm = outcome != OutcomeRules.OutcomePassWithIssues;

            var body = new StringBuilder();
            body.Append("Dear ").Append(Salutation(adviserName)).AppendLine(",");
            body.AppendLine();
            body.Append(Subject(clientName,
                "has been subject to an AQS file check and a need for remedial work has been identified due to the case receiving "
                    + grading + "."));
            body.Append(dueText ?? string.Empty);
            body.AppendLine();
            body.AppendLine();

            body.Append("The case summary in the portal details the remedial actions.");
            if (harm)
            {
                body.Append(" Please liaise with your T&C Manager to move this case forward.");
            }

            body.AppendLine();
            body.AppendLine();
            body.Append("Please follow the link to confirm that the ");
            body.Append(harm ? "required remedial action" : "remedial action");
            body.Append(" has been taken");
            body.Append(caseLink == null ? " in the portal." : ": " + caseLink);
            body.AppendLine();
            body.AppendLine();
            body.Append("Many thanks");
            return body.ToString();
        }

        /// <summary>The subject line for <see cref="Pass"/>.</summary>
        public static string PassSubject(string caseReference)
        {
            return "Case check - Pass: " + Reference(caseReference);
        }

        /// <summary>
        /// The subject line for <see cref="Remediation"/>, or null for a grading the copy
        /// does not cover - so a caller cannot send one of these subjects over the fallback
        /// body.
        /// </summary>
        public static string RemediationSubject(int? outcome, string caseReference)
        {
            if (Grading(outcome) == null)
            {
                return null;
            }

            var name = outcome == OutcomeRules.OutcomePassWithIssues
                ? "Pass with issues"
                : "insufficient evidence/ potential harm";

            return "Remedial needed - " + name + ": " + Reference(caseReference);
        }

        /// <summary>
        /// How the letter names the grading, or null when it is not one of the two the
        /// supplied copy was written for. Insufficient evidence and Potential harm share a
        /// letter, as the copy does.
        /// </summary>
        private static string Grading(int? outcome)
        {
            if (!outcome.HasValue)
            {
                return null;
            }

            switch (outcome.Value)
            {
                case OutcomeRules.OutcomePassWithIssues:
                    return "a pass with issues grading";
                case OutcomeRules.OutcomeInsufficient:
                case OutcomeRules.OutcomePotentialHarm:
                    return "an insufficient evidence/ potential harm grading";
                default:
                    return null;
            }
        }

        private static string Salutation(string adviserName)
        {
            return string.IsNullOrWhiteSpace(adviserName) ? UnknownAdviser : adviserName.Trim();
        }

        /// <summary>
        /// Opens the sentence with the client the check was about, or with "This case" when
        /// the case names no client. Capitalised only in the fallback, because the client
        /// name carries its own capitals and lower-casing "Mr and Mrs Smith" would be worse
        /// than the sentence starting with a name.
        /// </summary>
        private static string Subject(string clientName, string predicate)
        {
            return string.IsNullOrWhiteSpace(clientName)
                ? "This case " + predicate
                : clientName.Trim() + " " + predicate;
        }

        private static string Reference(string caseReference)
        {
            return string.IsNullOrWhiteSpace(caseReference) ? "a case" : caseReference.Trim();
        }

        /// <summary>
        /// Adds the link sentence, or nothing at all when the environment has no portal.
        /// <see cref="NotificationOutbox.CaseLink"/> returns null there rather than a bare
        /// path, and a sentence that offers a link and then shows none reads as a fault.
        /// </summary>
        private static void AppendLink(StringBuilder body, string lead, string caseLink)
        {
            if (string.IsNullOrWhiteSpace(caseLink))
            {
                return;
            }

            body.AppendLine();
            body.Append(lead).AppendLine(caseLink);
        }
    }
}
