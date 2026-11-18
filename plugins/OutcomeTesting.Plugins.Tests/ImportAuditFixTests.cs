using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>Import gaps the 2026-11-17 audit proved by probe, each with the corrected rule.</summary>
    public class ImportAuditFixTests
    {
        private const string Header =
            "TaskID,Client,AdviserName,AdviserEmail,AssignedBy,ParaplannerEmail,ChecklistItem1,CompletedBy1,CompletionDate1\n";

        private static string Row(string taskId, string client = "C", string stamp = "18/08/2026 13:54")
        {
            return taskId + "," + client + ",A,a@x.com,P,p@x.com,Leaver,C," + stamp + "\n";
        }

        [Fact]
        public void A_value_longer_than_its_column_refuses_that_row_naming_the_column()
        {
            var parsed = ImportRules.ParseCsv(Header + Row("T1", client: new string('x', 101)) + Row("T2"));

            var error = Assert.Single(parsed.Invalid);
            Assert.Equal("\"Client\" is longer than 100 characters.", error.Reason);
            Assert.Equal("T2", Assert.Single(parsed.Valid).Reference);
        }

        [Fact]
        public void A_task_id_longer_than_its_column_refuses_that_row()
        {
            var error = Assert.Single(ImportRules.ParseCsv(Header + Row(new string('9', 101))).Invalid);

            Assert.Equal("\"TaskID\" is longer than 100 characters.", error.Reason);
        }

        [Fact]
        public void A_value_exactly_at_the_limit_imports()
        {
            Assert.Single(ImportRules.ParseCsv(Header + Row("T1", client: new string('x', 100))).Valid);
        }

        [Theory]
        [InlineData("08/18/2026 13:54")]
        [InlineData("13:54")]
        public void A_checklist_stamp_that_cannot_be_read_refuses_the_row(string stamp)
        {
            var error = Assert.Single(ImportRules.ParseCsv(Header + Row("T1", stamp: stamp)).Invalid);

            Assert.Equal("\"CompletionDate1\" is not a valid date: \"" + stamp + "\".", error.Reason);
        }

        [Theory]
        [InlineData("18/08/2026 1:54 PM", 13)]
        [InlineData("18/08/2026 1:54:10 AM", 1)]
        [InlineData("18/08/2026 01:54 pm", 13)]
        public void A_uk_date_with_a_twelve_hour_time_reads_as_its_uk_date_and_time(string value, int hour)
        {
            Assert.Equal(new DateTime(2026, 8, 18), ImportRules.ParseDate(value));
            Assert.Equal(hour, ImportRules.ParseDateTime(value).Value.Hour);
        }

        [Fact]
        public void A_checklist_completed_by_longer_than_its_column_refuses_that_row_naming_the_cell()
        {
            var parsed = ImportRules.ParseCsv(Header
                + "T1,C,A,a@x.com,P,p@x.com,Leaver," + new string('x', 101) + ",18/08/2026 13:54\n" + Row("T2"));

            Assert.Equal("\"CompletedBy1\" is longer than 100 characters.", Assert.Single(parsed.Invalid).Reason);
            Assert.Equal("T2", Assert.Single(parsed.Valid).Reference);
        }

        [Fact]
        public void Every_length_limit_matches_the_column_it_is_written_to()
        {
            var entity = XDocument.Load(Path.Combine(
                Path.GetDirectoryName(ThisFile()), "..", "..", "src", "Entities", "al_OutcomeCase", "Entity.xml"));
            var textColumns = ImportRules.Columns
                .Where(c => c.Kind == ImportRules.ColumnKind.Text)
                .Select(c => c.Attribute)
                .Concat(new[] { "al_checklistcompletedby" })
                .Distinct()
                .ToArray();

            foreach (var attribute in textColumns)
            {
                var max = entity.Descendants("attribute")
                    .Single(a => (string)a.Element("LogicalName") == attribute)
                    .Element("MaxLength").Value;
                Assert.True(int.Parse(max) == ImportRules.MaxTextLength(attribute), attribute + ": schema " + max);
            }
        }

        private static string ThisFile([CallerFilePath] string path = null)
        {
            return path;
        }
    }
}
