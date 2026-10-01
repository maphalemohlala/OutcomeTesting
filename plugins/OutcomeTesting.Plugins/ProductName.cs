using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// What this environment calls the product: OTIS in PROD, "Outcome Testing" everywhere
    /// else (owner, 2026-10-01).
    /// </summary>
    /// <remarks>
    /// The name is a label in some places and a KEY in others: the manager web role, the
    /// security roles, the two teams and the AQS queue account are all found by name. So
    /// every one of them is derived here from the one per-environment value and never written
    /// out anywhere else - renaming one place without the others would quietly take access
    /// away. An environment that sets nothing gets <see cref="Default"/>, and every derived
    /// name is then exactly the literal that was in use before this class existed.
    /// </remarks>
    public static class ProductName
    {
        /// <summary>The environment variable's schema name. Its definition ships with the default.</summary>
        public const string Variable = "al_ProductName";

        public const string Default = "Outcome Testing";

        /// <summary>This environment's product name. Never throws and never returns blank.</summary>
        public static string Read(IOrganizationService service)
        {
            var value = EnvironmentVariable.Read(service, Variable);
            return string.IsNullOrWhiteSpace(value) ? Default : value.Trim();
        }

        /// <summary>The portal web role that oversees and allocates both disciplines.</summary>
        public static string ManagerWebRole(string product) { return "AL Portal - " + product + " Manager"; }

        public static string AppUserRole(string product) { return product + " App User"; }

        public static string AppAdminRole(string product) { return product + " App Admin"; }

        public static string TeamManagerRole(string product) { return product + " Team Manager"; }

        public static string TaxTeam(string product) { return product + " - Tax Team"; }

        public static string AqsTeam(string product) { return product + " - AQS Team"; }

        /// <summary>The account whose contacts read the AQS queue. Named as the AQS team is.</summary>
        public static string AqsQueueAccount(string product) { return AqsTeam(product); }

        public static string ChecklistTitle(string product) { return product + " - Checker Checklist"; }

        public static string ChecklistFooter(string product) { return product + " Checker Checklist | V5 Draft"; }

        public static string RemediationFooter(string product) { return product + " — Remediation and escalation | V8"; }
    }
}
