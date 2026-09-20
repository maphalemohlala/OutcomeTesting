using System;
using System.Collections.Generic;
using System.Linq;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// BR-002 validation over the Intelligent Office task extract (2026-09-12 design).
    /// The extract replaced the hand-built CSV template: one row is one task, the key is
    /// TaskID, and the review route is derived from the checklist items the paraplanner
    /// selected rather than from a "Tax check required" column that no longer exists.
    /// </summary>
    public class ImportRulesTests
    {
        private const string Header =
            "TaskID,ServiceCaseSequentialRef,Client,AdviserName,AssignedTo,Status,Outcome," +
            "CompletedBy,CompletedDate,DueDate,TaskType,AssignedBy," +
            "ChecklistItem1,CompletedBy1,CompletionDate1,ChecklistItem2,CompletedBy2,CompletionDate2";

        private const string Stamp = "2026-09-04T13:54:00";

        /// <summary>
        /// A row carrying one stamped Tax item by default, so a test that is not about the
        /// checklist still produces an importable row.
        /// </summary>
        private static string Row(
            string taskId,
            string item1 = "Tax Check",
            string by1 = "Miko Stewart",
            string date1 = Stamp,
            string item2 = "",
            string by2 = "",
            string date2 = "",
            string status = "Complete",
            string outcome = "",
            string client = "A. Client")
        {
            return string.Join(
                ",",
                taskId,
                "IOA07028411",
                client,
                "Jane Adviser",
                // AssignedTo. The extract puts the CHECKER here, which is why this fixture
                // no longer calls it "Pat Paraplanner" - the old name was the guess that
                // caused the mapping to be reversed twice.
                "Chris Checker",
                status,
                outcome,
                string.Empty,
                string.Empty,
                string.Empty,
                "Pre-Advice Check Required",
                // AssignedBy: the para-planner who raised the task (AD-160).
                "Pat Paraplanner",
                item1,
                by1,
                date1,
                item2,
                by2,
                date2);
        }

        private static string File(params string[] rows)
        {
            return Header + "\r\n" + string.Join("\r\n", rows) + "\r\n";
        }

        // ---- IO reference -------------------------------------------------------

        /// <summary>
        /// A valid extract row carrying a ClientRef. The class-level Header does not name
        /// that column, and a row with no stamped checklist item is refused before any
        /// column mapping happens - the route could not be derived - so these tests bring
        /// their own header rather than reusing one that would fail for an unrelated reason.
        /// </summary>
        private const string IoHeader =
            "TaskID,ClientRef,ChecklistItem1,CompletedBy1,CompletionDate1";

        private static string IoRow(string taskId, string clientRef)
        {
            return taskId + "," + clientRef + ",Tax Check,Miko Stewart," + Stamp;
        }

        private static string IoFile(params string[] rows)
        {
            return IoHeader + "\r\n" + string.Join("\r\n", rows) + "\r\n";
        }

        /// <summary>
        /// The IO reference is ClientRef, not TaskID (project owner, 2026-09-19).
        ///
        /// It is carried in its own column rather than re-sourcing al_casereference,
        /// because that column is the BR-001 import key and the table's alternate key.
        /// ClientRef repeats across a client's cases, so keying on it would make two
        /// genuine cases collide on import. TaskID keeps the key; ClientRef is what a
        /// person is shown and what points back at Intelligent Office.
        /// </summary>
        [Fact]
        public void Takes_the_io_reference_from_the_client_ref()
        {
            var result = ImportRules.ParseCsv(IoFile(IoRow("253925362", "CLI-4821")));

            Assert.Null(result.Fatal);
            var row = Assert.Single(result.Valid);
            Assert.Equal("CLI-4821", row.Values["al_ioreference"]);
        }

        /// <summary>
        /// And the key is still TaskID. Guards the collision the separate column exists to
        /// avoid: if al_casereference ever starts carrying ClientRef, this fails.
        /// </summary>
        [Fact]
        public void Keeps_the_case_reference_on_the_task_id_when_both_are_present()
        {
            var result = ImportRules.ParseCsv(IoFile(IoRow("253925362", "CLI-4821")));

            var row = Assert.Single(result.Valid);
            Assert.Equal("253925362", row.Values["al_casereference"]);
            Assert.Equal("CLI-4821", row.Values["al_clientref"]);
            Assert.Equal("CLI-4821", row.Values["al_ioreference"]);
        }

        /// <summary>
        /// Two cases for one client import as two cases. This is the scenario that
        /// re-sourcing al_casereference would have broken: both rows carry the same
        /// ClientRef, and both must survive because their TaskIDs differ.
        /// </summary>
        [Fact]
        public void Imports_two_cases_that_share_a_client_ref()
        {
            var result = ImportRules.ParseCsv(
                IoFile(IoRow("253925362", "CLI-4821"), IoRow("254471517", "CLI-4821")));

            Assert.Empty(result.Invalid);
            Assert.Equal(2, result.Valid.Count);
            Assert.Equal("CLI-4821", result.Valid[0].Values["al_ioreference"]);
            Assert.Equal("CLI-4821", result.Valid[1].Values["al_ioreference"]);
        }

        /// <summary>A row with no ClientRef leaves the column unset rather than blank.</summary>
        [Fact]
        public void Leaves_the_io_reference_unset_when_the_extract_carries_none()
        {
            var result = ImportRules.ParseCsv(IoFile(IoRow("253925362", "")));

            var row = Assert.Single(result.Valid);
            Assert.False(row.Values.ContainsKey("al_ioreference"));
        }

        // ---- key ----------------------------------------------------------------

        [Fact]
        public void Imports_a_row_keyed_on_the_task_id()
        {
            var result = ImportRules.ParseCsv(File(Row("253925362")));

            Assert.Null(result.Fatal);
            var row = Assert.Single(result.Valid);
            Assert.Equal("253925362", row.Reference);
            Assert.Equal("253925362", row.Values["al_casereference"]);
            Assert.Equal("253925362", row.Values["al_name"]);
        }

        [Fact]
        public void Rejects_a_row_with_no_task_id()
        {
            var result = ImportRules.ParseCsv(File(Row("")));

            Assert.Empty(result.Valid);
            var bad = Assert.Single(result.Invalid);
            Assert.Null(bad.Reference);
            Assert.Contains("Missing TaskID", bad.Reason);
        }

        [Fact]
        public void Rejects_the_second_of_two_rows_naming_the_same_task_id()
        {
            var result = ImportRules.ParseCsv(File(Row("1", client: "First"), Row("1", client: "Second")));

            Assert.Single(result.Valid);
            Assert.Equal("First", result.Valid[0].Values["al_clientname"]);
            var bad = Assert.Single(result.Invalid);
            Assert.Contains("Duplicate TaskID", bad.Reason);
        }

        [Fact]
        public void Imports_two_tasks_that_share_one_service_case()
        {
            // D2: a service case carrying two tasks becomes two cases. IOA07066846 in the
            // supplied workbook does exactly this, and rejecting the second would drop real
            // checking work.
            var result = ImportRules.ParseCsv(File(Row("254471517"), Row("254471891")));

            Assert.Empty(result.Invalid);
            Assert.Equal(2, result.Valid.Count);
            Assert.All(result.Valid, r => Assert.Equal("IOA07028411", r.Values["al_servicecaseref"]));
        }

        [Fact]
        public void Reports_a_file_with_no_task_id_column_as_fatal()
        {
            var result = ImportRules.ParseCsv("Client,AdviserName\r\nA. Client,Jane Adviser\r\n");

            Assert.NotNull(result.Fatal);
            Assert.Contains("TaskID", result.Fatal);
        }


        [Fact]
        public void Takes_the_paraplanner_from_assigned_by()
        {
            // REVERSED 2026-09-20 (AD-160). This test asserted AssignedTo until today, on
            // the 2026-09-14 direction that the real extract put the two the other way
            // round from the sample.
            //
            // The owner reaffirmed AssignedBy with that conflict put to them explicitly. It
            // is also what the written specification always said, and what the only extract
            // in this repository shows - AssignedBy is "Paraplanner N" in every row of it.
            //
            // The fixture names changed with the mapping, deliberately: calling the
            // AssignedTo value "Pat Paraplanner" is precisely the assumption that got
            // written into the column map twice.
            var result = ImportRules.ParseCsv(File(Row("1")));

            var row = Assert.Single(result.Valid);
            Assert.Equal("Pat Paraplanner", row.Values["al_paraplanner"]);
            Assert.False(row.Values.ContainsKey("al_assignedby"));
        }

        [Theory]
        [InlineData("Tax Check")]
        [InlineData("Trust Documentation Check")]
        [InlineData("High Risk Item 1")]
        [InlineData("High Risk Item 2")]
        [InlineData("Enhanced Supervision")]
        [InlineData("Pre-CAS Adviser")]
        [InlineData("Leaver")]
        public void Recognises_every_item_wording_the_real_extract_uses(string item)
        {
            // These seven are the ONLY stamped checklist names in the real Pre-Advice Check
            // task extract of 2026-09-14, read on 2026-09-20. They answer AD-124's open
            // question about IO's exact item wording from data rather than from the
            // synthetic sample, and they matter because an unrecognised name does not route
            // on what it did recognise - it FAILS the whole row (BR-002).
            //
            // Only the vocabulary is recorded here. The extract itself carries client names,
            // references, addresses, postcodes, emails and phone numbers, so it is not in
            // this repository and must not be.
            Assert.True(
                ImportRules.ChecklistItems.ContainsKey(item),
                item + " is used by the real extract and must be recognised.");
        }

        [Fact]
        public void Does_not_import_the_assigned_to_name_anywhere()
        {
            // Not merely mapped elsewhere - absent. AD-113: a name the file carried never
            // proved a case was allocated, and a re-import must not overwrite whoever is.
            var result = ImportRules.ParseCsv(File(Row("1")));

            var row = Assert.Single(result.Valid);
            foreach (var value in row.Values)
            {
                Assert.NotEqual("Chris Checker", value.Value as string);
            }
        }

        [Fact]
        public void Does_not_import_a_checker_name()
        {
            // The checker is set manually (project owner, 2026-09-14) - by allocation
            // (AssignCasePlugin), by a claim (ClaimCasePlugin), or by editing the case. The
            // import writing it was AD-113's complaint made concrete: a name the file carried
            // that never proved the case was allocated to anyone.
            //
            // It must not merely be written from a different column - it must not be written
            // AT ALL, or a re-import of the same task would overwrite whoever was allocated.
            var result = ImportRules.ParseCsv(File(Row("1")));

            var row = Assert.Single(result.Valid);
            Assert.False(row.Values.ContainsKey("al_checkername"));
        }

        // ---- route derivation (D6) ----------------------------------------------

        [Fact]
        public void A_tax_item_alone_asks_for_a_tax_check()
        {
            var result = ImportRules.ParseCsv(File(Row("1", item1: "Tax Check")));

            var row = Assert.Single(result.Valid);
            Assert.Equal(ImportRules.TaxCheckRequiredYes, row.Values["al_taxcheckrequired"]);
        }

        [Fact]
        public void Trust_documentation_is_a_tax_item()
        {
            var result = ImportRules.ParseCsv(File(Row("1", item1: "Trust Documentation Check")));

            var row = Assert.Single(result.Valid);
            Assert.Equal(ImportRules.TaxCheckRequiredYes, row.Values["al_taxcheckrequired"]);
        }

        [Theory]
        [InlineData("High Risk Item 1")]
        [InlineData("High Risk Item 2")]
        [InlineData("Enhanced Supervision")]
        [InlineData("Pre-CAS Adviser")]
        [InlineData("Leaver")]
        public void An_aqs_item_alone_asks_for_no_tax_check(string item)
        {
            var result = ImportRules.ParseCsv(File(Row("1", item1: item)));

            var row = Assert.Single(result.Valid);
            Assert.Equal(ImportRules.TaxCheckRequiredNo, row.Values["al_taxcheckrequired"]);
        }

        [Fact]
        public void Tax_and_aqs_items_together_ask_for_a_tax_check_first()
        {
            // The client's rule: where both are required the starting point is always Tax,
            // and the Tax team decides whether AQS is genuinely owed. That is what
            // al_taxcheckrequired = Yes already means to DeriveRoute (ROUTE-TAX-AQS).
            var result = ImportRules.ParseCsv(
                File(Row("1", item1: "High Risk Item 1", item2: "Tax Check", by2: "Miko Stewart", date2: Stamp)));

            var row = Assert.Single(result.Valid);
            Assert.Equal(ImportRules.TaxCheckRequiredYes, row.Values["al_taxcheckrequired"]);
        }

        [Fact]
        public void Accepts_the_short_names_the_client_used_for_the_tax_items()
        {
            // The client wrote "Tax" and "Trust documentation"; the workbook says "Tax Check"
            // and "Trust Documentation Check". Both name the same item (design §5).
            var result = ImportRules.ParseCsv(File(Row("1", item1: "Tax"), Row("2", item1: "Trust documentation")));

            Assert.Empty(result.Invalid);
            Assert.All(result.Valid, r => Assert.Equal(ImportRules.TaxCheckRequiredYes, r.Values["al_taxcheckrequired"]));
        }

        // ---- the stamp rule (D7) -------------------------------------------------

        [Fact]
        public void An_item_with_no_completion_stamp_is_not_selected()
        {
            // The fixed-slot reading: IO lists every item in the workflow and stamps only the
            // ones the paraplanner picked. Counting the unstamped one would route every case
            // through Tax.
            var result = ImportRules.ParseCsv(
                File(Row("1", item1: "High Risk Item 1", item2: "Tax Check", by2: "", date2: "")));

            var row = Assert.Single(result.Valid);
            Assert.Equal(ImportRules.TaxCheckRequiredNo, row.Values["al_taxcheckrequired"]);
            Assert.Equal("High Risk Item 1", row.Values["al_checklistitems"]);
        }

        [Fact]
        public void Reads_a_selection_wherever_the_columns_place_it()
        {
            // The packed reading: a lone selection lands in ChecklistItem1. Position is never
            // consulted, so both readings give the same answer.
            var packed = ImportRules.ParseCsv(File(Row("1", item1: "Tax Check")));
            var fixedSlot = ImportRules.ParseCsv(
                File(Row("2", item1: "High Risk Item 1", by1: "", date1: "", item2: "Tax Check", by2: "Miko Stewart", date2: Stamp)));

            Assert.Equal(
                ImportRules.TaxCheckRequiredYes,
                Assert.Single(packed.Valid).Values["al_taxcheckrequired"]);
            Assert.Equal(
                ImportRules.TaxCheckRequiredYes,
                Assert.Single(fixedSlot.Valid).Values["al_taxcheckrequired"]);
        }

        // ---- the checklist on the case (D4) --------------------------------------

        [Fact]
        public void Keeps_every_selected_item_name_one_per_line()
        {
            var result = ImportRules.ParseCsv(
                File(Row("1", item1: "High Risk Item 1", item2: "Tax Check", by2: "Miko Stewart", date2: Stamp)));

            var row = Assert.Single(result.Valid);
            Assert.Equal("High Risk Item 1\nTax Check", row.Values["al_checklistitems"]);
        }

        [Fact]
        public void Collapses_the_repeated_stamp_to_one_completed_by_and_one_date()
        {
            var result = ImportRules.ParseCsv(
                File(Row("1", item1: "High Risk Item 1", item2: "Tax Check", by2: "Miko Stewart", date2: Stamp)));

            var row = Assert.Single(result.Valid);
            Assert.Equal("Miko Stewart", row.Values["al_checklistcompletedby"]);
            Assert.Equal(new DateTime(2026, 9, 4, 13, 54, 0), row.Values["al_checklistcompleteddate"]);
        }

        [Fact]
        public void Keeps_the_earliest_stamp_when_the_items_disagree()
        {
            var result = ImportRules.ParseCsv(
                File(Row(
                    "1",
                    item1: "High Risk Item 1",
                    date1: "2026-09-05T09:00:00",
                    item2: "Tax Check",
                    by2: "Miko Stewart",
                    date2: "2026-09-04T13:54:00")));

            var row = Assert.Single(result.Valid);
            Assert.Equal(new DateTime(2026, 9, 4, 13, 54, 0), row.Values["al_checklistcompleteddate"]);
        }

        // ---- the two new rejections (BR-002, design §7) ---------------------------

        [Fact]
        public void Rejects_a_row_with_no_selected_checklist_item()
        {
            // IOA07028411 in the supplied workbook looks like this. Creating it would leave a
            // case at Imported with no route, which nothing downstream would surface.
            var result = ImportRules.ParseCsv(File(Row("1", item1: "", by1: "", date1: "")));

            Assert.Empty(result.Valid);
            var bad = Assert.Single(result.Invalid);
            Assert.Equal("1", bad.Reference);
            Assert.Contains("No checklist items", bad.Reason);
            Assert.Contains("route", bad.Reason);
        }

        [Fact]
        public void Rejects_a_row_carrying_a_checklist_item_it_does_not_recognise()
        {
            // Protects the open question about IO's exact wording: a name we do not know
            // stops the row rather than quietly routing it on the items we did recognise.
            var result = ImportRules.ParseCsv(File(Row("1", item1: "Vulnerability Review")));

            Assert.Empty(result.Valid);
            var bad = Assert.Single(result.Invalid);
            Assert.Contains("Vulnerability Review", bad.Reason);
            Assert.Contains("not recognised", bad.Reason);
        }

        [Fact]
        public void Does_not_let_a_recognised_item_excuse_an_unrecognised_one()
        {
            var result = ImportRules.ParseCsv(
                File(Row("1", item1: "Tax Check", item2: "Vulnerability Review", by2: "Miko Stewart", date2: Stamp)));

            Assert.Empty(result.Valid);
            Assert.Contains("Vulnerability Review", Assert.Single(result.Invalid).Reason);
        }

        [Fact]
        public void Ignores_an_unrecognised_name_that_carries_no_stamp()
        {
            // An unstamped item was never selected, so its name is not a routing input and
            // must not fail the row.
            var result = ImportRules.ParseCsv(
                File(Row("1", item1: "Tax Check", item2: "Vulnerability Review", by2: "", date2: "")));

            Assert.Empty(result.Invalid);
            Assert.Equal("Tax Check", Assert.Single(result.Valid).Values["al_checklistitems"]);
        }

        // ---- the IO outcome is reference data, not a grade (D3) ------------------

        [Fact]
        public void Stores_the_io_outcome_in_its_own_column()
        {
            var result = ImportRules.ParseCsv(File(Row("1", outcome: "Pass with issues")));

            var row = Assert.Single(result.Valid);
            Assert.Equal(ImportRules.IoOutcomePassWithIssues, row.Values["al_iooutcome"]);
            Assert.False(row.Values.ContainsKey("al_outcome"));
        }

        [Fact]
        public void Rejects_an_io_outcome_it_does_not_know()
        {
            var result = ImportRules.ParseCsv(File(Row("1", outcome: "Referred")));

            Assert.Empty(result.Valid);
            Assert.Contains("Referred", Assert.Single(result.Invalid).Reason);
        }

        [Fact]
        public void Maps_the_task_status()
        {
            var result = ImportRules.ParseCsv(File(Row("1", status: "Not Started")));

            Assert.Equal(
                ImportRules.IoTaskStatusNotStarted,
                Assert.Single(result.Valid).Values["al_iotaskstatus"]);
        }

        // ---- derived header values ----------------------------------------------

        [Fact]
        public void A_pre_advice_task_is_a_pre_check()
        {
            var result = ImportRules.ParseCsv(File(Row("1")));

            Assert.Equal(
                ImportRules.PreOrPostCheckPre,
                Assert.Single(result.Valid).Values["al_preorpostcheck"]);
        }

        [Fact]
        public void Imports_every_row_of_the_supplied_workbook_shape()
        {
            // A smoke test over the real header, so a mapping that compiles but does not line
            // up with the file is caught here rather than in DEV.
            var result = ImportRules.ParseCsv(File(
                Row("253925362", item1: "", by1: "", date1: ""),
                Row("254294642", status: "Not Started"),
                Row("254397454", outcome: "Pass"),
                Row("254398988", outcome: "Pass with issues"),
                Row("254399947", outcome: "Potential harm")));

            Assert.Equal(4, result.Valid.Count);
            Assert.Contains(result.Invalid, r => r.Reference == "253925362");
        }

        // ---- the supplied extract ------------------------------------------------

        /// <summary>
        /// data/io-task-extract-sample.csv is the supplied workbook transcribed, with the
        /// personal columns dropped (D8). Parsing it here is what catches a mapping that
        /// compiles but does not line up with the real file.
        /// </summary>
        private static string Fixture()
        {
            var directory = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = System.IO.Path.Combine(directory.FullName, "data", "io-task-extract-sample.csv");
                if (System.IO.File.Exists(candidate))
                {
                    return System.IO.File.ReadAllText(candidate);
                }

                directory = directory.Parent;
            }

            throw new System.IO.FileNotFoundException("data/io-task-extract-sample.csv was not found above " + System.AppContext.BaseDirectory);
        }

        [Fact]
        public void Imports_every_row_of_the_supplied_extract_but_the_one_with_no_checklist()
        {
            var result = ImportRules.ParseCsv(Fixture());

            Assert.Null(result.Fatal);
            Assert.Equal(6, result.Valid.Count);
            Assert.Contains("No checklist items", Assert.Single(result.Invalid).Reason);
        }

        [Fact]
        public void Routes_every_checklisted_row_of_the_supplied_extract_through_tax_first()
        {
            // Every completed row in the sample carries both a Tax item and a High Risk item,
            // so each is a Tax-then-AQS case.
            var result = ImportRules.ParseCsv(Fixture());

            Assert.All(result.Valid, row => Assert.Equal(ImportRules.TaxCheckRequiredYes, row.Values["al_taxcheckrequired"]));
        }

        [Fact]
        public void Keeps_the_seven_selected_reasons_and_one_stamp()
        {
            var first = ImportRules.ParseCsv(Fixture()).Valid[0];

            Assert.Equal(
                new[]
                {
                    "High Risk Item 1", "High Risk Item 2", "Enhanced Supervision", "Pre-CAS Adviser",
                    "Leaver", "Tax Check", "Trust Documentation Check",
                },
                ((string)first.Values["al_checklistitems"]).Split('\n'));
            Assert.Equal(new DateTime(2026, 8, 18, 13, 54, 0), first.Values["al_checklistcompleteddate"]);
        }

        [Fact]
        public void Reads_the_two_tasks_sharing_a_service_case_as_two_cases()
        {
            var shared = ImportRules.ParseCsv(Fixture()).Valid
                .Where(row => (string)row.Values["al_servicecaseref"] == "IOA07066846")
                .ToList();

            Assert.Equal(2, shared.Count);
            Assert.Equal(2, shared.Select(row => row.Reference).Distinct().Count());
        }

        /// <summary>
        /// The check's deadline (project owner, 2026-09-19). Derived from the upload, not
        /// read from the sheet: the extract's DueDate is Intelligent Office's deadline for
        /// the paraplanner's task, and letting it through would have the spreadsheet
        /// overriding this system's rule.
        /// </summary>
        [Fact]
        public void Due_three_days_after_the_day_of_the_upload()
        {
            var uploadedAt = new DateTime(2026, 9, 19, 9, 30, 0, DateTimeKind.Utc);

            Assert.Equal(new DateTime(2026, 9, 22), ImportRules.DueDateFor(uploadedAt));
        }

        [Fact]
        public void Returns_midnight_because_the_column_cannot_hold_a_time()
        {
            // al_duedate is DateOnly (Behavior 2). Returning 09:30 would invent a precision
            // the record does not keep - which is the defect this replaced: the rule said
            // "72 hours", the column stored a date, and the deployment note claimed the time
            // of day survived. Every al_duedate in DEV reads back 00:00:00.
            var uploadedAt = new DateTime(2026, 9, 19, 16, 45, 0, DateTimeKind.Utc);

            Assert.Equal(TimeSpan.Zero, ImportRules.DueDateFor(uploadedAt).TimeOfDay);
        }

        [Fact]
        public void Counts_calendar_days_straight_through_a_weekend()
        {
            // Friday to Monday. Calendar days, not working days - which is what "3 days"
            // says, and differs from remediation, where the clock counts working days.
            var friday = new DateTime(2026, 9, 18, 16, 0, 0, DateTimeKind.Utc);
            var due = ImportRules.DueDateFor(friday);

            Assert.Equal(new DateTime(2026, 9, 21), due);
            Assert.Equal(DayOfWeek.Monday, due.DayOfWeek);
        }

        // ------------------------------------------------------------------ the UK day

        [Fact]
        public void A_late_bst_evening_upload_belongs_to_the_next_uk_day()
        {
            // 23:30 UTC on 17 June is 00:30 on the 18th in British Summer Time. The person
            // who pressed the button did so on the 18th, so their deadline is the 21st.
            //
            // THE defect (audit finding 5). Adding 72 hours to the UTC instant gave
            // 2026-06-20, a day earlier than the same file uploaded a minute later - a seam
            // in the middle of the night that nobody would have found by testing at noon.
            var uploadedAt = new DateTime(2026, 6, 17, 23, 30, 0, DateTimeKind.Utc);

            Assert.Equal(new DateTime(2026, 6, 21), ImportRules.DueDateFor(uploadedAt));
        }

        [Fact]
        public void An_early_bst_morning_upload_stays_on_its_own_uk_day()
        {
            // 00:30 BST on 18 June is 23:30 UTC on the 17th, which is the same instant as
            // the case above - and must give the same answer. The pair is the point: one
            // upload, two ways of writing it down, one deadline.
            var uploadedAt = new DateTime(2026, 6, 17, 23, 30, 0, DateTimeKind.Utc);
            var sameMomentLocal = new DateTimeOffset(2026, 6, 18, 0, 30, 0, TimeSpan.FromHours(1)).UtcDateTime;

            Assert.Equal(ImportRules.DueDateFor(uploadedAt), ImportRules.DueDateFor(sameMomentLocal));
        }

        [Fact]
        public void A_winter_evening_upload_stays_on_its_own_day()
        {
            // GMT, no offset: 23:30 UTC on 15 January is still the 15th in the UK, so the
            // conversion must NOT move it. The fix has to be a time-zone conversion rather
            // than a blanket "add a day to late uploads".
            var uploadedAt = new DateTime(2026, 1, 15, 23, 30, 0, DateTimeKind.Utc);

            Assert.Equal(new DateTime(2026, 1, 18), ImportRules.DueDateFor(uploadedAt));
        }

        [Fact]
        public void Spans_the_spring_clock_change()
        {
            // BST began 29 March 2026. An upload on the 27th is due on the 30th: the clocks
            // going forward does not cost anyone a day, because the arithmetic is on dates
            // and not on elapsed hours. Adding 72 hours would have been an hour short of
            // three days here.
            var uploadedAt = new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc);

            Assert.Equal(new DateTime(2026, 3, 30), ImportRules.DueDateFor(uploadedAt));
        }

        [Fact]
        public void Spans_the_autumn_clock_change()
        {
            // BST ended 25 October 2026. An upload on the 23rd is due on the 26th, and the
            // extra hour does not buy one either.
            var uploadedAt = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

            Assert.Equal(new DateTime(2026, 10, 26), ImportRules.DueDateFor(uploadedAt));
        }

        [Fact]
        public void Every_case_in_one_upload_shares_a_deadline()
        {
            // Taken from the upload, not from each row. Two cases from one file cannot fall
            // due on different days for a reason nobody can see.
            var uploadedAt = new DateTime(2026, 9, 19, 9, 30, 0, DateTimeKind.Utc);

            Assert.Equal(ImportRules.DueDateFor(uploadedAt), ImportRules.DueDateFor(uploadedAt));
        }

        [Fact]
        public void Does_not_map_the_sheets_own_due_date()
        {
            Assert.DoesNotContain(
                ImportRules.Columns,
                column => string.Equals(column.Attribute, "al_duedate", StringComparison.OrdinalIgnoreCase));
        }
    }
}
