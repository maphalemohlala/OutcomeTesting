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
    }
}
