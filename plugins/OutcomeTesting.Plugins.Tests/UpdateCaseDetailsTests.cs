using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The per-case adviser and paraplanner codes are no longer editable (project owner,
    /// 2026-09-22). A code is a property of a PERSON and lives on contact.al_staffcode;
    /// leaving a second, hand-typed copy on the case would give two answers that could
    /// disagree, and the export reads only the registry.
    ///
    /// The columns stay in the database holding whatever was typed into them historically.
    /// Nothing reads them any more.
    /// </summary>
    public class UpdateCaseDetailsTests
    {
        private static readonly Guid CaseId = Guid.Parse("11111111-1111-4111-8111-111111111111");

        private static InvalidPluginExecutionException Rejects(string attr)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { attr, "X123" },
            };

            return Assert.Throws<InvalidPluginExecutionException>(
                () => UpdateCaseDetailsPlugin.ApplyFields(
                    new FakeOrganizationService(),
                    fields,
                    new Entity("al_outcomecase", CaseId),
                    new Entity("al_outcomecase", CaseId),
                    new List<string>(),
                    null));
        }

        /// <summary>
        /// Asserted through the public gate (ApplyFields) rather than by reading Editables
        /// directly, because Editables is private and it is the refusal that matters - same
        /// pattern as CheckDateStampTests.No_command_offers_the_check_date_as_an_editable_field.
        /// </summary>
        [Fact]
        public void The_case_code_columns_are_not_editable()
        {
            Assert.Contains("al_advisercode", Rejects("al_advisercode").Message);
            Assert.Contains("al_paraplannercode", Rejects("al_paraplannercode").Message);
        }
    }
}
