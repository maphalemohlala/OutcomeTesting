using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// That the recipient an administrator chooses (AD-168) reaches the code that actually
    /// sends the letter.
    ///
    /// <para>
    /// F33, found in DEV on 2026-09-20 working APP-137. Setting "Goes to" to T&amp;C Manager on
    /// the allocation letter and then allocating a case sent it to the checker as before.
    /// <c>NotificationOutbox.Queue</c> reads the override from the template row named by its
    /// <c>templateCode</c> argument - and that argument was optional, defaulting to null, and
    /// not one of the seven places that queue a built-in letter passed it. So
    /// <c>SettingsFor(service, null)</c> returned nothing every time and the override was
    /// dead for all twelve built-in letters.
    /// </para>
    /// <para>
    /// <see cref="NotificationRecipientTests"/> covers the override thoroughly and passes,
    /// because every one of its cases calls <c>Queue</c> with <c>templateCode:</c> supplied
    /// by hand. It tested the seam rather than the path to it. These tests go through the
    /// emitters the product uses, which is the only place the bug was visible.
    /// </para>
    /// </summary>
    public class NotificationRecipientReachesSenderTests
    {
        private static readonly Guid CaseId = Guid.Parse("cafe0001-0001-4001-8001-000000000001");
        private static readonly Guid AssignmentId = Guid.Parse("cafe0002-0002-4002-8002-000000000002");
        private static readonly Guid Correlation = Guid.NewGuid();

        [Fact]
        public void The_allocation_letter_goes_where_the_template_says()
        {
            var service = Case();
            Allocation(service);
            Template(service, NotificationTemplates.AllocationNoLink, NotificationRecipients.KindContact);

            NotificationEmitterPlugin.QueueAllocation(service, Correlation, AssignmentId);

            Assert.Equal("manager@example.com", Recipients(service).Single());
        }

        [Fact]
        public void The_pass_letter_goes_where_the_template_says()
        {
            var service = Case();
            Template(service, NotificationTemplates.CasePassed, NotificationRecipients.KindContact);

            NotificationEmitterPlugin.QueueCasePassed(
                service, Correlation, new EntityReference("al_outcomecase", CaseId));

            Assert.Equal("manager@example.com", Recipients(service).Single());
        }

        [Fact]
        public void An_allocation_with_nothing_chosen_still_reaches_the_checker()
        {
            // The negative. Wiring the template code through must not change the routing of
            // the eleven twelfths of letters nobody has overridden.
            var service = Case();
            Allocation(service);

            NotificationEmitterPlugin.QueueAllocation(service, Correlation, AssignmentId);

            Assert.Equal("checker@example.com", Recipients(service).Single());
        }

        [Fact]
        public void No_way_of_queueing_a_letter_lets_the_caller_leave_the_template_out()
        {
            // What makes the fix hold. The override is only ever read from the row the
            // template code names, so a call site that omits it silently disables the
            // feature for that letter - which is exactly how this shipped. Required rather
            // than defaulted means the compiler catches the ninth call site, and no test has
            // to remember to.
            var source = File.ReadAllText(OutboxPath());

            Assert.DoesNotContain("string templateCode = null", source);
            Assert.Contains("string templateCode", source);
        }

        [Fact]
        public void Every_built_in_letter_is_queued_under_a_code_an_administrator_can_edit()
        {
            // A code passed to Queue that is not one of the twelve would read no template
            // row, so the override and the stored wording would both be dead for it while
            // looking wired.
            var sources = Directory.GetFiles(PluginsPath(), "*.cs")
                .Where(p => !p.EndsWith("NotificationOutbox.cs", StringComparison.Ordinal))
                .ToArray();

            var checked_ = 0;

            foreach (var path in sources)
            {
                var text = File.ReadAllText(path);
                foreach (Match call in Regex.Matches(
                    text, @"NotificationOutbox\.Queue(WithCompletedCheck)?\((?<args>[^;]*?)\);",
                    RegexOptions.Singleline))
                {
                    var args = call.Groups["args"].Value;
                    checked_++;

                    Assert.True(
                        args.Contains("NotificationTemplates.") || args.Contains("code"),
                        Path.GetFileName(path) + " queues a letter without naming its template, "
                        + "so an administrator's choice of recipient and wording is ignored "
                        + "for it (F33).");
                }
            }

            // Guards the test itself: a rename that broke the pattern would otherwise make
            // the loop above vacuously true.
            // Seven today: three in the emitter, two in sign-off progress, one in
            // complete-remediation and the para-planner's in submit-review.
            Assert.True(checked_ >= 7, "Found only " + checked_ + " places that queue a letter.");
        }

        // ------------------------------------------------------------ fixtures

        private static FakeOrganizationService Case()
        {
            var service = new FakeOrganizationService();
            service.Seed("al_outcomecase", CaseId,
                "al_casereference", "OT-2026-0433",
                "al_clientname", "A. Client",
                "al_advisername", "Adviser User 1",
                "al_adviseremail", "adviser@example.com",
                "statecode", 0);
            service.Seed("contact", Guid.Parse("cafe0003-0003-4003-8003-000000000003"),
                "fullname", "Adviser User 1",
                "emailaddress1", "adviser@example.com",
                "statecode", new OptionSetValue(0));
            return service;
        }

        private static void Allocation(FakeOrganizationService service)
        {
            var checker = service.Seed("contact", Guid.Parse("cafe0004-0004-4004-8004-000000000004"),
                "fullname", "A Checker",
                "emailaddress1", "checker@example.com",
                "statecode", new OptionSetValue(0));

            service.Seed("al_caseassignment", AssignmentId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_assignedcontactid", new EntityReference("contact", checker.Id),
                "al_isactive", true,
                "al_assignedon", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc));
        }

        private static void Template(FakeOrganizationService service, string code, int kind)
        {
            var manager = service.Seed("contact", Guid.NewGuid(),
                "emailaddress1", "manager@example.com", "statecode", 0);

            service.Seed(NotificationTemplates.TemplateEntity, Guid.NewGuid(),
                NotificationTemplates.CodeAttr, code,
                NotificationTemplateRows.RecipientKindAttr, new OptionSetValue(kind),
                NotificationTemplateRows.RecipientContactAttr, new EntityReference("contact", manager.Id),
                "statecode", 0);
        }

        private static string[] Recipients(FakeOrganizationService service)
        {
            var query = new QueryExpression(NotificationOutbox.NotificationEntity)
            {
                ColumnSet = new ColumnSet("al_recipientemail"),
            };

            return service.RetrieveMultiple(query).Entities
                .Select(e => e.GetAttributeValue<string>("al_recipientemail"))
                .ToArray();
        }

        private static string PluginsPath()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null
                && !Directory.Exists(Path.Combine(
                    directory.FullName, "plugins", "OutcomeTesting.Plugins")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(directory.FullName, "plugins", "OutcomeTesting.Plugins");
        }

        private static string OutboxPath()
        {
            return Path.Combine(PluginsPath(), "NotificationOutbox.cs");
        }
    }
}
