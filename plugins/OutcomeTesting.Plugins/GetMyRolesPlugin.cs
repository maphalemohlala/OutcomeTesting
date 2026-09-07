using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side read GetMyRoles (AD-003, AD-089). Registered against the Custom API
    /// message <c>al_GetMyRoles</c>.
    ///
    /// Answers "what do I hold" for the caller, so the client stops deriving an answer the
    /// server disagrees with. No permission is enforced: the caller is asking about
    /// themselves, and gating it would make the sign-in path depend on a permission that
    /// cannot be resolved until the path completes.
    /// </summary>
    public class GetMyRolesPlugin : PluginBase
    {
        private const string OutRoleCodes = "RoleCodes";

        public GetMyRolesPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(GetMyRolesPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var systemService = localPluginContext.PluginUserService;

            // GetCallerEmail THROWS on a blank work email rather than returning one — it
            // never falls through to the string.IsNullOrWhiteSpace check that used to sit
            // here, which made that check dead code and the "holds nothing" fallback it
            // implied unreachable. Without this catch, a caller with no work email got an
            // API failure instead, and the client turns that into its permissive fail-open —
            // the opposite of what the sign-in path should do. Catching here makes the
            // intended fallback real.
            List<string> codes;
            try
            {
                var email = PermissionHelpers.GetCallerEmail(systemService, context);
                codes = PermissionHelpers.ResolveRoleCodesForEmail(systemService, email);
            }
            catch (InvalidPluginExecutionException)
            {
                codes = new List<string>();
            }

            context.OutputParameters[OutRoleCodes] = ToJson(codes);
        }

        /// <summary>A JSON array of strings; net462 with no serializer, as elsewhere here.</summary>
        public static string ToJson(IEnumerable<string> codes)
        {
            var builder = new StringBuilder("[");
            var first = true;

            foreach (var code in codes ?? new List<string>())
            {
                if (!first)
                {
                    builder.Append(",");
                }

                first = false;
                builder.Append("\"").Append(ImportRules.JsonEscape(code ?? string.Empty)).Append("\"");
            }

            return builder.Append("]").ToString();
        }
    }
}
