using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command UpdateSection (AD-003, AD-043, AD-123). The only in-place edit in
    /// the checklist administration set: sections are not versioned, so the Audit Event's
    /// before/after is what preserves the trail instead.
    ///
    /// Owner role is editable by project owner direction of 2026-09-11, and the change is
    /// retroactive in a way nothing else here is. Owner role is not versioned and carries no
    /// dates, so a section switched from Tax to AQS stops rendering in submitted Tax reviews
    /// and starts rendering in submitted AQS ones, which never answered it. The answers are
    /// not lost - they hang off the review instance and keep resolving - but which discipline
    /// appears to have been asked does change. Both is usually the better answer, because it
    /// adds a discipline without taking one away.
    /// </summary>
    public class UpdateSectionPlugin : PluginBase
    {
        private const string SectionEntity = "al_section";
        private const int CommandUpdateSection = 120910798;

        public UpdateSectionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(UpdateSectionPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService;
            var systemService = localPluginContext.PluginUserService;

            var sectionId = CommandHelpers.ParseRequiredGuid(context, "SectionId");
            var nameArg = CommandHelpers.GetOptionalString(context, "Name");
            var helpTextArg = CommandHelpers.GetOptionalString(context, "HelpText");
            var ownerRoleArg = CommandHelpers.GetOptionalString(context, "OwnerRole");
            var displayOrderArg = CommandHelpers.GetOptionalString(context, "DisplayOrder");
            var isOptionalArg = CommandHelpers.GetOptionalString(context, "IsOptional");
            var reason = CommandHelpers.GetRequiredString(context, "Reason").Trim();
            var idempotencyKey = CommandHelpers.GetRequiredString(context, "IdempotencyKey");

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandUpdateSection);
            if (existingAudit != null)
            {
                SetResponse(
                    context, sectionId.ToString("D"),
                    existingAudit.GetAttributeValue<string>("al_details"), existingAudit.Id);
                return;
            }

            var before = userService.Retrieve(
                SectionEntity, sectionId,
                new ColumnSet("al_name", "al_helptext", "al_ownerrole", "al_displayorder", "al_isoptional", "al_sectioncode"));

            var after = CopyOf(before);

            if (!string.IsNullOrWhiteSpace(nameArg))
            {
                after["al_name"] = nameArg.Trim();
            }

            if (helpTextArg != null)
            {
                after["al_helptext"] = helpTextArg.Trim();
            }

            if (!string.IsNullOrWhiteSpace(ownerRoleArg))
            {
                int ownerRole;
                if (!int.TryParse(ownerRoleArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out ownerRole)
                    || !IsAssignableOwnerRole(ownerRole))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix +
                        "Owner role must be Tax (120910100), AQS (120910101) or Both (120910105).");
                }

                after["al_ownerrole"] = new OptionSetValue(ownerRole);
            }

            if (!string.IsNullOrWhiteSpace(displayOrderArg))
            {
                int displayOrder;
                if (!int.TryParse(displayOrderArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out displayOrder))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + "Display order must be a whole number.");
                }

                after["al_displayorder"] = displayOrder;
            }

            if (!string.IsNullOrWhiteSpace(isOptionalArg))
            {
                bool isOptional;
                if (!bool.TryParse(isOptionalArg.Trim(), out isOptional))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + "Optional must be 'true' or 'false'.");
                }

                // An optional section owes no answers, so making one optional excuses a
                // load-bearing question exactly as retiring it would (AD-122).
                if (isOptional && !IsOptionalOf(before))
                {
                    var refusal = RetireSectionPlugin.ProtectedCodeIn(
                        RetireSectionPlugin.QuestionCodesIn(userService, sectionId));
                    if (refusal != null)
                    {
                        throw new InvalidPluginExecutionException(
                            CommandHelpers.PreconditionPrefix + refusal);
                    }
                }

                after["al_isoptional"] = isOptional;
            }

            var changes = DescribeChanges(before, after);
            if (changes.Length == 0)
            {
                // Nothing to write, and nothing to audit. An Audit Event recording no change
                // is noise in the trail a regulator reads.
                SetResponse(context, sectionId.ToString("D"), string.Empty, Guid.Empty);
                return;
            }

            after.Id = sectionId;
            userService.Update(after);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandUpdateSection,
                "UpdateSection " + before.GetAttributeValue<string>("al_sectioncode"),
                SectionEntity, sectionId, reason, changes, idempotencyKey, context);

            SetResponse(context, sectionId.ToString("D"), changes, auditId);
        }

        /// <summary>
        /// The before and after of each changed field, as one clause per field. Empty when
        /// nothing changed.
        ///
        /// Public and static so it can be tested with plain Entities - the assembly is
        /// signed and carries no InternalsVisibleTo.
        /// </summary>
        public static string DescribeChanges(Entity before, Entity after)
        {
            var clauses = new List<string>();

            AddIfChanged(clauses, "Name",
                before.GetAttributeValue<string>("al_name"),
                after.GetAttributeValue<string>("al_name"));

            AddIfChanged(clauses, "Help text",
                before.GetAttributeValue<string>("al_helptext"),
                after.GetAttributeValue<string>("al_helptext"));

            AddIfChanged(clauses, "Owner role",
                OptionOf(before, "al_ownerrole"),
                OptionOf(after, "al_ownerrole"));

            AddIfChanged(clauses, "Display order",
                IntOf(before, "al_displayorder"),
                IntOf(after, "al_displayorder"));

            // Absent reads as required, because al_isoptional is null on every section that
            // predates AD-123 - Dataverse applies a boolean default to new rows only. Any
            // other reading reports a change that never happened.
            AddIfChanged(clauses, "Optional",
                IsOptionalOf(before) ? "Yes" : "No",
                IsOptionalOf(after) ? "Yes" : "No");

            return string.Join("; ", clauses.ToArray());
        }

        private static void AddIfChanged(ICollection<string> clauses, string field, string before, string after)
        {
            var from = before ?? string.Empty;
            var to = after ?? string.Empty;

            if (!string.Equals(from, to, StringComparison.Ordinal))
            {
                clauses.Add(field + ": '" + from + "' -> '" + to + "'");
            }
        }

        private static string OptionOf(Entity row, string attribute)
        {
            var option = row.GetAttributeValue<OptionSetValue>(attribute);
            return option == null
                ? string.Empty
                : option.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static string IntOf(Entity row, string attribute)
        {
            return row.Contains(attribute)
                ? row.GetAttributeValue<int>(attribute).ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static bool IsOptionalOf(Entity row)
        {
            return row.Contains("al_isoptional") && row.GetAttributeValue<bool>("al_isoptional");
        }

        private static bool IsAssignableOwnerRole(int ownerRole)
        {
            return ownerRole == ResponseRules.OwnerRoleTaxTeam
                || ownerRole == ResponseRules.OwnerRoleAqsChecker
                || ownerRole == SectionRules.OwnerRoleBoth;
        }

        private static Entity CopyOf(Entity row)
        {
            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var pair in row.Attributes)
            {
                copy[pair.Key] = pair.Value;
            }

            return copy;
        }

        private static void SetResponse(
            IPluginExecutionContext context, string sectionId, string changes, Guid auditId)
        {
            context.OutputParameters["SectionId"] = sectionId;
            context.OutputParameters["Changes"] = changes ?? string.Empty;
            context.OutputParameters["AuditEventId"] =
                auditId == Guid.Empty ? string.Empty : auditId.ToString("D");
            context.OutputParameters["Conflict"] = false;
        }
    }
}
