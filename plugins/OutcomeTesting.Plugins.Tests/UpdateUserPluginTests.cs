using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// al_UpdateUser's StaffCode handling (Task 6, staff-codes-registry). The brief calls
    /// the absent-vs-empty distinction load-bearing: the People page sends only what it
    /// edited, so a save of somebody's NAME must never silently wipe their code. These pin
    /// the three cases through the real plug-in pipeline - permission gate, idempotency
    /// lookup, the write itself and the audit event - rather than against a copy of the
    /// branch, because a passing copy is exactly what would keep passing if the branch were
    /// ever deleted from ExecuteDataversePlugin.
    ///
    /// Driving <see cref="UpdateUserPlugin.Execute"/> directly is possible here (unlike
    /// AssignUserRolePlugin's guard) because <see cref="PermissionHelpers.EnsureAppPermission"/>
    /// bootstraps open on an empty al_userrolemapping table, and these tests never seed one -
    /// so no permission scaffolding is needed to reach the write.
    /// </summary>
    public class UpdateUserPluginTests
    {
        private static readonly Guid UserId = Guid.Parse("55555555-9999-4999-8999-555555555555");

        private static FakeOrganizationService ContactAt(string staffCode)
        {
            var service = new FakeOrganizationService();
            service.Seed(
                ContactRegistry.Entity,
                UserId,
                ContactRegistry.EmailAttr, "person@example.invalid",
                ContactRegistry.FullNameAttr, "Original Name",
                ContactRegistry.FirstNameAttr, "Original",
                ContactRegistry.LastNameAttr, "Name",
                ContactRegistry.StaffCodeAttr, staffCode);
            return service;
        }

        private static void Run(FakeOrganizationService service, string idempotencyKey, string staffCode)
        {
            var provider = new FakeServiceProvider(service);
            provider.Context.InputParameters["UserId"] = UserId.ToString("D");
            provider.Context.InputParameters["FullName"] = "Updated Name";
            provider.Context.InputParameters["IdempotencyKey"] = idempotencyKey;
            if (staffCode != null)
            {
                provider.Context.InputParameters["StaffCode"] = staffCode;
            }

            new UpdateUserPlugin(null, null).Execute(provider);
        }

        [Fact]
        public void An_absent_staff_code_leaves_the_stored_code_unchanged()
        {
            // The People page sends only what it edited: a name-only save must not carry a
            // StaffCode parameter at all, and the plug-in must not touch the column.
            var service = ContactAt("OLD1");

            Run(service, "key-absent", staffCode: null);

            Assert.Equal("OLD1", service.Row(ContactRegistry.Entity, UserId).GetAttributeValue<string>(ContactRegistry.StaffCodeAttr));

            // Not just "the value happens to match" - the write itself must never have named
            // the column, or a coincidence here would hide a plug-in that clears-then-restores it.
            Assert.False(service.Updates[0].Contains(ContactRegistry.StaffCodeAttr));
        }

        [Fact]
        public void An_explicitly_empty_staff_code_clears_the_stored_code()
        {
            var service = ContactAt("OLD1");

            Run(service, "key-empty", staffCode: string.Empty);

            Assert.Null(service.Row(ContactRegistry.Entity, UserId).GetAttributeValue<string>(ContactRegistry.StaffCodeAttr));
        }

        [Fact]
        public void A_supplied_staff_code_is_set_and_trimmed()
        {
            var service = ContactAt(null);

            Run(service, "key-value", staffCode: "  EMP123  ");

            Assert.Equal("EMP123", service.Row(ContactRegistry.Entity, UserId).GetAttributeValue<string>(ContactRegistry.StaffCodeAttr));
        }

        /// <summary>The al_details this run wrote onto its Audit Event.</summary>
        private static string AuditDetails(FakeOrganizationService service)
        {
            var audit = service.Creates.Find(e =>
                string.Equals(e.LogicalName, "al_auditevent", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(audit);
            return audit.GetAttributeValue<string>("al_details");
        }

        [Fact]
        public void A_changed_employee_code_is_named_in_the_audit_line()
        {
            // Before this, changing ONLY somebody's code produced "Name Jane Adviser ->
            // Jane Adviser" - a line that reads as a no-op, on a permission-gated
            // administrative write that feeds a positional file an external system consumes.
            // AD-041 is the reason this command exists at all.
            var service = ContactAt("OLD1");

            Run(service, "key-code-changed", staffCode: "NEW2");

            var details = AuditDetails(service);
            Assert.Contains("Employee code OLD1 -> NEW2", details);
        }

        [Fact]
        public void Setting_and_clearing_a_code_both_read_as_transitions()
        {
            var setting = ContactAt(null);
            Run(setting, "key-code-set", staffCode: "EMP9");
            Assert.Contains("Employee code (none) -> EMP9", AuditDetails(setting));

            var clearing = ContactAt("EMP9");
            Run(clearing, "key-code-cleared", staffCode: string.Empty);
            Assert.Contains("Employee code EMP9 -> (none)", AuditDetails(clearing));
        }

        [Fact]
        public void A_name_only_save_says_nothing_about_the_code()
        {
            // Two ways a save can leave the code alone, and neither should claim otherwise:
            // the parameter absent, and the parameter resent unchanged.
            var absent = ContactAt("OLD1");
            Run(absent, "key-code-absent-audit", staffCode: null);
            Assert.DoesNotContain("Employee code", AuditDetails(absent));

            var resent = ContactAt("OLD1");
            Run(resent, "key-code-resent", staffCode: "OLD1");
            Assert.DoesNotContain("Employee code", AuditDetails(resent));
        }

        [Fact]
        public void The_name_transition_is_still_there()
        {
            // The clause that existed is appended to, not replaced.
            var service = ContactAt("OLD1");

            Run(service, "key-both", staffCode: "NEW2");

            Assert.Contains("Name Original Name -> Updated Name", AuditDetails(service));
        }
    }
}
