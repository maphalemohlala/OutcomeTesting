using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Holds a direct write of an outcome's final grade or fail accountability to the
    /// permission the command that owns it checks. Registered synchronous pre-operation on
    /// Update of <c>al_outcome</c>, filtered to the columns below.
    ///
    /// The app's Dataverse role writes al_outcome at Global depth because the commands
    /// write as the caller, so without this anyone holding it could set a final grade or
    /// name who carries a fail straight on the table. The commands' own writes still pass:
    /// each caller already holds the permission checked here, the portal's request
    /// plug-ins write as the site, and our SYSTEM code is trusted
    /// (<see cref="WriteProvenance"/>). An unchanged value is not a change, so a submit
    /// that upserts over its own earlier write is never asked for a permission it lacks.
    /// </summary>
    public class OutcomeGuardPlugin : PluginBase
    {
        private const string OutcomeEntity = "al_outcome";

        // The permission al_SetFailAccountability checks.
        private const string AccountabilityResource = "page.cases";

        public static readonly string[] FinalGradeColumns =
        {
            Outcomes.FinalOutcomeAttr, "al_regradereason", "al_regradedon", "al_finalisedon",
        };

        public static readonly string[] AccountabilityColumns =
        {
            "al_fqadviseraccountable", "al_fqparaplanneraccountable",
            "al_aqadviseraccountable", "al_aqparaplanneraccountable",
            "al_fqaccountablecontactid", "al_aqaccountablecontactid",
            GenerateExportPlugin.FqAdministratorContactAttr,
            GenerateExportPlugin.FqAdministratorFlag,
        };

        public OutcomeGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(OutcomeGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            if (target == null || !string.Equals(target.LogicalName, OutcomeEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Guard(localPluginContext.OrgSvcFactory.CreateOrganizationService(null), context, target);
        }

        public static void Guard(IOrganizationService systemService, IPluginExecutionContext context, Entity target)
        {
            var columns = FinalGradeColumns.Concat(AccountabilityColumns).Where(target.Contains).ToArray();
            if (columns.Length == 0 || WriteProvenance.IsSystemWrite(systemService, context))
            {
                return;
            }

            // A Create is held to the same rule against an empty row: without it a final grade
            // written on a pre-made outcome survived the real submit, which updates that row.
            var stored = string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                ? new Entity(OutcomeEntity)
                : systemService.Retrieve(OutcomeEntity, target.Id, new ColumnSet(columns));
            var changed = columns.Where(c => !SameValue(target[c], stored.Contains(c) ? stored[c] : null)).ToList();
            if (changed.Count == 0 || WriteProvenance.IsApplicationUser(systemService, context.InitiatingUserId))
            {
                return;
            }

            if (changed.Any(FinalGradeColumns.Contains))
            {
                EnsureMaySetFinalGrade(systemService, context);
            }

            if (changed.Any(AccountabilityColumns.Contains))
            {
                PermissionHelpers.EnsureAppPermission(
                    systemService, context, AccountabilityResource, PermissionHelpers.AccessEdit);
            }
        }

        /// <summary>
        /// The regrade permission only. A sign-off records its grade as SYSTEM (SignoffProgress),
        /// so the sign-off permission is not leave to set any final grade on any outcome.
        /// </summary>
        private static void EnsureMaySetFinalGrade(IOrganizationService systemService, IPluginExecutionContext context)
        {
            PermissionHelpers.EnsureAppPermission(
                systemService, context, RegradeCasePlugin.RegradeResource, PermissionHelpers.AccessEdit);
        }

        // An unset two-option column reads as false, so writing false over it is no change.
        private static bool SameValue(object written, object stored)
        {
            if (written == null || stored == null)
            {
                var other = written ?? stored;
                return other == null || (other is bool && !(bool)other);
            }

            var writtenOption = written as OptionSetValue;
            if (writtenOption != null)
            {
                var storedOption = stored as OptionSetValue;
                return storedOption != null && storedOption.Value == writtenOption.Value;
            }

            var writtenRef = written as EntityReference;
            if (writtenRef != null)
            {
                var storedRef = stored as EntityReference;
                return storedRef != null && storedRef.Id == writtenRef.Id;
            }

            return written.Equals(stored);
        }
    }
}
