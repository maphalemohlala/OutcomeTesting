using System;
using System.Collections.Generic;
using System.Linq;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// BR-002 validation, now that it runs server-side (AD-003). Every case here was a
    /// client-only rule before: a caller posting straight to the Web API met none of them,
    /// so an extract could put an unreadable advice date or an unknown option value into
    /// al_outcomecase with nothing to say it had happened.
    /// </summary>
    public class ImportRulesTests
    {
        private const string Header =
            "IO reference,Client name,Adviser name,Advice date,Case type,Vulnerable client";

        private static string File(params string[] rows)
        {
            return Header + "\r\n" + string.Join("\r\n", rows) + "\r\n";
        }

        [Fact]
        public void Imports_a_row_carrying_only_the_reference()
        {
            // BR-001 makes IO reference the import key and nothing else mandatory. Requiring
            // more would reject extracts the business considers complete.
            var result = ImportRules.ParseCsv(File("IO-1,,,,,"));

            Assert.Null(result.Fatal);
            var row = Assert.Single(result.Valid);
            Assert.Equal("IO-1", row.Reference);
            Assert.Equal("IO-1", row.Values["al_casereference"]);
            Assert.Equal("IO-1", row.Values["al_name"]);
        }

        [Fact]
        public void Rejects_a_row_with_no_reference()
        {
            var result = ImportRules.ParseCsv(File(",A. Client,,,,"));

            Assert.Empty(result.Valid);
            var bad = Assert.Single(result.Invalid);
            Assert.Null(bad.Reference);
            Assert.Contains("Missing IO reference", bad.Reason);
        }

        [Fact]
        public void Rejects_the_second_of_two_rows_naming_the_same_reference()
        {
            // Both would otherwise be created, and BR-001's one-case-per-reference rule
            // would be broken by a single file rather than by a re-upload.
            var result = ImportRules.ParseCsv(File("IO-1,First,,,,", "IO-1,Second,,,,"));

            Assert.Single(result.Valid);
            Assert.Equal("First", result.Valid[0].Values["al_clientname"]);
            var bad = Assert.Single(result.Invalid);
            Assert.Equal(3, bad.RowNumber);
            Assert.Contains("Duplicate IO reference", bad.Reason);
        }

        [Fact]
        public void Treats_a_repeated_reference_as_duplicate_regardless_of_case()
        {
            var result = ImportRules.ParseCsv(File("IO-1,,,,,", "io-1,,,,,"));

            Assert.Single(result.Valid);
            Assert.Single(result.Invalid);
        }

        [Fact]
        public void Rejects_a_row_whose_choice_value_is_not_an_option()
        {
            var result = ImportRules.ParseCsv(File("IO-1,,,,Not a case type,"));

            Assert.Empty(result.Valid);
            var bad = Assert.Single(result.Invalid);
            Assert.Contains("Case type", bad.Reason);
            Assert.Contains("Not a case type", bad.Reason);
        }

        [Fact]
        public void Accepts_a_choice_label_in_any_case()
        {
            var result = ImportRules.ParseCsv(File("IO-1,,,,NEW ADVICE,"));

            Assert.Equal(120910510, result.Valid[0].Values["al_casetype"]);
        }

        [Fact]
        public void Accepts_a_raw_option_value_so_coded_extracts_import()
        {
            var result = ImportRules.ParseCsv(File("IO-1,,,,120910511,"));

            Assert.Equal(120910511, result.Valid[0].Values["al_casetype"]);
        }

        [Fact]
        public void Rejects_a_number_that_is_not_one_of_the_options()
        {
            // Accepting codes must not become accepting any integer: 999 is not a case type,
            // and writing it would put a value in the column no form can render.
            var result = ImportRules.ParseCsv(File("IO-1,,,,999,"));

            Assert.Empty(result.Valid);
            Assert.Single(result.Invalid);
        }

        [Fact]
        public void Reads_a_uk_date_as_day_first()
        {
            // 03/09/2026 is 3 September in an Intelligent Office extract. Read month-first
            // it becomes 9 March, and nothing downstream would ever flag it.
            var result = ImportRules.ParseCsv(File("IO-1,,,03/09/2026,,"));

            Assert.Equal(new DateTime(2026, 9, 3), result.Valid[0].Values["al_advicedate"]);
        }

        [Fact]
        public void Reads_an_iso_date()
        {
            var result = ImportRules.ParseCsv(File("IO-1,,,2026-01-31,,"));

            Assert.Equal(new DateTime(2026, 1, 31), result.Valid[0].Values["al_advicedate"]);
        }

        [Fact]
        public void Rejects_an_unreadable_date()
        {
            var result = ImportRules.ParseCsv(File("IO-1,,,not a date,,"));

            Assert.Empty(result.Valid);
            Assert.Contains("Advice date", Assert.Single(result.Invalid).Reason);
        }

        [Fact]
        public void Rejects_a_uk_date_whose_day_or_month_is_out_of_range()
        {
            var result = ImportRules.ParseCsv(File("IO-1,,,32/01/2026,,", "IO-2,,,01/13/2026,,"));

            Assert.Empty(result.Valid);
            Assert.Equal(2, result.Invalid.Count);
        }

        [Fact]
        public void Rejects_a_file_with_no_reference_column()
        {
            var result = ImportRules.ParseCsv("Client name,Adviser name\r\nA. Client,J. Adviser\r\n");

            Assert.NotNull(result.Fatal);
            Assert.Contains("IO reference", result.Fatal);
            Assert.Empty(result.Valid);
            Assert.Empty(result.Invalid);
        }

        [Fact]
        public void Rejects_an_empty_file()
        {
            Assert.Equal("The file is empty.", ImportRules.ParseCsv(string.Empty).Fatal);
        }

        [Fact]
        public void Skips_the_template_guide_row()
        {
            // The downloadable template ships a guide row of example values. Importing it
            // would create a case called IO-000123 on every first use of the template.
            var result = ImportRules.ParseCsv(File("IO-000123,A. Client,,,,", "IO-1,,,,,"));

            Assert.Single(result.Valid);
            Assert.Equal("IO-1", result.Valid[0].Reference);
            Assert.Empty(result.Invalid);
        }

        [Fact]
        public void Ignores_blank_lines_between_rows()
        {
            var result = ImportRules.ParseCsv(File("IO-1,,,,,", ",,,,,", "IO-2,,,,,"));

            Assert.Equal(2, result.Valid.Count);
            Assert.Empty(result.Invalid);
            Assert.Equal(4, result.Valid[1].RowNumber);
        }

        [Fact]
        public void Numbers_rows_as_the_spreadsheet_does()
        {
            // The row number is what a user uses to find the row in their own file, so it
            // counts the header, is 1-based, and — the part that is easy to get wrong —
            // does not shift when a blank line is skipped.
            var result = ImportRules.ParseCsv(File("IO-1,,,,,", ",,,,,", ",A. Client,,,,"));

            Assert.Equal(2, result.Valid[0].RowNumber);
            Assert.Equal(4, Assert.Single(result.Invalid).RowNumber);
        }

        [Fact]
        public void Keeps_a_quoted_comma_inside_one_cell()
        {
            var result = ImportRules.ParseCsv(File("IO-1,\"Client, A.\",,,,"));

            Assert.Equal("Client, A.", result.Valid[0].Values["al_clientname"]);
        }

        [Fact]
        public void Reads_a_doubled_quote_as_one_quote()
        {
            var result = ImportRules.ParseCsv(File("IO-1,\"A \"\"nickname\"\" client\",,,,"));

            Assert.Equal("A \"nickname\" client", result.Valid[0].Values["al_clientname"]);
        }

        [Fact]
        public void Matches_headers_regardless_of_case_or_surrounding_space()
        {
            var result = ImportRules.ParseCsv(" io REFERENCE , Client Name \r\nIO-1,A. Client\r\n");

            Assert.Null(result.Fatal);
            Assert.Equal("A. Client", result.Valid[0].Values["al_clientname"]);
        }

        [Fact]
        public void Tolerates_a_row_shorter_than_the_header()
        {
            // Spreadsheet exports routinely drop trailing empty cells. Treating that as a
            // parse failure would reject a file that is entirely valid.
            var result = ImportRules.ParseCsv(File("IO-1,A. Client"));

            Assert.Equal("A. Client", result.Valid[0].Values["al_clientname"]);
            Assert.False(result.Valid[0].Values.ContainsKey("al_casetype"));
        }

        [Fact]
        public void Leaves_a_column_the_file_does_not_carry_unset()
        {
            var result = ImportRules.ParseCsv("IO reference\r\nIO-1\r\n");

            Assert.False(result.Valid[0].Values.ContainsKey("al_clientname"));
        }

        [Fact]
        public void Records_the_original_row_so_a_user_can_correct_it()
        {
            var result = ImportRules.ParseCsv(File("IO-1,,,bad date,,"));

            Assert.Contains("IO-1", Assert.Single(result.Invalid).Raw);
            Assert.Contains("bad date", result.Invalid[0].Raw);
        }

        [Fact]
        public void Caps_the_original_row_at_the_column_length()
        {
            var longCell = new string('x', ImportRules.RawDataLimit + 500);
            var result = ImportRules.ParseCsv(File("IO-1,," + longCell + ",bad date,,"));

            Assert.True(Assert.Single(result.Invalid).Raw.Length <= ImportRules.RawDataLimit);
        }

        [Fact]
        public void Counts_every_row_it_saw()
        {
            var result = ImportRules.ParseCsv(File("IO-1,,,,,", "IO-2,,,,bad,", ",A. Client,,,,"));

            Assert.Equal(3, result.Total);
        }

        [Fact]
        public void Every_column_the_template_offers_maps_to_a_distinct_dataverse_column()
        {
            // A copy-paste slip in the column table would write two headers to one column,
            // silently dropping a value the extract carried.
            var attributes = ImportRules.Columns.Select(c => c.Attribute).ToList();

            Assert.Equal(attributes.Count, new HashSet<string>(attributes).Count);
        }

        [Fact]
        public void Every_choice_column_carries_its_options()
        {
            foreach (var column in ImportRules.Columns)
            {
                if (column.Kind == ImportRules.ColumnKind.Choice)
                {
                    Assert.NotNull(column.Choices);
                    Assert.NotEmpty(column.Choices);
                }
            }
        }

        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("has,comma", "\"has,comma\"")]
        [InlineData("has\"quote", "\"has\"\"quote\"")]
        public void Quotes_a_csv_cell_only_when_it_has_to(string value, string expected)
        {
            Assert.Equal(expected, ImportRules.CsvCell(value));
        }

        [Fact]
        public void Escapes_a_json_string_so_a_report_row_stays_parseable()
        {
            // The rejection report crosses the wire as JSON, and a rejected row is exactly
            // the row most likely to contain a stray quote or newline.
            Assert.Equal("a\\\"b\\\\c\\nd", ImportRules.JsonEscape("a\"b\\c\nd"));
        }

        [Fact]
        public void Escapes_a_control_character_as_a_unicode_escape()
        {
            Assert.Equal("a\\u0001b", ImportRules.JsonEscape("a" + (char)1 + "b"));
        }
    }
}
