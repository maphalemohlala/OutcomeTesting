using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The adviser-facing wording for the three emails the project owner supplied
    /// (2026-09-10): a clean pass, and the two remedial letters that differ only by the
    /// grading that earned them.
    ///
    /// Kept free of Dataverse types on purpose, as OutcomeRules and ResponseRules are, so
    /// the exact words an adviser reads can be asserted without a fake organisation
    /// service. The wiring that decides WHICH of these to send is tested against the
    /// emitter; this file only tests what they say.
    /// </summary>
    public class NotificationBodiesTests
    {
        private const string Link = "https://portal.example.com/case-details?id=abc";

        [Fact]
        public void Pass_confirms_the_grade_and_asks_for_nothing()
        {
            var body = NotificationBodies.Pass("Jane Adviser", "Mr and Mrs Smith", Link);

            Assert.StartsWith("<p>Dear Jane Adviser,</p>", body);
            Assert.Contains("Mr and Mrs Smith has been checked and graded a Pass", body);
            Assert.Contains("No further action is required.", body);
            Assert.Contains("Kind regards", body);
        }

        [Fact]
        public void Pass_carries_the_case_link()
        {
            var body = NotificationBodies.Pass("Jane Adviser", "Mr and Mrs Smith", Link);

            Assert.Contains(Link, body);
        }

        [Fact]
        public void Pass_drops_the_link_sentence_when_there_is_no_site()
        {
            // CaseLink returns null where the environment has no portal. A sentence that
            // offers a link and then shows nothing is worse than no sentence at all.
            var body = NotificationBodies.Pass("Jane Adviser", "Mr and Mrs Smith", null);

            Assert.DoesNotContain("<a href", body);
            Assert.Contains("No further action is required.", body);
        }

        [Fact]
        public void Pass_with_issues_names_its_grading_and_links_the_confirmation()
        {
            var body = NotificationBodies.Remediation(
                OutcomeRules.OutcomePassWithIssues, "Jane Adviser", "Mr and Mrs Smith", Link, null);

            Assert.StartsWith("<p>Dear Jane Adviser,</p>", body);
            Assert.Contains("Mr and Mrs Smith has been subject to an AQS file check", body);
            Assert.Contains("pass with issues grading", body);
            Assert.Contains("confirm that the remedial action has been taken", body);
            Assert.Contains(Link, body);
            Assert.Contains("Many thanks", body);
        }

        [Fact]
        public void Pass_with_issues_points_at_the_portal_not_an_attachment()
        {
            // The supplied copy said "The attached Case Summary". Delivery is a subject and
            // a body with nothing attached, so the sentence points at the page the link
            // already opens rather than promising a file that is not there.
            var body = NotificationBodies.Remediation(
                OutcomeRules.OutcomePassWithIssues, "Jane Adviser", "Mr and Mrs Smith", Link, null);

            Assert.DoesNotContain("attached", body);
            Assert.Contains("case summary in the portal details the remedial actions", body);
        }

        [Fact]
        public void Insufficient_evidence_names_its_grading_and_sends_them_to_their_manager()
        {
            var body = NotificationBodies.Remediation(
                OutcomeRules.OutcomeInsufficient, "Jane Adviser", "Mr and Mrs Smith", Link, null);

            Assert.Contains("insufficient evidence/ potential harm grading", body);
            Assert.Contains("liaise with your T&amp;C Manager", body);
            Assert.Contains(Link, body);
        }

        [Fact]
        public void Potential_harm_reads_as_the_same_letter_as_insufficient_evidence()
        {
            // One letter covers both gradings, as the supplied copy does.
            var insufficient = NotificationBodies.Remediation(
                OutcomeRules.OutcomeInsufficient, "Jane Adviser", "Mr and Mrs Smith", Link, null);
            var harm = NotificationBodies.Remediation(
                OutcomeRules.OutcomePotentialHarm, "Jane Adviser", "Mr and Mrs Smith", Link, null);

            Assert.Equal(insufficient, harm);
        }

        [Fact]
        public void Remediation_carries_the_due_date_when_the_action_has_one()
        {
            var body = NotificationBodies.Remediation(
                OutcomeRules.OutcomePassWithIssues, "Jane Adviser", "Mr and Mrs Smith", Link,
                " It is due by 1 October 2026.");

            Assert.Contains("It is due by 1 October 2026.", body);
        }

        [Fact]
        public void Remediation_has_no_letter_for_a_grading_the_copy_does_not_cover()
        {
            // A flagged Pass and a Tax non-pass both raise remediation without earning
            // either supplied grading. Returning null lets the caller keep the wording
            // those cases already had, rather than telling an adviser their Pass was a
            // pass with issues.
            Assert.Null(NotificationBodies.Remediation(
                OutcomeRules.OutcomePass, "Jane Adviser", "Mr and Mrs Smith", Link, null));
            Assert.Null(NotificationBodies.Remediation(
                null, "Jane Adviser", "Mr and Mrs Smith", Link, null));
        }

        [Fact]
        public void An_unmatched_adviser_is_still_addressed()
        {
            // AdviserContact refuses to guess between two people of the same name, and the
            // case may name nobody at all. The letter still has to read as a letter.
            var body = NotificationBodies.Pass(null, "Mr and Mrs Smith", Link);

            Assert.StartsWith("<p>Dear Adviser,</p>", body);
        }

        [Fact]
        public void A_case_with_no_client_named_still_reads_as_a_sentence()
        {
            var body = NotificationBodies.Pass("Jane Adviser", "  ", Link);

            Assert.Contains("This case has been checked and graded a Pass", body);
        }

        [Fact]
        public void Subjects_name_the_letter_and_the_case()
        {
            // An adviser holds many cases, so the reference belongs in the subject line -
            // the convention every other PP-15 subject already follows.
            Assert.Equal("Case check - Pass: OT-1001", NotificationBodies.PassSubject("OT-1001"));
            Assert.Equal("Remedial needed - Pass with issues: OT-1001",
                NotificationBodies.RemediationSubject(OutcomeRules.OutcomePassWithIssues, "OT-1001"));
            Assert.Equal("Remedial needed - insufficient evidence/ potential harm: OT-1001",
                NotificationBodies.RemediationSubject(OutcomeRules.OutcomeInsufficient, "OT-1001"));
        }

        [Fact]
        public void An_uncovered_grading_has_no_subject_either()
        {
            Assert.Null(NotificationBodies.RemediationSubject(OutcomeRules.OutcomePass, "OT-1001"));
        }
        [Fact]
        public void Pass_renders_the_link_as_an_anchor()
        {
            var body = NotificationBodies.Pass("Jane Adviser", "Mr and Mrs Smith", Link);

            Assert.Contains("<a href=\"" + Link + "\"", body);
            Assert.Contains("</a>", body);
        }

        [Fact]
        public void Remediation_renders_the_confirmation_link_as_an_anchor()
        {
            var body = NotificationBodies.Remediation(
                OutcomeRules.OutcomePassWithIssues, "Jane Adviser", "Mr and Mrs Smith", Link, null);

            Assert.Contains("<a href=\"" + Link + "\"", body);
        }

        [Fact]
        public void A_client_name_carrying_markup_cannot_reach_the_reader_as_markup()
        {
            // The body is HTML now, and the client name is copied off the case. Anything
            // that is not escaped here is markup in somebody else's mailbox.
            var body = NotificationBodies.Pass("Jane Adviser", "<b>Smith & Co</b>", Link);

            Assert.DoesNotContain("<b>", body);
            Assert.Contains("&lt;b&gt;", body);
            Assert.Contains("Smith &amp; Co", body);
        }

        [Fact]
        public void An_adviser_name_carrying_markup_is_escaped_too()
        {
            var body = NotificationBodies.Pass("Jane <script>alert(1)</script>", "Mr and Mrs Smith", Link);

            Assert.DoesNotContain("<script>", body);
            Assert.Contains("&lt;script&gt;", body);
        }

        [Fact]
        public void The_manager_sentence_escapes_its_ampersand()
        {
            // "T&C Manager" is the supplied copy. Left raw it is an unterminated entity.
            var body = NotificationBodies.Remediation(
                OutcomeRules.OutcomeInsufficient, "Jane Adviser", "Mr and Mrs Smith", Link, null);

            Assert.Contains("T&amp;C Manager", body);
        }

        [Fact]
        public void A_link_carrying_a_query_string_is_escaped_for_the_attribute()
        {
            // A real portal link joins its parameters with &, which is an entity start
            // inside an href.
            var body = NotificationBodies.Pass(
                "Jane Adviser", "Mr and Mrs Smith", "https://p.example.com/c?id=abc&mode=view");

            Assert.Contains("id=abc&amp;mode=view", body);
            Assert.DoesNotContain("id=abc&mode=view", body);
        }

        [Fact]
        public void Line_breaks_are_markup_rather_than_newlines()
        {
            // A newline is whitespace in HTML. Without paragraphs the whole letter arrives
            // as one run-on line.
            var body = NotificationBodies.Pass("Jane Adviser", "Mr and Mrs Smith", Link);

            Assert.Contains("<p>", body);
        }
    }
}
