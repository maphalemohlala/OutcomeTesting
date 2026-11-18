using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The question commands act on the version in force on the day the change takes effect,
    /// not merely the highest-numbered one. Each scenario here used to leave the checklist
    /// asking a question twice, or bring a retired one back.
    ///
    /// Run end to end through the plug-ins: the mapping table is empty, so the permission
    /// gate's bootstrap admits the caller.
    /// </summary>
    public class QuestionCommandFixTests
    {
        private static readonly Guid QuestionId = Guid.Parse("66666666-6666-4666-8666-666666666666");
        private static readonly Guid FromSection = Guid.Parse("77777777-7777-4777-8777-777777777777");
        private static readonly Guid ToSection = Guid.Parse("88888888-8888-4888-8888-888888888888");

        private static DateTime Today
        {
            get { return DateTime.UtcNow.Date; }
        }

        private static string Day(DateTime value)
        {
            return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static FakeOrganizationService Checklist()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_question", QuestionId,
                "al_questioncode", "Q-E1-01", "al_name", "Ordinary", "al_displayorder", 1,
                "al_sectionid", new EntityReference("al_section", FromSection));
            svc.Seed("al_section", FromSection, "al_name", "From");
            return svc;
        }

        private static Guid Version(FakeOrganizationService svc, int number, DateTime from, DateTime? to)
        {
            var id = Guid.NewGuid();
            var row = svc.Seed("al_questionversion", id,
                "al_questionid", new EntityReference("al_question", QuestionId),
                "al_versionnumber", number, "al_questiontext", "v" + number,
                "al_effectivefrom", from, "al_ismandatory", true, "al_displayorder", 1,
                "al_responsetype", new OptionSetValue(120910000));
            if (to.HasValue)
            {
                row["al_effectiveto"] = to.Value;
            }

            return id;
        }

        private static void Run(IPlugin plugin, FakeOrganizationService svc, params string[] pairs)
        {
            var provider = new FakeServiceProvider(svc);
            for (var i = 0; i < pairs.Length; i += 2)
            {
                provider.Context.InputParameters[pairs[i]] = pairs[i + 1];
            }

            provider.Context.InputParameters["IdempotencyKey"] = Guid.NewGuid().ToString("D");
            plugin.Execute(provider);
        }

        private static void Edit(FakeOrganizationService svc, DateTime from)
        {
            Run(new RetireAndSucceedQuestionPlugin(null, null), svc,
                "QuestionId", QuestionId.ToString("D"), "NewWording", "Reworded", "EffectiveFrom", Day(from));
        }

        private static string Refusal(Action command)
        {
            return Assert.Throws<InvalidPluginExecutionException>(command).Message;
        }

        private static DateTime? EndOf(FakeOrganizationService svc, Guid versionId)
        {
            return svc.Row("al_questionversion", versionId).GetAttributeValue<DateTime?>("al_effectiveto");
        }

        // (a) v1 runs to day 15, v2 is scheduled from day 15. An edit dated today used to end
        // v2 before it began and start v3 today, while v1 was still in force to day 15.
        [Fact]
        public void An_edit_dated_before_a_scheduled_version_is_refused_and_writes_nothing()
        {
            var svc = Checklist();
            var v1 = Version(svc, 1, Today.AddDays(-30), Today.AddDays(15));
            var v2 = Version(svc, 2, Today.AddDays(15), null);

            var message = Refusal(() => Edit(svc, Today));

            Assert.Contains("already scheduled", message);
            Assert.Contains(Day(Today.AddDays(15)), message);
            Assert.Equal(Today.AddDays(15), EndOf(svc, v1));
            Assert.Null(EndOf(svc, v2));
            Assert.Equal(2, svc.All("al_questionversion").Count());
        }

        [Fact]
        public void An_edit_dated_on_or_after_a_scheduled_version_succeeds_it()
        {
            var svc = Checklist();
            Version(svc, 1, Today.AddDays(-30), Today.AddDays(15));
            var v2 = Version(svc, 2, Today.AddDays(15), null);

            Edit(svc, Today.AddDays(20));

            Assert.Equal(Today.AddDays(20), EndOf(svc, v2));
            var v3 = svc.All("al_questionversion").Single(v => v.GetAttributeValue<int>("al_versionnumber") == 3);
            Assert.Equal(Today.AddDays(20), v3.GetAttributeValue<DateTime?>("al_effectivefrom"));
        }

        // (b) Retirement planned for day 15, then an edit dated day 25. v1's end used to move
        // to day 25 and v2 was open-ended, so the retirement was silently undone.
        [Fact]
        public void An_edit_dated_after_a_planned_retirement_is_refused()
        {
            var svc = Checklist();
            var v1 = Version(svc, 1, Today.AddDays(-30), Today.AddDays(15));

            var message = Refusal(() => Edit(svc, Today.AddDays(25)));

            Assert.Contains("due to retire on " + Day(Today.AddDays(15)), message);
            Assert.Equal(Today.AddDays(15), EndOf(svc, v1));
            Assert.Single(svc.All("al_questionversion"));
        }

        [Fact]
        public void An_edit_dated_before_a_planned_retirement_carries_the_retirement_forward()
        {
            var svc = Checklist();
            Version(svc, 1, Today.AddDays(-30), Today.AddDays(15));

            Edit(svc, Today.AddDays(5));

            var v2 = svc.All("al_questionversion").Single(v => v.GetAttributeValue<int>("al_versionnumber") == 2);
            Assert.Equal(Today.AddDays(15), v2.GetAttributeValue<DateTime?>("al_effectiveto"));
        }

        // (c) A question retired last month. An edit used to date v1 out again - later - and
        // create an open-ended v2, reviving it.
        [Fact]
        public void A_retired_question_cannot_be_edited()
        {
            var svc = Checklist();
            var retiredOn = Today.AddDays(-10);
            var v1 = Version(svc, 1, Today.AddDays(-60), retiredOn);

            var message = Refusal(() => Edit(svc, Today));

            Assert.Contains("was retired on " + Day(retiredOn), message);
            Assert.Equal(retiredOn, EndOf(svc, v1));
            Assert.Single(svc.All("al_questionversion"));
        }

        [Fact]
        public void A_question_retired_today_cannot_be_edited()
        {
            var svc = Checklist();
            Version(svc, 1, Today.AddDays(-60), Today);

            Assert.Contains("was retired on", Refusal(() => Edit(svc, Today)));
        }

        [Fact]
        public void A_first_version_not_yet_started_may_still_be_reworded()
        {
            // Nothing runs before it, so ending it early overlaps nothing.
            var svc = Checklist();
            Version(svc, 1, Today.AddDays(10), null);

            Edit(svc, Today.AddDays(10));

            Assert.Equal(2, svc.All("al_questionversion").Count());
        }

        [Fact]
        public void A_retirement_dated_before_a_scheduled_version_is_refused()
        {
            // Dating out v2 before it starts leaves v1 running to day 15: the question would
            // not stop on the day the administrator chose.
            var svc = Checklist();
            Version(svc, 1, Today.AddDays(-30), Today.AddDays(15));
            var v2 = Version(svc, 2, Today.AddDays(15), null);

            var message = Refusal(() => Run(new RetireQuestionPlugin(null, null), svc,
                "QuestionId", QuestionId.ToString("D"), "Reason", "No longer needed"));

            Assert.Contains("already scheduled", message);
            Assert.Null(EndOf(svc, v2));
        }

        [Fact]
        public void A_retirement_dated_on_the_scheduled_day_retires_the_scheduled_version()
        {
            var svc = Checklist();
            Version(svc, 1, Today.AddDays(-30), Today.AddDays(15));
            var v2 = Version(svc, 2, Today.AddDays(15), null);

            Run(new RetireQuestionPlugin(null, null), svc,
                "QuestionId", QuestionId.ToString("D"), "Reason", "No longer needed",
                "EffectiveTo", Day(Today.AddDays(15)));

            Assert.Equal(Today.AddDays(15), EndOf(svc, v2));
        }

        private static void Move(FakeOrganizationService svc, DateTime from)
        {
            Run(new MoveQuestionPlugin(null, null), svc,
                "QuestionId", QuestionId.ToString("D"), "TargetSectionId", ToSection.ToString("D"),
                "NewQuestionCode", "Q-E2-09", "Reason", "Belongs elsewhere", "EffectiveFrom", Day(from));
        }

        [Fact]
        public void A_move_dated_before_a_scheduled_version_is_refused()
        {
            var svc = Checklist();
            svc.Seed("al_section", ToSection, "al_name", "To");
            Version(svc, 1, Today.AddDays(-30), Today.AddDays(15));
            Version(svc, 2, Today.AddDays(15), null);

            Assert.Contains("already scheduled", Refusal(() => Move(svc, Today)));
            Assert.Single(svc.All("al_question"));
        }

        // Finding 2: the move is checked against the day it takes effect, not today.
        [Fact]
        public void A_move_dated_after_a_planned_retirement_is_refused()
        {
            var svc = Checklist();
            svc.Seed("al_section", ToSection, "al_name", "To");
            var v1 = Version(svc, 1, Today.AddDays(-30), Today.AddDays(15));

            var message = Refusal(() => Move(svc, Today.AddDays(25)));

            Assert.Contains(Day(Today.AddDays(15)), message);
            Assert.Equal(Today.AddDays(15), EndOf(svc, v1));
            Assert.Single(svc.All("al_question"));
        }

        [Fact]
        public void A_move_into_a_section_that_retires_before_the_move_starts_is_refused()
        {
            var svc = Checklist();
            svc.Seed("al_section", ToSection, "al_name", "To", "al_effectiveto", Today.AddDays(10));
            Version(svc, 1, Today.AddDays(-30), null);

            Assert.Contains("target section", Refusal(() => Move(svc, Today.AddDays(20))));
            Assert.Single(svc.All("al_question"));
        }

        [Fact]
        public void A_move_into_a_section_starting_on_the_move_day_is_allowed()
        {
            var svc = Checklist();
            svc.Seed("al_section", ToSection, "al_name", "To", "al_effectivefrom", Today.AddDays(10));
            var v1 = Version(svc, 1, Today.AddDays(-30), null);

            Move(svc, Today.AddDays(10));

            Assert.Equal(Today.AddDays(10), EndOf(svc, v1));
            Assert.Equal(2, svc.All("al_question").Count());
        }

        private static void Add(FakeOrganizationService svc, DateTime from)
        {
            Run(new AddQuestionPlugin(null, null), svc,
                "SectionId", ToSection.ToString("D"), "QuestionCode", "Q-E2-10", "Name", "New",
                "Wording", "New wording", "ResponseType", "120910000", "DisplayOrder", "5",
                "EffectiveFrom", Day(from));
        }

        [Fact]
        public void A_question_added_from_the_day_its_new_section_starts_is_allowed()
        {
            var svc = Checklist();
            svc.Seed("al_section", ToSection, "al_name", "To", "al_effectivefrom", Today.AddDays(10));

            Add(svc, Today.AddDays(10));

            Assert.Equal(2, svc.All("al_question").Count());
        }

        [Fact]
        public void A_question_added_from_after_its_section_retires_is_refused()
        {
            var svc = Checklist();
            svc.Seed("al_section", ToSection, "al_name", "To", "al_effectiveto", Today.AddDays(10));

            Assert.Contains("no longer part of the checklist", Refusal(() => Add(svc, Today.AddDays(20))));
            Assert.Single(svc.All("al_question"));
        }

        [Fact]
        public void A_retired_refusal_is_read_against_the_move_day()
        {
            var today = new DateTime(2026, 9, 13);

            Assert.Null(MoveQuestionPlugin.RetiredRefusal(new DateTime(2026, 10, 1), today, new DateTime(2026, 9, 20)));
            Assert.NotNull(MoveQuestionPlugin.RetiredRefusal(new DateTime(2026, 10, 1), today, new DateTime(2026, 10, 1)));
            Assert.NotNull(MoveQuestionPlugin.RetiredRefusal(new DateTime(2026, 10, 1), today, new DateTime(2026, 10, 9)));
        }
    }
}
