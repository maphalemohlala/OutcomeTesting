using System;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The CSV and date primitives under ImportRules, which the IO task extract rebuild
    /// (9f4e968) left implemented but untested: the old ImportRulesTests exercised them
    /// through the previous column set and were rewritten wholesale. Tested here on the
    /// primitives themselves, so the extract's column set can change again without
    /// taking the parser's own guarantees with it.
    /// </summary>
    public class ImportParserTests
    {
        [Fact]
        public void Keeps_a_quoted_comma_inside_one_cell()
        {
            var rows = ImportRules.Tokenise("a,\"Client, A.\",c\r\n");

            Assert.Equal("Client, A.", rows[0][1]);
            Assert.Equal(3, rows[0].Count);
        }

        [Fact]
        public void Reads_a_doubled_quote_as_one_quote()
        {
            var rows = ImportRules.Tokenise("\"A \"\"nickname\"\" client\"\r\n");

            Assert.Equal("A \"nickname\" client", rows[0][0]);
        }

        [Fact]
        public void Reads_a_uk_date_as_day_first()
        {
            // 03/09/2026 is 3 September in an Intelligent Office extract. Read month-first
            // it becomes 9 March, and nothing downstream would ever flag it.
            Assert.Equal(new DateTime(2026, 9, 3), ImportRules.ParseDate("03/09/2026"));
        }

        [Fact]
        public void Reads_an_iso_date()
        {
            Assert.Equal(new DateTime(2026, 1, 31), ImportRules.ParseDate("2026-01-31"));
        }

        [Fact]
        public void Rejects_an_unreadable_date()
        {
            Assert.Null(ImportRules.ParseDate("not a date"));
        }

        [Fact]
        public void Rejects_a_month_first_or_out_of_range_date_rather_than_guessing()
        {
            Assert.Null(ImportRules.ParseDate("01/13/2026"));
            Assert.Null(ImportRules.ParseDate("32/01/2026"));
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
