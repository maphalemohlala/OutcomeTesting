using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Who may allocate which check (AD-218; closes OD-029(c)). Each team manager allocates
    /// their own discipline only; Outcome Testing Manager and Administrators allocate either.
    /// Anyone else holding command.assign is refused: denied by default, so a role granted
    /// allocation by accident cannot reach across teams.
    ///
    /// Keyed on web role NAME because that is what al_rolecode and every permission rule
    /// carry (AD-087). The app mirrors this in features/cases/allocationScope.ts.
    /// </summary>
    public static class AllocationScope
    {
        public const string TaxTeamManagerRole = "AL Portal - Tax Team Manager";
        public const string AqsTeamManagerRole = "AL Portal - AQS Team Manager";
        private const string OutcomeTestingManagerRole = "AL Portal - Outcome Testing Manager";
        private const string AdministratorsRole = "Administrators";

        public static bool MayAllocate(IEnumerable<string> roleCodes, int reviewType)
        {
            var roles = new HashSet<string>(
                (roleCodes ?? Enumerable.Empty<string>()).Where(r => r != null).Select(r => r.Trim()),
                StringComparer.OrdinalIgnoreCase);

            if (roles.Contains(OutcomeTestingManagerRole) || roles.Contains(AdministratorsRole))
            {
                return true;
            }

            if (reviewType == ResponseRules.ReviewTypeTax)
            {
                return roles.Contains(TaxTeamManagerRole);
            }

            if (reviewType == ResponseRules.ReviewTypeAqs)
            {
                return roles.Contains(AqsTeamManagerRole);
            }

            return false;
        }

        public static void EnsureCallerMayAllocate(
            IOrganizationService systemService, IPluginExecutionContext context, int reviewType)
        {
            // The same bootstrap the permission gate has: before any role is mapped, the first
            // administrator must be able to set the environment up.
            if (!PermissionHelpers.AnyMappingExists(systemService))
            {
                return;
            }

            var roles = PermissionHelpers.ResolveRoleCodesForEmail(
                systemService, PermissionHelpers.GetCallerEmail(systemService, context));
            if (MayAllocate(roles, reviewType))
            {
                return;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.UnauthorizedPrefix
                + "Your role does not allocate " + Discipline(reviewType)
                + " checks. Each team manager allocates their own team's checks.");
        }

        public static void EnsureAssigneeHoldsDiscipline(
            IOrganizationService service, AssignCasePlugin.Assignee assignee, int reviewType)
        {
            string required;
            if (reviewType == ResponseRules.ReviewTypeTax)
            {
                required = WebRoleRegistry.TaxReviewerRole;
            }
            else if (reviewType == ResponseRules.ReviewTypeAqs)
            {
                required = WebRoleRegistry.AqsReviewerRole;
            }
            else
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "This check has no recognised discipline, so it cannot be allocated.");
            }

            if (WebRoleRegistry.HasRole(service, assignee.ContactId, required))
            {
                return;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix
                + assignee.ContactName + " does not hold the " + required + " role, so "
                + (reviewType == ResponseRules.ReviewTypeTax ? "a Tax" : "an AQS")
                + " check cannot be allocated to them.");
        }

        private static string Discipline(int reviewType)
        {
            return reviewType == ResponseRules.ReviewTypeTax ? "Tax"
                : reviewType == ResponseRules.ReviewTypeAqs ? "AQS"
                : "these";
        }
    }
}
