using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Who a table write really comes from, for the guards that stop a direct write
    /// skipping the command that owns it.
    ///
    /// The app's own Dataverse role keeps Global create and write on the tables the commands
    /// write, because the commands write as the caller on purpose. So a guard cannot rely on
    /// the platform privilege: it trusts our own code running as SYSTEM, and the portal's
    /// application user (whose request plug-ins do their own role checks), and holds
    /// everybody else to the permission the matching command checks.
    ///
    /// Deliberately not ParentContext: walking it to prove a write came from inside a
    /// command was tried and was false in DEV even on the command's own path.
    /// </summary>
    public static class WriteProvenance
    {
        private const string UserEntity = "systemuser";

        /// <summary>The SYSTEM user's id per organisation, asked once.</summary>
        private static readonly ConcurrentDictionary<Guid, Guid> SystemUserIds = new ConcurrentDictionary<Guid, Guid>();
        private const string ApplicationIdAttr = "applicationid";

        /// <summary>
        /// True for a write our plug-in code made through <c>CreateOrganizationService(null)</c>.
        ///
        /// <para>
        /// It used to test that <c>UserId</c> and <c>InitiatingUserId</c> differ. Live DEV,
        /// 2026-11-17, proved that false: the action write inside al_CompleteRemediation traced
        /// "Initiating User" as the SYSTEM user itself, so a SYSTEM write arrives with SYSTEM in
        /// both, and the guards refused our own code. It also let an impersonating caller (whose
        /// two ids do differ) pass as SYSTEM. So the SYSTEM user's id is asked of
        /// <paramref name="systemService"/> by WhoAmI, once per organisation, and compared.
        /// </para>
        /// <para>
        /// <paramref name="systemService"/> MUST come from <c>CreateOrganizationService(null)</c>:
        /// WhoAmI on the caller's service would name the caller, who would then pass as SYSTEM.
        /// </para>
        /// </summary>
        public static bool IsSystemWrite(IOrganizationService systemService, IPluginExecutionContext context)
        {
            if (systemService == null || context == null)
            {
                return false;
            }

            var system = SystemUserIds.GetOrAdd(
                context.OrganizationId,
                _ => (Guid)systemService.Execute(new WhoAmIRequest()).Results["UserId"]);
            return system != Guid.Empty && (context.UserId == system || context.InitiatingUserId == system);
        }

        /// <summary>
        /// The environment variable holding the application (client) id the portal's writes
        /// arrive as: the <c>applicationid</c> on that application user.
        ///
        /// <para>
        /// That is NOT the site's own "# Portals-..." registration. DEV's plug-in traces of
        /// 2026-11-16 show the portal's answer writes initiated by "# PowerPages Data Runtime
        /// PROD" (client id f61b1d65-0f1b-4ec8-8721-cfeca7c30416); "# Portals-Outcome Testing"
        /// appears in no trace and on no row.
        /// </para>
        /// <para>
        /// Not read from the site: <c>powerpagesite</c> has no column naming an application
        /// (DEV, 2026-11-17), and the row is imported from DEV anyway, as
        /// <see cref="PortalSite"/> explains. The client id rather than the systemuserid: the
        /// user row's id differs per environment, the client id survives the user being
        /// re-created. It may well be the same everywhere, but that is unchecked, so the
        /// definition ships with no default and each environment sets its own.
        /// </para>
        /// </summary>
        public const string PortalApplicationIdVariable = "al_PortalApplicationId";

        /// <summary>
        /// True when the user is the portal's application user. Power Pages writes as the
        /// site's application user; none of the people holding the app's Dataverse role is one.
        /// Read with the system service so the answer never depends on the caller's reads.
        ///
        /// Where <see cref="PortalApplicationIdVariable"/> is set, only that application
        /// passes; flows, integrations and platform services are application users too.
        /// </summary>
        public static bool IsApplicationUser(IOrganizationService systemService, Guid userId)
        {
            // A query, not a Retrieve: an id with no user row is simply not the portal, and a
            // Retrieve's not-found fault would abort the caller's whole write.
            var query = new QueryExpression(UserEntity) { ColumnSet = new ColumnSet(ApplicationIdAttr), TopCount = 1 };
            query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            var user = systemService.RetrieveMultiple(query).Entities.FirstOrDefault();
            var applicationId = user == null ? null : user.GetAttributeValue<Guid?>(ApplicationIdAttr);
            if (!applicationId.HasValue || applicationId.Value == Guid.Empty)
            {
                return false;
            }

            // Read only for application users, so a person's write costs no extra query.
            string pinned;
            if (!EnvironmentVariable.TryRead(systemService, PortalApplicationIdVariable, out pinned))
            {
                // Could not be read, which is not the same as unset: refuse rather than reopen
                // the door to every application user.
                return false;
            }

            if (string.IsNullOrWhiteSpace(pinned))
            {
                // shortcut: unset, any application user passes as the portal (flows, integrations,
                // platform services too), upgrade by setting al_PortalApplicationId in every environment.
                return true;
            }

            // Set but not a GUID is a typo; refusing makes it visible rather than silently open.
            Guid portal;
            return Guid.TryParse(pinned.Trim(), out portal) && portal == applicationId.Value;
        }

        /// <summary>
        /// Refuses a portal request column written by anyone but the portal. The request
        /// plug-ins trust the contact row that was written as the signed-in person, which
        /// only holds when the site wrote it.
        /// </summary>
        public static void EnsurePortalCaller(IOrganizationService systemService, IPluginExecutionContext context)
        {
            if (IsApplicationUser(systemService, context.InitiatingUserId))
            {
                return;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.UnauthorizedPrefix + "This request can only be made from the portal.");
        }

        /// <summary>
        /// True for SYSTEM or the portal's application user. The INITIATING user, as
        /// <see cref="EnsurePortalCaller"/> reads it: a caller impersonating the site's application
        /// user sets UserId to it but stays the initiating user.
        /// </summary>
        public static bool IsTrusted(IOrganizationService systemService, IPluginExecutionContext context)
        {
            return IsSystemWrite(systemService, context) || IsApplicationUser(systemService, context.InitiatingUserId);
        }

        /// <summary>
        /// Lets SYSTEM and the portal through and holds anyone else to the permission the
        /// matching command checks.
        /// </summary>
        public static void EnsureTrustedOrPermitted(
            IOrganizationService systemService, IPluginExecutionContext context, string resourceKey, int requiredLevel)
        {
            if (IsTrusted(systemService, context))
            {
                return;
            }

            PermissionHelpers.EnsureAppPermission(systemService, context, resourceKey, requiredLevel);
        }
    }
}
