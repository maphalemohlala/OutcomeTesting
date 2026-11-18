using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Regression tests for the letters and documents audit of 2026-11-16: the PDF writer,
    /// the outbox, the templates, the frame and the drain. One region per finding.
    /// </summary>
    public class LettersAuditFixTests
    {
        private const double Margin = 42.5;

        private static string Pdf(byte[] bytes)
        {
            return Encoding.GetEncoding(28591).GetString(bytes);
        }

        /// <summary>Every drawn text run with its baseline, in the order drawn.</summary>
        private static List<KeyValuePair<double, string>> Drawn(string pdf)
        {
            return Regex.Matches(pdf, @"1 0 0 1 \S+ (-?[\d.]+) Tm\n\((.*?)\) Tj")
                .Cast<Match>()
                .Select(m => new KeyValuePair<double, string>(
                    double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value))
                .ToList();
        }

        // ---- 1. A row taller than a page ---------------------------------------------

        [Fact]
        public void A_row_taller_than_a_page_stays_on_the_page()
        {
            var t = new PdfTable(0.2, 0.8);
            t.Add(PdfCell.Of("Remedial action"), PdfCell.Of(string.Join("\n", Enumerable.Range(1, 120).Select(i => "line " + i))));
            var pdf = Pdf(PdfWriter.Build(new[] { PdfBlock.Table(t) }, true));
            var m = Regex.Match(pdf, @"1 0 0 1 \S+ (-?[\d.]+) Tm\n\(line 120\) Tj");
            Assert.True(m.Success, "line 120 not found in PDF");
            Assert.True(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) >= Margin, "line 120 drawn at y=" + m.Groups[1].Value);
        }

        [Fact]
        public void Every_line_of_a_split_row_is_drawn_once_inside_the_margins_under_a_repeated_heading()
        {
            var t = new PdfTable(0.2, 0.8);
            t.AddHeader(PdfCell.Head("Issue"), PdfCell.Head("Action"));
            t.Add(PdfCell.Of("Remedial action"), PdfCell.Of(string.Join("\n", Enumerable.Range(1, 200).Select(i => "line " + i))));
            t.Add(PdfCell.Of("After"), PdfCell.Of("the next row"));

            var pdf = Pdf(PdfWriter.Build(new[] { PdfBlock.Table(t) }));
            var drawn = Drawn(pdf);

            for (var i = 1; i <= 200; i++)
            {
                Assert.Single(drawn, d => d.Value == "line " + i);
            }

            Assert.All(drawn, d => Assert.InRange(d.Key, Margin, 841.89 - Margin));
            Assert.Contains(drawn, d => d.Value == "the next row");

            var pages = int.Parse(pdf.Substring(pdf.IndexOf("/Count ", StringComparison.Ordinal) + 7).Split(' ')[0], CultureInfo.InvariantCulture);
            Assert.True(pages > 1);
            Assert.Equal(pages, drawn.Count(d => d.Value == "Issue"));
        }

        // ---- 2. A long letter ---------------------------------------------------------

        private static string LongBody()
        {
            return "<p>" + new string('x', 3990) + "</p><p><a href=\"https://p.test/x\">Confirm</a></p>";
        }

        [Fact]
        public void A_letter_too_long_for_the_outbox_is_parked_with_its_reason_not_cut_and_sent()
        {
            var s = new FakeOrganizationService();
            NotificationOutbox.Queue(s, Guid.NewGuid(), NotificationOutbox.EventCasePassed, "al_outcomecase", Guid.NewGuid(),
                "a@b.test", "S", LongBody(), occurrence: null, templateCode: NotificationTemplates.CasePassed);

            var row = s.Creates.Single(e => e.LogicalName == "al_notification");
            Assert.Equal(NotificationOutbox.StatusFailed, row.GetAttributeValue<OptionSetValue>("al_status").Value);
            Assert.False(row.Contains("al_recipientemail"), "a parked letter must carry no address the drain could send to");
            var reason = row.GetAttributeValue<string>("al_failurereason");
            Assert.Contains("a@b.test", reason);
            Assert.Contains("4000", reason);
            Assert.True(row.GetAttributeValue<string>("al_body").Length <= NotificationOutbox.BodyLength);

            // And the drain never sends it, nor writes over why.
            var stored = s.Retrieve(NotificationOutbox.NotificationEntity, row.Id, NotificationDrain.Columns());
            Assert.Equal(NotificationDrain.Result.Skipped,
                NotificationDrain.Send(s, stored, new EntityReference("systemuser", Guid.NewGuid())));
            Assert.Empty(s.Requests);
            Assert.Equal(reason, s.Row(NotificationOutbox.NotificationEntity, row.Id).GetAttributeValue<string>("al_failurereason"));
        }

        [Fact]
        public void A_template_too_long_to_ever_send_is_refused_when_saved()
        {
            var refusal = NotificationTemplateGuardPlugin.Refusal(NotificationTemplates.CasePassed, "Case {{reference}}", LongBody());
            Assert.NotNull(refusal);
            Assert.Contains("could never be sent", refusal);
            Assert.DoesNotMatch(@"\b(AD|OD|BR|FR|PP)-\d", refusal);
        }

        // ---- 3. Plain letters escape their values -------------------------------------

        [Fact]
        public void Built_in_plain_letter_escapes_the_notes()
        {
            var letter = NotificationTemplates.Render(new FakeOrganizationService(), NotificationTemplates.SignoffApprovedRecheck,
                new Dictionary<string, string> { { "reference", "OT-1" }, { "notes", " Notes: <img src=x> & <a href=\"https://evil.test\">go</a>" } });
            var html = NotificationFrame.Wrap(letter.Body, "Outcome Testing");
            Assert.False(html.Contains("<img src=x>"), "raw markup reached the email");
            Assert.DoesNotContain("<a href=\"https://evil.test\">go</a>", html);
            Assert.Contains("&lt;img src=x&gt;", html);
        }

        // ---- 4. UK days in the documents ----------------------------------------------

        [Fact]
        public void Age_counts_uk_days()
        {
            Assert.Equal("1 working day", RemediationDocument.Age(
                new DateTime(2026, 9, 25, 23, 30, 0, DateTimeKind.Utc), new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc)));
        }

        [Theory]
        [InlineData(DateTimeKind.Utc, 23, "26 Sep 2026")]
        [InlineData(DateTimeKind.Utc, 22, "25 Sep 2026")]
        [InlineData(DateTimeKind.Unspecified, 0, "25 Sep 2026")] // a date-only value is the day it says
        public void A_date_in_a_document_is_the_uk_day(DateTimeKind kind, int hour, string printed)
        {
            var s = new FakeOrganizationService();
            var review = Guid.NewGuid();
            s.Seed("al_remediationaction", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", review),
                "al_description", "Issue one",
                "al_duedate", new DateTime(2026, 9, 25, hour, hour == 0 ? 0 : 30, 0, kind),
                "statecode", new OptionSetValue(0));

            var cells = CheckRemedialActions.Blocks(s, review)
                .Where(b => b.Grid != null)
                .SelectMany(b => b.Grid.Rows)
                .SelectMany(r => r.Cells)
                .Select(c => c.Text)
                .ToList();

            Assert.Contains(printed, cells);
        }

        // ---- 5. Inactive contacts ----------------------------------------------------

        [Fact]
        public void Inactive_named_contact_is_not_a_recipient()
        {
            var s = new FakeOrganizationService(); var id = Guid.NewGuid();
            s.Seed("contact", id, "fullname", "Left", "emailaddress1", "gone@example.com", "statecode", new OptionSetValue(1));
            var c = new NotificationTemplateRows.RecipientChoice(); c.Contacts.Add(new EntityReference("contact", id));

            var resolved = NotificationRecipients.Resolve(s, c, null, null).Single();
            Assert.Null(resolved.Email);
            Assert.Contains("no longer an active contact", resolved.Problem);
        }

        [Fact]
        public void An_inactive_contact_has_no_address_for_any_letter()
        {
            var s = new FakeOrganizationService(); var id = Guid.NewGuid();
            s.Seed("contact", id, "emailaddress1", "gone@example.com", "statecode", new OptionSetValue(1));
            Assert.Null(NotificationOutbox.ContactEmail(s, new EntityReference("contact", id)));

            s.Row("contact", id)["statecode"] = new OptionSetValue(0);
            Assert.Equal("gone@example.com", NotificationOutbox.ContactEmail(s, new EntityReference("contact", id)));
        }

        [Fact]
        public void A_letter_naming_an_inactive_contact_is_refused_when_saved()
        {
            var s = new FakeOrganizationService(); var id = Guid.NewGuid();
            s.Seed("contact", id, "fullname", "Left Ages Ago", "emailaddress1", "gone@example.com", "statecode", new OptionSetValue(1));

            var refusal = Assert.Throws<InvalidPluginExecutionException>(() => Guard(s, Custom(id.ToString())));
            Assert.Contains("Left Ages Ago is no longer an active contact", refusal.Message);
        }

        // ---- 7. A deleted contact is a query answer, not a fault ---------------------

        [Fact]
        public void A_deleted_named_contact_is_reported_without_a_faulting_retrieve()
        {
            var s = new FakeOrganizationService();
            var c = new NotificationTemplateRows.RecipientChoice(); c.Contacts.Add(new EntityReference("contact", Guid.NewGuid()));

            var resolved = NotificationRecipients.Resolve(s, c, null, null).Single();

            Assert.Null(resolved.Email);
            Assert.Equal("The chosen contact no longer exists.", resolved.Problem);
            Assert.Equal(0, s.RetrieveCount);
            Assert.Null(NotificationOutbox.ContactEmail(s, new EntityReference("contact", Guid.NewGuid())));
            Assert.Equal(0, s.RetrieveCount);
        }

        [Fact]
        public void A_letter_naming_a_deleted_contact_is_refused_without_a_faulting_retrieve()
        {
            var s = new FakeOrganizationService();

            var refusal = Assert.Throws<InvalidPluginExecutionException>(() => Guard(s, Custom(Guid.NewGuid().ToString())));
            Assert.Contains("no longer exists", refusal.Message);
            Assert.Equal(0, s.RetrieveCount);
        }

        // ---- 8, 9. Substitute ----------------------------------------------------------

        [Fact]
        public void Spaced_token_is_substituted()
        {
            Assert.Empty(NotificationTemplates.UnknownTokens(NotificationTemplates.CasePassed, "Case {{ reference }}", "<p>Dear {{ adviser }},</p>"));
            Assert.Equal("<p>Dear Ann,</p>", NotificationTemplates.Substitute("<p>Dear {{ adviser }},</p>", new Dictionary<string, string> { { "adviser", "Ann" } }, true));
        }

        [Fact]
        public void Values_are_not_re_expanded()
        {
            Assert.Equal("<p>Smith {{caseButton}}</p>", NotificationTemplates.Substitute("<p>{{client}}</p>",
                new Dictionary<string, string> { { "client", "Smith {{caseButton}}" }, { "caseButton", "<p><a href=\"https://p.test\">X</a></p>" } }, true));
        }

        [Fact]
        public void Unknown_and_broken_braces_are_left_as_written()
        {
            var tokens = new Dictionary<string, string> { { "reference", "OT-1" } };
            Assert.Equal("{{nope}} OT-1", NotificationTemplates.Substitute("{{nope}} {{reference}}", tokens, false));
            Assert.Equal("{{ OT-1", NotificationTemplates.Substitute("{{ {{reference}}", tokens, false));
            Assert.Equal("OT-1 {{reference", NotificationTemplates.Substitute("{{reference}} {{reference", tokens, false));
            Assert.Equal("a  b", NotificationTemplates.Substitute("a {{dueText}} b", tokens, false));
        }

        // ---- 10. The frame links only bare addresses ---------------------------------

        [Fact]
        public void Existing_anchor_survives_the_frame()
        {
            var html = NotificationFrame.Wrap("See <a href=\"https://p.test/x\">here</a>", "OT");
            Assert.DoesNotContain("href=\"<a", html);
            Assert.Contains("See <a href=\"https://p.test/x\">here</a>", html);
        }

        [Fact]
        public void A_bare_address_beside_an_anchor_is_still_linked()
        {
            var html = NotificationFrame.Wrap("Open https://p.test/a. Or <a href=\"https://p.test/b\">https://p.test/b</a>", "OT");
            Assert.Contains("<a href=\"https://p.test/a\" style=", html);
            Assert.Contains("<a href=\"https://p.test/b\">https://p.test/b</a>", html);
            Assert.Equal(2, Regex.Matches(html, "<a ").Count);
        }

        // ---- 11. Rich text keeps its lines in the PDF --------------------------------

        [Fact]
        public void Lists_keep_lines()
        {
            Assert.Equal("Fix A\nFix B", CompletedCheck.PlainText("<ul><li>Fix A</li><li>Fix B</li></ul>"));
            Assert.Equal("One\nTwo\nThree", CompletedCheck.PlainText("<p>One</p><p>Two<br/>Three</p>"));
            Assert.Equal("a b", CompletedCheck.PlainText("a<b>b</b>"));
            Assert.Equal("Tom & Jerry", CompletedCheck.PlainText("<div>Tom &amp; Jerry</div>"));
        }

        // ---- 12. A word wider than its cell ------------------------------------------

        [Fact]
        public void A_word_wider_than_its_cell_is_broken_inside_the_cell()
        {
            var link = "https://example.test/" + new string('w', 300);
            var t = new PdfTable(0.5, 0.5);
            t.Add(PdfCell.Of("before " + link + " after"), PdfCell.Of("x"));

            var drawn = Drawn(Pdf(PdfWriter.Build(new[] { PdfBlock.Table(t) })));
            var cellText = (595.28 - (2 * Margin)) / 2 - (2 * 4);

            var pieces = drawn.Where(d => d.Value != "x").Select(d => d.Value).ToList();
            Assert.All(pieces, p => Assert.True(PdfWriter.Width(p, "F1", 9) <= cellText + 0.01, "ran over the cell: " + p));
            Assert.True(pieces.Count > 3, "the link was not broken");
            Assert.Equal("before" + link + "after", string.Concat(pieces).Replace(" ", string.Empty));
        }

        // ---- 13. The drain's batch leaves out rows nobody can be sent ----------------

        [Fact]
        public void The_drain_query_leaves_out_rows_that_have_no_recipient()
        {
            var query = DrainNotificationsPlugin.PendingQuery(50);
            var conditions = query.Criteria.Conditions.Concat(query.Criteria.Filters.SelectMany(f => f.Conditions));
            Assert.Contains(conditions, c => c.AttributeName == "al_recipientemail" && c.Operator == ConditionOperator.NotNull);
        }

        [Fact]
        public void Unreached_rows_do_not_take_the_batch_and_keep_their_reason()
        {
            var s = new FakeOrganizationService();
            var unreached = Guid.NewGuid();
            s.Seed(NotificationOutbox.NotificationEntity, unreached,
                "al_status", new OptionSetValue(NotificationOutbox.StatusFailed),
                "al_failurereason", "Not sent to T&C Manager. No T&C Manager is mapped.",
                "al_queuedon", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var addressed = Guid.NewGuid();
            s.Seed(NotificationOutbox.NotificationEntity, addressed,
                "al_status", new OptionSetValue(NotificationOutbox.StatusPending),
                "al_recipientemail", "a@b.test",
                "al_queuedon", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

            var batch = s.RetrieveMultiple(DrainNotificationsPlugin.PendingQuery(1)).Entities;
            Assert.Equal(addressed, Assert.Single(batch).Id);

            // The async step still reaches it on create: the reason it was parked with stays.
            var row = s.Retrieve(NotificationOutbox.NotificationEntity, unreached, NotificationDrain.Columns());
            Assert.Equal(NotificationDrain.Result.Skipped, NotificationDrain.Send(s, row, Sender));
            Assert.Equal("Not sent to T&C Manager. No T&C Manager is mapped.",
                s.Row(NotificationOutbox.NotificationEntity, unreached).GetAttributeValue<string>("al_failurereason"));
        }

        // ---- 14. Two drains, one letter ----------------------------------------------

        private static readonly EntityReference Sender = new EntityReference("systemuser", Guid.NewGuid());

        private static FakeOrganizationService PendingRow(out Guid id)
        {
            var s = new FakeOrganizationService();
            id = Guid.NewGuid();
            s.Seed(NotificationOutbox.NotificationEntity, id,
                "al_status", new OptionSetValue(NotificationOutbox.StatusPending),
                "al_recipientemail", "a@b.test",
                "al_subject", "S",
                "al_body", "B");
            return s;
        }

        private static int SendEmails(FakeOrganizationService s)
        {
            return s.Requests.Count(r => r.RequestName == NotificationDrain.SendEmailMessage);
        }

        [Fact]
        public void A_second_drain_holding_the_same_pending_row_does_not_send_it_again()
        {
            Guid id;
            var s = PendingRow(out id);
            var seenByStep = s.Retrieve(NotificationOutbox.NotificationEntity, id, NotificationDrain.Columns());
            var seenByBatch = s.Retrieve(NotificationOutbox.NotificationEntity, id, NotificationDrain.Columns());

            Assert.Equal(NotificationDrain.Result.Sent, NotificationDrain.Send(s, seenByStep, Sender));
            Assert.Equal(NotificationDrain.Result.Skipped, NotificationDrain.Send(s, seenByBatch, Sender, true));
            Assert.Equal(1, SendEmails(s));
        }

        [Fact]
        public void A_row_another_drain_is_sending_now_is_left_to_it_until_the_claim_lapses()
        {
            Guid id;
            var s = PendingRow(out id);
            s.Row(NotificationOutbox.NotificationEntity, id)["al_senton"] = DateTime.UtcNow.AddMinutes(-1);

            var row = s.Retrieve(NotificationOutbox.NotificationEntity, id, NotificationDrain.Columns());
            Assert.Equal(NotificationDrain.Result.Skipped, NotificationDrain.Send(s, row, Sender));
            Assert.Equal(0, SendEmails(s));

            s.Row(NotificationOutbox.NotificationEntity, id)["al_senton"] = DateTime.UtcNow.AddHours(-1);
            row = s.Retrieve(NotificationOutbox.NotificationEntity, id, NotificationDrain.Columns());
            Assert.Equal(NotificationDrain.Result.Sent, NotificationDrain.Send(s, row, Sender));
            Assert.Equal(1, SendEmails(s));
        }

        [Fact]
        public void A_failed_send_releases_the_claim_so_the_row_is_retried()
        {
            Guid id;
            var s = PendingRow(out id);
            s.ExecuteThrows = new InvalidOperationException("The mailbox is not approved for sending.");

            var row = s.Retrieve(NotificationOutbox.NotificationEntity, id, NotificationDrain.Columns());
            Assert.Equal(NotificationDrain.Result.Failed, NotificationDrain.Send(s, row, Sender));
            Assert.Null(s.Row(NotificationOutbox.NotificationEntity, id).GetAttributeValue<DateTime?>("al_senton"));

            s.ExecuteThrows = null;
            row = s.Retrieve(NotificationOutbox.NotificationEntity, id, NotificationDrain.Columns());
            Assert.Equal(NotificationDrain.Result.Sent, NotificationDrain.Send(s, row, Sender));
        }

        [Fact]
        public void The_claim_is_conditional_on_the_row_version_and_a_lost_race_sends_nothing()
        {
            Guid id;
            var fake = PendingRow(out id);
            var s = new Versioned(fake);

            var row = s.Retrieve(NotificationOutbox.NotificationEntity, id, NotificationDrain.Columns());
            s.BeforeClaim = () => s.Version++; // another drain stamped the row in between
            Assert.Equal(NotificationDrain.Result.Skipped, NotificationDrain.Send(s, row, Sender));
            Assert.Equal(0, SendEmails(fake));

            s.BeforeClaim = null;
            row = s.Retrieve(NotificationOutbox.NotificationEntity, id, NotificationDrain.Columns());
            Assert.Equal(NotificationDrain.Result.Sent, NotificationDrain.Send(s, row, Sender));
            Assert.Equal(1, SendEmails(fake));
            Assert.Equal(2, s.Claims.Count);
            Assert.All(s.Claims, c => Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, c.ConcurrencyBehavior));
        }

        // ---- 15. Inside the batch's transaction a fault stops the batch, loudly -------

        [Fact]
        public void Inside_the_batch_a_service_fault_stops_it_with_the_row_and_the_reason()
        {
            Guid id;
            var s = PendingRow(out id);
            s.ExecuteThrows = Fault("The mailbox is not approved for sending.");

            var row = s.Retrieve(NotificationOutbox.NotificationEntity, id, NotificationDrain.Columns());
            var stopped = Assert.Throws<InvalidPluginExecutionException>(() => NotificationDrain.Send(s, row, Sender, true));
            Assert.Contains(id.ToString("D"), stopped.Message);
            Assert.Contains("The mailbox is not approved for sending.", stopped.Message);

            // The async step, outside any transaction, still records it on the row.
            row = s.Retrieve(NotificationOutbox.NotificationEntity, id, NotificationDrain.Columns());
            row["al_senton"] = null;
            s.Row(NotificationOutbox.NotificationEntity, id)["al_senton"] = null;
            Assert.Equal(NotificationDrain.Result.Failed, NotificationDrain.Send(s, row, Sender));
        }

        // ---- 16. A different adviser gets their own letter ---------------------------

        private static readonly Guid CaseId = Guid.Parse("0d0d0d0d-1d1d-4e1e-8f1f-0a0a0a0a0a0a");
        private static readonly Guid ReviewId = Guid.Parse("1c1c1c1c-2c2c-4d2d-8e2e-1f1f1f1f1f1f");

        private static FakeOrganizationService RemediationCase()
        {
            var s = new FakeOrganizationService();
            s.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-TEST-200",
                "al_advisername", "Sam Adviser",
                "al_adviseremail", "sam@example.com",
                "al_clientname", "Mr Jones");
            s.Seed("contact", Guid.NewGuid(), "fullname", "Sam Adviser", "emailaddress1", "sam@example.com", "statecode", new OptionSetValue(0));
            s.Seed("contact", Guid.NewGuid(), "fullname", "Bea Adviser", "emailaddress1", "bea@example.com", "statecode", new OptionSetValue(0));
            for (var i = 0; i < 2; i++)
            {
                s.Seed("al_remediationaction", Guid.NewGuid(),
                    "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                    "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                    "al_actionstatus", new OptionSetValue(Remediation.StatusOpen),
                    "al_name", "Remediation IO-TEST-200");
            }

            return s;
        }

        private static List<string> AdviserLetters(FakeOrganizationService s)
        {
            return s.Creates
                .Where(c => c.LogicalName == NotificationOutbox.NotificationEntity
                    && c.GetAttributeValue<OptionSetValue>("al_event").Value == NotificationOutbox.EventRemediationAssigned
                    && !(c.GetAttributeValue<string>("al_notificationcode") ?? string.Empty)
                        .EndsWith("-" + ParaplannerRemediationLetter.Occurrence, StringComparison.Ordinal))
                .Select(c => c.GetAttributeValue<string>("al_recipientemail"))
                .ToList();
        }

        [Fact]
        public void Actions_moved_to_a_new_adviser_tell_the_new_adviser_once()
        {
            var s = RemediationCase();
            var caseRef = new EntityReference("al_outcomecase", CaseId);

            Remediation.AssignOpenActions(s, caseRef, Guid.NewGuid());
            Assert.Equal(new[] { "sam@example.com" }, AdviserLetters(s));

            s.Row("al_outcomecase", CaseId)["al_adviseremail"] = "bea@example.com";
            Remediation.AssignOpenActions(s, caseRef, Guid.NewGuid());

            // Two actions, one review: one letter each adviser, not one per action.
            Assert.Equal(new[] { "sam@example.com", "bea@example.com" }, AdviserLetters(s));
        }

        // ---- 17. Template codes compare the same everywhere --------------------------

        [Fact]
        public void A_template_code_is_the_same_letter_whatever_its_case_or_spacing()
        {
            Assert.Same(NotificationTemplates.Definition(NotificationTemplates.CasePassed),
                NotificationTemplates.Definition("  case-passed "));
            Assert.Null(NotificationTemplateGuardPlugin.Refusal(" case-passed", "Case {{reference}}", "<p>Dear {{adviser}}</p>"));
        }

        [Fact]
        public void A_built_in_code_in_lower_case_is_not_sent_again_as_an_extra_letter()
        {
            var s = new FakeOrganizationService();
            s.Seed(NotificationTemplates.TemplateEntity, Guid.NewGuid(),
                NotificationTemplates.CodeAttr, "case-passed ",
                NotificationTemplates.SubjectAttr, "Subject",
                NotificationTemplates.BodyAttr, "<p>Body</p>",
                NotificationTemplateRows.EventAttr, new OptionSetValue(NotificationOutbox.EventCasePassed),
                NotificationTemplateRows.RecipientKindsAttr, NotificationRecipients.KindAdviser.ToString(CultureInfo.InvariantCulture),
                "statecode", new OptionSetValue(0));

            Assert.Empty(NotificationTemplateRows.CustomFor(s, NotificationOutbox.EventCasePassed));
        }

        [Fact]
        public void The_guard_stores_the_code_trimmed_and_upper_case()
        {
            var row = new Entity(NotificationTemplates.TemplateEntity);
            row[NotificationTemplates.CodeAttr] = " case-passed ";
            row[NotificationTemplates.SubjectAttr] = "Case {{reference}}";
            row[NotificationTemplates.BodyAttr] = "<p>Dear {{adviser}}</p>";

            Guard(new FakeOrganizationService(), row);

            Assert.Equal(NotificationTemplates.CasePassed, row.GetAttributeValue<string>(NotificationTemplates.CodeAttr));
        }

        // ---- 6. A configuration read that failed is not "unset" ----------------------

        [Fact]
        public void An_unreadable_portal_variable_gives_no_link_rather_than_the_site_rows_stale_host()
        {
            var fake = new FakeOrganizationService();
            fake.Seed("powerpagesite", Guid.NewGuid(), "primarydomainname", "outcometesting.powerappsportals.com");
            var s = new ThrowsOn(fake, "environmentvariabledefinition");

            string value;
            Assert.False(EnvironmentVariable.TryRead(s, PortalSite.BaseUrlVariable, out value));
            Assert.Null(PortalSite.BaseUrl(s));

            // Genuinely unset still falls back to the site row, as it always has.
            Assert.Equal("https://outcometesting.powerappsportals.com", PortalSite.BaseUrl(fake));
        }

        [Fact]
        public void An_unreadable_sender_address_refuses_rather_than_sending_from_the_run_as_account()
        {
            var fake = new FakeOrganizationService();
            var s = new ThrowsOn(fake, "environmentvariabledefinition");

            string problem;
            Assert.Null(NotificationSender.Resolve(s, Sender, out problem));
            Assert.Contains(NotificationSender.AddressVariable, problem);

            // Genuinely unset still sends from the run-as account.
            Assert.Same(Sender, NotificationSender.Resolve(fake, Sender, out problem));
            Assert.Null(problem);
        }

        // ---- helpers -------------------------------------------------------------------

        private static Entity Custom(string contactIds)
        {
            var row = new Entity(NotificationTemplates.TemplateEntity);
            row[NotificationTemplates.CodeAttr] = "TELL-SOMEBODY";
            row[NotificationTemplates.SubjectAttr] = "Subject";
            row[NotificationTemplates.BodyAttr] = "<p>Body</p>";
            row[NotificationTemplateRows.EventAttr] = new OptionSetValue(NotificationOutbox.EventRemediationAssigned);
            row[NotificationTemplateRows.RecipientContactIdsAttr] = contactIds;
            return row;
        }

        private static void Guard(FakeOrganizationService service, Entity row)
        {
            var provider = new FakeServiceProvider(service);
            provider.Context.InputParameters["Target"] = row;
            provider.Context.MessageName = "Create";

            new NotificationTemplateGuardPlugin(null, null).Execute(provider);
        }

        private static System.ServiceModel.FaultException<OrganizationServiceFault> Fault(string message, int code = 0)
        {
            return new System.ServiceModel.FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = message, ErrorCode = code },
                new System.ServiceModel.FaultReason(message));
        }

        /// <summary>The fake, with one table that cannot be read - as a missing privilege refuses it.</summary>
        private sealed class ThrowsOn : IOrganizationService
        {
            private readonly FakeOrganizationService _inner;
            private readonly string _table;

            public ThrowsOn(FakeOrganizationService inner, string table)
            {
                _inner = inner;
                _table = table;
            }

            public EntityCollection RetrieveMultiple(QueryBase query)
            {
                var expression = query as QueryExpression;
                if (expression != null && string.Equals(expression.EntityName, _table, StringComparison.OrdinalIgnoreCase))
                {
                    throw Fault("Principal user is missing prvRead" + _table + " privilege.");
                }

                return _inner.RetrieveMultiple(query);
            }

            public Guid Create(Entity entity) { return _inner.Create(entity); }
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) { return _inner.Retrieve(entityName, id, columnSet); }
            public void Update(Entity entity) { _inner.Update(entity); }
            public void Delete(string entityName, Guid id) { _inner.Delete(entityName, id); }
            public OrganizationResponse Execute(OrganizationRequest request) { return _inner.Execute(request); }
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { _inner.Associate(entityName, entityId, relationship, relatedEntities); }
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { _inner.Disassociate(entityName, entityId, relationship, relatedEntities); }
        }

        /// <summary>
        /// The fake with a row version, and an UpdateRequest that honours it the way the
        /// platform does - which the shared fake does not model.
        /// </summary>
        private sealed class Versioned : IOrganizationService
        {
            private readonly FakeOrganizationService _inner;

            public Versioned(FakeOrganizationService inner)
            {
                _inner = inner;
            }

            public int Version { get; set; } = 1;

            public Action BeforeClaim { get; set; }

            public List<UpdateRequest> Claims { get; } = new List<UpdateRequest>();

            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
            {
                var row = _inner.Retrieve(entityName, id, columnSet);
                row.RowVersion = Version.ToString(CultureInfo.InvariantCulture);
                return row;
            }

            public OrganizationResponse Execute(OrganizationRequest request)
            {
                var update = request as UpdateRequest;
                if (update == null)
                {
                    return _inner.Execute(request);
                }

                Claims.Add(update);
                BeforeClaim?.Invoke();
                if (update.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches
                    && update.Target.RowVersion != Version.ToString(CultureInfo.InvariantCulture))
                {
                    throw Fault("The version of the existing record doesn't match the RowVersion property provided.",
                        unchecked((int)0x80060882));
                }

                Update(update.Target);
                return new UpdateResponse();
            }

            public void Update(Entity entity)
            {
                _inner.Update(entity);
                Version++;
            }

            public Guid Create(Entity entity) { return _inner.Create(entity); }
            public void Delete(string entityName, Guid id) { _inner.Delete(entityName, id); }
            public EntityCollection RetrieveMultiple(QueryBase query) { return _inner.RetrieveMultiple(query); }
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { _inner.Associate(entityName, entityId, relationship, relatedEntities); }
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { _inner.Disassociate(entityName, entityId, relationship, relatedEntities); }
        }
    }
}
