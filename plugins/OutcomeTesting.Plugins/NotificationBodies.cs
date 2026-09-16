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
            Paragraph(body, "Dear " + Salutation(adviserName) + ",");
            Paragraph(body, Subject(clientName, "has been checked and graded a Pass."));
            Paragraph(body, "No further action is required.");
            AppendButton(body, "View the case", caseLink);
            Paragraph(body, "Kind regards");
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
            var hasLink = !string.IsNullOrWhiteSpace(caseLink);

            var body = new StringBuilder();
            Paragraph(body, "Dear " + Salutation(adviserName) + ",");
            Paragraph(body, Subject(clientName,
                "has been subject to an AQS file check and a need for remedial work has been identified due to the case receiving "
                    + grading + ".") + (dueText ?? string.Empty));

            Paragraph(body, "The case summary in the portal details the remedial actions."
                + (harm ? " Please liaise with your T&C Manager to move this case forward." : string.Empty));

            Paragraph(body, "Please follow the link to confirm that the "
                + (harm ? "required remedial action" : "remedial action")
                + " has been taken" + (hasLink ? "." : " in the portal."));
            AppendButton(body, "Confirm remedial action", caseLink);
            Paragraph(body, "Many thanks");
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
        /// One paragraph of the letter.
        ///
        /// Every word that reaches a reader goes through here, so the escaping has one home
        /// rather than sitting at each interpolation where a later edit forgets it. That
        /// matters for the copy as much as for the data: "T&amp;C Manager" is an
        /// unterminated entity if it is written raw, and the adviser and client names are
        /// copied off the case.
        /// </summary>
        private static void Paragraph(StringBuilder body, string text)
        {
            body.Append("<p>").Append(Html(text)).Append("</p>");
        }

        /// <summary>
        /// The call to action, or nothing at all when the environment has no portal.
        /// <see cref="NotificationOutbox.CaseLink"/> returns null there rather than a bare
        /// path, and a button that opens nothing reads as a fault.
        ///
        /// The style is inline because email clients drop a style element, and the href is
        /// escaped like any other value: a portal link joins its parameters with an
        /// ampersand, which starts an entity inside an attribute. The address itself is
        /// built from the Power Pages site row rather than from anything a person typed, so
        /// the escaping is about correctness rather than about the scheme.
        /// </summary>
        private static void AppendButton(StringBuilder body, string label, string caseLink)
        {
            if (string.IsNullOrWhiteSpace(caseLink))
            {
                return;
            }

            body.Append("<p><a href=\"").Append(Html(caseLink)).Append("\" style=\"")
                .Append("background-color:#0b5394;color:#ffffff;display:inline-block;")
                .Append("padding:12px 22px;border-radius:4px;text-decoration:none;")
                .Append("font-family:Segoe UI,Arial,sans-serif;font-size:14px;font-weight:600;")
                .Append("\">").Append(Html(label)).Append("</a></p>");
        }

        /// <summary>
        /// Text as HTML. The ampersand is replaced first: doing it last would re-escape the
        /// entities the other replacements had just written.
        /// </summary>
        private static string Html(string text)
        {
            return (text ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
