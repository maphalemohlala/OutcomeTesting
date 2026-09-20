using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The subject and body of every notification, editable as data (Change 1, AD-163).
    ///
    /// <para>
    /// Until 2026-09-20 every word of every email was compiled into this assembly, so
    /// changing a sentence meant a build and a deployment. The wording now lives in
    /// <c>al_notificationtemplate</c>, keyed by a stable code.
    /// </para>
    /// <para>
    /// <b>The compiled copy is still here, and is still the fallback.</b> A template row that
    /// is missing, inactive or empty falls back to the compiled catalogue below, which holds today's
    /// wording verbatim. That is what makes this change safe to deploy before a single row
    /// exists: with an empty table every letter is byte-for-byte what it was, and the seed
    /// is generated from the same source rather than transcribed beside it.
    /// </para>
    /// <para>
    /// <b>Escaping.</b> The stored template is markup written by an administrator and is used
    /// as-is. Token VALUES come from case data and are escaped on the way in, exactly as
    /// <c>NotificationBodies.Paragraph</c> escaped them before. Getting this backwards either
    /// breaks the markup or lets a client name carrying an angle bracket rewrite the letter,
    /// so the direction is asserted by its own test.
    /// </para>
    /// <para>
    /// <b>Not every body is HTML.</b> The Pass and Remediation letters are; allocation,
    /// review-submitted, sign-off and recheck are plain text, and always have been. Each code
    /// records which it is, because escaping a plain-text body would put
    /// <c>&amp;amp;</c> in front of a reader.
    /// </para>
    /// </summary>
    public static class NotificationTemplates
    {
        /// <summary>The table holding the editable wording.</summary>
        public const string TemplateEntity = "al_notificationtemplate";

        /// <summary>The stable code identifying a letter; the table's alternate key.</summary>
        public const string CodeAttr = "al_templatecode";

        /// <summary>The editable subject line.</summary>
        public const string SubjectAttr = "al_subject";

        /// <summary>The editable body.</summary>
        public const string BodyAttr = "al_body";

        // ---------------------------------------------------------------- codes

        /// <summary>A case was allocated to a checker, and the environment has a portal.</summary>
        public const string Allocation = "ALLOCATION";

        /// <summary>A case was allocated, and there is no portal link to offer.</summary>
        public const string AllocationNoLink = "ALLOCATION-NO-LINK";

        /// <summary>A checker submitted a review.</summary>
        public const string ReviewSubmitted = "REVIEW-SUBMITTED";

        /// <summary>Remediation raised on a Pass with issues grading.</summary>
        public const string RemediationPassWithIssues = "REMEDIATION-PASS-WITH-ISSUES";

        /// <summary>Remediation raised on an insufficient evidence / potential harm grading.</summary>
        public const string RemediationHarm = "REMEDIATION-HARM";

        /// <summary>Remediation raised on a grading the supplied copy was never written for.</summary>
        public const string RemediationOther = "REMEDIATION-OTHER";

        /// <summary>A submit closed the case having raised nothing.</summary>
        public const string CasePassed = "CASE-PASSED";

        /// <summary>A sign-off was approved and the case moved on to recheck.</summary>
        public const string SignoffApprovedRecheck = "SIGNOFF-APPROVED-RECHECK";

        /// <summary>A sign-off was approved with a grade, closing the case.</summary>
        public const string SignoffApprovedClosed = "SIGNOFF-APPROVED-CLOSED";

        /// <summary>A sign-off was rejected and the work went back to the adviser.</summary>
        public const string SignoffRejected = "SIGNOFF-REJECTED";

        /// <summary>The case is at Awaiting Recheck, waiting for its final outcome.</summary>
        public const string RecheckDue = "RECHECK-DUE";

        /// <summary>Every action is complete, so the T&amp;C Manager's sign-off is owed.</summary>
        public const string SignoffDue = "SIGNOFF-DUE";

        // ---------------------------------------------------------------- tokens

        /// <summary>The case reference, or "a case" when it carries none.</summary>
        public const string TokenReference = "reference";

        /// <summary>The adviser's name, or "Adviser".</summary>
        public const string TokenAdviser = "adviser";

        /// <summary>The client's name, or "This case" — it opens a sentence either way.</summary>
        public const string TokenClient = "client";

        /// <summary>The portal address of the case.</summary>
        public const string TokenCaseLink = "caseLink";

        /// <summary>
        /// The styled call-to-action anchor, or nothing when there is no portal.
        ///
        /// <b>Raw markup, not escaped</b>, because it is built by this assembly and never by
        /// a person. It is a token rather than fixed copy so an administrator can move it or
        /// drop it, but not restyle it into something an email client will not render.
        /// </summary>
        public const string TokenCaseButton = "caseButton";

        /// <summary>" It is due by 3 March 2026.", or nothing.</summary>
        public const string TokenDueText = "dueText";

        /// <summary>How the letter names the grading.</summary>
        public const string TokenGrading = "grading";

        /// <summary>The final outcome label recorded at sign-off.</summary>
        public const string TokenFinalOutcome = "finalOutcome";

        /// <summary>" Notes: …" as the signatory wrote them, or nothing.</summary>
        public const string TokenNotes = "notes";

        /// <summary>Tokens whose value is markup and must NOT be escaped.</summary>
        private static readonly HashSet<string> RawTokens =
            new HashSet<string>(StringComparer.Ordinal) { TokenCaseButton };

        // ---------------------------------------------------------------- catalogue

        /// <summary>One letter: what it says by default, and what may appear in it.</summary>
        public sealed class TemplateDefinition
        {
            /// <summary>The stable code.</summary>
            public string Code { get; set; }

            /// <summary>What an administrator sees in a list.</summary>
            public string Name { get; set; }

            /// <summary>The subject line, with tokens.</summary>
            public string Subject { get; set; }

            /// <summary>The body, with tokens.</summary>
            public string Body { get; set; }

            /// <summary>True when the body is markup rather than plain text.</summary>
            public bool IsHtml { get; set; }

            /// <summary>The tokens this letter may use; anything else is refused.</summary>
            public string[] Tokens { get; set; }

            /// <summary>
            /// The words on the call-to-action button, where the letter has one.
            ///
            /// Deliberately NOT editable as data in this iteration. The button is markup this
            /// assembly builds so that an email client will render it, and the label sits
            /// inside that markup. Making the label editable means either exposing the anchor
            /// to an editor or adding a second token for one word; neither was worth it
            /// before anyone has asked to change it, and this comment is the record that it
            /// was a choice rather than an oversight.
            /// </summary>
            public string ButtonLabel { get; set; }
        }

        private static readonly Dictionary<string, TemplateDefinition> Catalogue = BuildCatalogue();

        /// <summary>Every letter this solution sends, in a stable order.</summary>
        public static IEnumerable<TemplateDefinition> All
        {
            get { return Catalogue.Values; }
        }

        /// <summary>The definition for a code, or null when the code is not one of ours.</summary>
        public static TemplateDefinition Definition(string code)
        {
            TemplateDefinition found;
            return code != null && Catalogue.TryGetValue(code, out found) ? found : null;
        }

        // ---------------------------------------------------------------- rendering

        /// <summary>A rendered letter.</summary>
        public sealed class Letter
        {
            /// <summary>The subject line.</summary>
            public string Subject { get; set; }

            /// <summary>The body.</summary>
            public string Body { get; set; }

            /// <summary>True when the stored template was used rather than the compiled copy.</summary>
            public bool FromTemplate { get; set; }
        }

        /// <summary>
        /// The letter for this code, from the stored template where there is a usable one and
        /// from the compiled copy otherwise.
        ///
        /// <para>
        /// Never throws. This runs inside the transaction that caused the notification, so an
        /// unreadable template must not roll back a submit or a completion — it falls back and
        /// the letter still goes. A broken row costs the wording, never the send.
        /// </para>
        /// </summary>
        public static Letter Render(
            IOrganizationService service, string code, IDictionary<string, string> tokens)
        {
            var definition = Definition(code);
            if (definition == null)
            {
                throw new ArgumentException("Unknown notification template code: " + code, "code");
            }

            string subject = null;
            string body = null;
            var fromTemplate = false;

            try
            {
                var stored = Stored(service, code);
                if (stored != null)
                {
                    var storedSubject = stored.GetAttributeValue<string>(SubjectAttr);
                    var storedBody = stored.GetAttributeValue<string>(BodyAttr);

                    // Both or neither. A row carrying a subject and an empty body would
                    // otherwise send a letter with nothing in it, which reads as a fault and
                    // is worse than the wording being out of date.
                    if (!string.IsNullOrWhiteSpace(storedSubject) && !string.IsNullOrWhiteSpace(storedBody))
                    {
                        subject = storedSubject;
                        body = storedBody;
                        fromTemplate = true;
                    }
                }
            }
            catch (Exception)
            {
                // Swallowed deliberately; see the summary. The compiled copy below is the
                // answer to every failure here.
                subject = null;
                body = null;
                fromTemplate = false;
            }

            if (subject == null || body == null)
            {
                subject = definition.Subject;
                body = definition.Body;
                fromTemplate = false;
            }

            return new Letter
            {
                // The subject is plain text in every mail client, so its tokens are never
                // escaped whatever the body is.
                Subject = Substitute(subject, tokens, escape: false),
                Body = Substitute(body, tokens, escape: definition.IsHtml),
                FromTemplate = fromTemplate,
            };
        }

        /// <summary>The active stored template for a code, or null.</summary>
        private static Entity Stored(IOrganizationService service, string code)
        {
            var query = new QueryExpression(TemplateEntity)
            {
                ColumnSet = new ColumnSet(SubjectAttr, BodyAttr),
                // The code is the table's alternate key, so a second row of it cannot exist.
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(CodeAttr, ConditionOperator.Equal, code);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count == 0 ? null : found[0];
        }

        /// <summary>
        /// Replaces every <c>{{token}}</c> with its value.
        ///
        /// <paramref name="escape"/> is true for an HTML body: the template itself is markup
        /// and is left alone, while the values — a client's name, an adviser's name, a link —
        /// are escaped as they always were. A raw token is exempt, because its value is
        /// markup this assembly built.
        ///
        /// A token with no value becomes empty rather than being left as <c>{{token}}</c>.
        /// Several of these letters are written around a fragment that is legitimately absent
        /// — a due date, a note — and printing the placeholder would be worse than a gap.
        /// </summary>
        public static string Substitute(
            string template, IDictionary<string, string> tokens, bool escape)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            var result = new StringBuilder(template);

            foreach (var name in AllTokenNames)
            {
                string value = null;
                if (tokens != null)
                {
                    tokens.TryGetValue(name, out value);
                }

                value = value ?? string.Empty;

                if (escape && !RawTokens.Contains(name))
                {
                    value = Html(value);
                }

                result.Replace("{{" + name + "}}", value);
            }

            return result.ToString();
        }

        /// <summary>Every token name this solution knows, raw ones included.</summary>
        public static readonly string[] AllTokenNames =
        {
            TokenReference, TokenAdviser, TokenClient, TokenCaseLink, TokenCaseButton,
            TokenDueText, TokenGrading, TokenFinalOutcome, TokenNotes,
        };

        /// <summary>
        /// The tokens a template uses that its code does not allow, or an empty array.
        ///
        /// Used by the guard plug-in so a template naming a token that will never be supplied
        /// is refused at the point of saving, rather than quietly rendering a gap in a letter
        /// somebody receives.
        /// </summary>
        public static string[] UnknownTokens(string code, string subject, string body)
        {
            var definition = Definition(code);
            if (definition == null)
            {
                return new[] { "(unknown template code)" };
            }

            return UnknownTokensAgainst(definition.Tokens, subject, body);
        }

        /// <summary>
        /// The tokens in this wording that are not in the allowed set.
        ///
        /// Split out from <see cref="UnknownTokens(string,string,string)"/> for the letters an
        /// administrator writes themselves (AD-168): those have no catalogue entry, so the
        /// allowed set comes from what a case can always answer rather than from a definition.
        /// One scanner, so the two cannot come to disagree about what a token even is.
        /// </summary>
        public static string[] UnknownTokensAgainst(
            string[] allowedTokens, string subject, string body)
        {
            var allowed = new HashSet<string>(
                allowedTokens ?? new string[0], StringComparer.Ordinal);
            var offenders = new List<string>();
            var text = (subject ?? string.Empty) + " " + (body ?? string.Empty);

            var start = 0;
            while (true)
            {
                var open = text.IndexOf("{{", start, StringComparison.Ordinal);
                if (open < 0)
                {
                    break;
                }

                var close = text.IndexOf("}}", open + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    break;
                }

                var name = text.Substring(open + 2, close - open - 2).Trim();
                if (!allowed.Contains(name) && !offenders.Contains(name))
                {
                    offenders.Add(name);
                }

                start = close + 2;
            }

            return offenders.ToArray();
        }

        /// <summary>
        /// The call-to-action anchor, or nothing at all when the environment has no portal.
        ///
        /// <see cref="NotificationOutbox.CaseLink"/> returns null there rather than a bare
        /// path, and a button that opens nothing reads as a fault.
        ///
        /// The style is inline because email clients drop a style element, and the href is
        /// escaped like any other value: a portal link joins its parameters with an
        /// ampersand, which starts an entity inside an attribute.
        /// </summary>
        public static string CaseButton(string caseLink, string label)
        {
            if (string.IsNullOrWhiteSpace(caseLink))
            {
                return string.Empty;
            }

            return "<p><a href=\"" + Html(caseLink) + "\" style=\""
                + "background-color:#0b5394;color:#ffffff;display:inline-block;"
                + "padding:12px 22px;border-radius:4px;text-decoration:none;"
                + "font-family:Segoe UI,Arial,sans-serif;font-size:14px;font-weight:600;"
                + "\">" + Html(label) + "</a></p>";
        }

        /// <summary>The adviser's name for the salutation, or "Adviser".</summary>
        public static string Salutation(string adviserName)
        {
            return string.IsNullOrWhiteSpace(adviserName) ? "Adviser" : adviserName.Trim();
        }

        /// <summary>
        /// How the letter opens its sentence: with the client, or with "This case".
        ///
        /// Capitalised only in the fallback, because a client name carries its own capitals
        /// and lower-casing "Mr and Mrs Smith" would be worse than a sentence starting with
        /// a name.
        /// </summary>
        public static string ClientOpener(string clientName)
        {
            return string.IsNullOrWhiteSpace(clientName) ? "This case" : clientName.Trim();
        }

        /// <summary>The case reference, or "a case".</summary>
        public static string ReferenceOr(string caseReference)
        {
            return string.IsNullOrWhiteSpace(caseReference) ? "a case" : caseReference.Trim();
        }

        /// <summary>Text as HTML, as <c>NotificationBodies</c> has always escaped it.</summary>
        private static string Html(string text)
        {
            return (text ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        // ---------------------------------------------------------------- the copy

        private static Dictionary<string, TemplateDefinition> BuildCatalogue()
        {
            var all = new List<TemplateDefinition>
            {
                Plain(Allocation, "Case allocated",
                    "Case {{reference}} has been allocated to you",
                    "Case {{reference}} is now assigned to you for checking. Open it to start the review: {{caseLink}}",
                    TokenReference, TokenCaseLink),

                Plain(AllocationNoLink, "Case allocated (no portal link)",
                    "Case {{reference}} has been allocated to you",
                    "Case {{reference}} is now assigned to you for checking. Open it in the portal to start the review.",
                    TokenReference),

                Plain(ReviewSubmitted, "Review submitted",
                    "Review submitted on case {{reference}}",
                    "The review on case {{reference}} has been submitted and is locked to further edits (FR-017).",
                    TokenReference),

                Html(RemediationPassWithIssues, "Remedial needed - pass with issues",
                    "Remedial needed - Pass with issues: {{reference}}",
                    RemediationBody(harm: false),
                    "Confirm remedial action",
                    TokenReference, TokenAdviser, TokenClient, TokenGrading, TokenDueText, TokenCaseButton),

                Html(RemediationHarm, "Remedial needed - insufficient evidence or potential harm",
                    "Remedial needed - insufficient evidence/ potential harm: {{reference}}",
                    RemediationBody(harm: true),
                    "Confirm remedial action",
                    TokenReference, TokenAdviser, TokenClient, TokenGrading, TokenDueText, TokenCaseButton),

                Plain(RemediationOther, "Remediation raised (other grading)",
                    "Remediation required on case {{reference}}",
                    "Remediation has been raised against case {{reference}} and assigned to you (BR-006).{{dueText}}"
                        + " Record your response against each item in the portal.",
                    TokenReference, TokenDueText),

                Html(CasePassed, "Case check - Pass",
                    "Case check - Pass: {{reference}}",
                    "<p>Dear {{adviser}},</p>"
                        + "<p>{{client}} has been checked and graded a Pass.</p>"
                        + "<p>No further action is required.</p>"
                        + "{{caseButton}}"
                        + "<p>Kind regards</p>",
                    "View the case",
                    TokenReference, TokenAdviser, TokenClient, TokenCaseButton),

                Plain(SignoffApprovedRecheck, "Remediation approved - moving to recheck",
                    "Remediation approved on case {{reference}}",
                    "Your remediation on case {{reference}} has been approved and the case has moved on to recheck.{{notes}}",
                    TokenReference, TokenNotes),

                Plain(SignoffApprovedClosed, "Remediation approved - case closed",
                    "Remediation approved on case {{reference}}",
                    "Your remediation on case {{reference}} has been approved, and the case is now closed with a "
                        + "final outcome of {{finalOutcome}}.{{notes}}",
                    TokenReference, TokenFinalOutcome, TokenNotes),

                Plain(SignoffRejected, "Remediation sent back",
                    "Remediation sent back on case {{reference}}",
                    "Your remediation on case {{reference}} has been sent back for further work. "
                        + "The ten-working-day clock has restarted from today (OD-018).{{notes}}",
                    TokenReference, TokenNotes),

                Plain(RecheckDue, "Final outcome owed",
                    "Case {{reference}} is waiting for its final outcome",
                    "The remediation on case {{reference}} is approved and the case is now at "
                        + "Awaiting Recheck. It is waiting for you to record the final outcome, which is "
                        + "what closes it - until then it stays open and does not reach the export. "
                        + "Open the case's remediation page and use Record the final outcome.",
                    TokenReference),

                Plain(SignoffDue, "Sign-off due",
                    "Sign-off needed on case {{reference}}",
                    "The adviser has completed every remediation action on case {{reference}}, "
                        + "so it is now waiting for your sign-off.",
                    TokenReference),
            };

            var map = new Dictionary<string, TemplateDefinition>(StringComparer.Ordinal);
            foreach (var definition in all)
            {
                map.Add(definition.Code, definition);
            }

            return map;
        }

        /// <summary>
        /// The remedial letter, as <c>NotificationBodies.Remediation</c> wrote it.
        ///
        /// The two gradings differ in two sentences, which is why they are two codes rather
        /// than one template carrying a conditional: a template language with branching in it
        /// is a language, and the person editing this is writing a letter.
        /// </summary>
        private static string RemediationBody(bool harm)
        {
            return "<p>Dear {{adviser}},</p>"
                + "<p>{{client}} has been subject to an AQS file check and a need for remedial work has been "
                + "identified due to the case receiving {{grading}}.{{dueText}}</p>"
                + "<p>The case summary in the portal details the remedial actions."
                + (harm ? " Please liaise with your T&amp;C Manager to move this case forward." : string.Empty)
                + "</p>"
                + "<p>Please follow the link to confirm that the "
                + (harm ? "required remedial action" : "remedial action")
                + " has been taken.</p>"
                + "{{caseButton}}"
                + "<p>Many thanks</p>";
        }

        private static TemplateDefinition Plain(
            string code, string name, string subject, string body, params string[] tokens)
        {
            return new TemplateDefinition
            {
                Code = code, Name = name, Subject = subject, Body = body,
                IsHtml = false, Tokens = tokens,
            };
        }

        private static TemplateDefinition Html(
            string code, string name, string subject, string body, string buttonLabel,
            params string[] tokens)
        {
            return new TemplateDefinition
            {
                Code = code, Name = name, Subject = subject, Body = body,
                IsHtml = true, Tokens = tokens, ButtonLabel = buttonLabel,
            };
        }
    }
}
