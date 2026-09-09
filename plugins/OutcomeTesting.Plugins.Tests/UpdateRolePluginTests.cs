using System;
using System.Text;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What al_UpdateRole writes onto a web role. The registry moved from al_role to
    /// mspp_webrole under AD-087, and this command kept writing al_description and
    /// al_isactive — columns mspp_webrole does not have — so re-describing or retiring a
    /// role faulted at the platform, while the app reads mspp_description and statecode
    /// (app/src/features/admin/useRoles.ts).
    /// </summary>
    public class UpdateRolePluginTests
    {
        private static readonly Guid RoleId = Guid.Parse("77777777-1111-4111-8111-777777777777");

        private static Entity Role(string name, string description, bool active = true)
        {
            var role = new Entity(WebRoleRegistry.RoleEntity, RoleId)
            {
                [WebRoleRegistry.NameAttr] = name,
                ["statecode"] = new OptionSetValue(active ? 0 : 1),
            };
            if (description != null)
            {
                role[WebRoleRegistry.DescriptionAttr] = description;
            }

            return role;
        }

        [Fact]
        public void A_new_description_is_written_to_the_column_the_app_reads()
        {
            var details = new StringBuilder();

            var update = UpdateRolePlugin.BuildUpdate(Role("AL Portal - Planner", "Old"), null, "New wording", null, details);

            Assert.Equal("New wording", update.GetAttributeValue<string>(WebRoleRegistry.DescriptionAttr));
            Assert.False(update.Contains("al_description"));
            Assert.Contains("Description changed", details.ToString());
        }

        [Fact]
        public void An_empty_description_clears_the_column()
        {
            var update = UpdateRolePlugin.BuildUpdate(Role("AL Portal - Planner", "Old"), null, "", null, new StringBuilder());

            Assert.True(update.Contains(WebRoleRegistry.DescriptionAttr));
            Assert.Null(update[WebRoleRegistry.DescriptionAttr]);
        }

        [Fact]
        public void An_unchanged_description_writes_nothing()
        {
            var update = UpdateRolePlugin.BuildUpdate(Role("AL Portal - Planner", "Same"), null, "Same", null, new StringBuilder());

            Assert.Empty(update.Attributes);
        }

        [Fact]
        public void Retiring_writes_the_state_the_app_reads_back()
        {
            var details = new StringBuilder();

            var update = UpdateRolePlugin.BuildUpdate(Role("AL Portal - Planner", null), null, null, false, details);

            Assert.Equal(1, update.GetAttributeValue<OptionSetValue>("statecode").Value);
            Assert.Equal(2, update.GetAttributeValue<OptionSetValue>("statuscode").Value);
            Assert.False(update.Contains("al_isactive"));
            Assert.Contains("Active True -> False", details.ToString());
        }

        [Fact]
        public void Restoring_writes_the_active_state()
        {
            var update = UpdateRolePlugin.BuildUpdate(Role("AL Portal - Planner", null, active: false), null, null, true, new StringBuilder());

            Assert.Equal(0, update.GetAttributeValue<OptionSetValue>("statecode").Value);
            Assert.Equal(1, update.GetAttributeValue<OptionSetValue>("statuscode").Value);
        }

        [Fact]
        public void An_unchanged_active_state_writes_nothing()
        {
            var update = UpdateRolePlugin.BuildUpdate(Role("AL Portal - Planner", null), null, null, true, new StringBuilder());

            Assert.Empty(update.Attributes);
        }

        [Fact]
        public void Renaming_a_web_role_is_refused_because_the_name_is_its_code()
        {
            // Every al_userrolemapping and al_pagepermission row references the role by
            // al_rolecode, which for a web role IS its name (CreateRolePlugin). Renaming the
            // role would leave every assignment and rule pointing at a name that no longer
            // exists, and the gate resolves web-role associations by name.
            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => UpdateRolePlugin.BuildUpdate(Role("AL Portal - Planner", null), "AL Portal - Paraplanner", null, null, new StringBuilder()));

            Assert.Contains("VALIDATION:", ex.Message);
            Assert.Contains("renamed", ex.Message);
        }

        [Fact]
        public void Sending_the_current_name_back_is_not_a_rename()
        {
            var update = UpdateRolePlugin.BuildUpdate(Role("AL Portal - Planner", null), "  AL Portal - Planner ", null, null, new StringBuilder());

            Assert.Empty(update.Attributes);
        }
    }
}
