using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Where this environment's Power Pages site actually lives, for the links the
    /// notification bodies carry (reported 2026-09-22: "the links that go out on the Test
    /// portal still use the old dev url").
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the site row cannot answer this.</b> <c>powerpagesite.primarydomainname</c> looks
    /// like the obvious source and was the only one until now. But the site row is a SOLUTION
    /// COMPONENT: it is exported from DEV and imported everywhere else as a managed row,
    /// carrying DEV's domain with it. On 2026-09-22 the single row on Env_AQ_Test read
    /// <c>outcometesting.powerappsportals.com</c> - DEV's host - although the site is served
    /// from <c>outcometestingtest</c>. Every allocation, remediation and pass letter sent from
    /// TEST therefore linked recipients into DEV, and PROD would have done the same.
    /// </para>
    /// <para>
    /// Correcting the row on each environment would not hold: it is managed, so the next
    /// solution import puts DEV's value back, and nothing would say why it had moved.
    /// </para>
    /// <para>
    /// <b>The environment variable is the answer.</b> Its DEFINITION travels in the solution
    /// and its VALUE is set per environment, which is exactly the shape of the problem - and
    /// it is what the deployment-profiles README already prescribes for anything
    /// environment-bound. Set it on each environment after an import; the site row remains the
    /// fallback, so an environment where nobody has set it behaves exactly as it did before
    /// rather than sending letters with no link at all.
    /// </para>
    /// <para>
    /// A fallback and not a failure, deliberately. A missing link costs the reader a click;
    /// a plug-in that threw would cost them the letter, inside the transaction holding a
    /// checker's submit open.
    /// </para>
    /// </remarks>
    public static class PortalSite
    {
        /// <summary>
        /// The environment variable naming this environment's portal, host or full URL.
        /// Schema name, which is what <c>environmentvariabledefinition</c> is keyed by.
        /// </summary>
        public const string BaseUrlVariable = "al_PortalBaseUrl";

        private const string SiteEntity = "powerpagesite";
        private const string SiteDomainAttr = "primarydomainname";

        /// <summary>
        /// This environment's portal base URL, with no trailing slash, or null where neither
        /// source can answer.
        ///
        /// The environment variable wins wherever it is set, because it is the only source
        /// that can differ between environments. The site row is read only when it is not.
        /// </summary>
        public static string BaseUrl(IOrganizationService service)
        {
            return Normalise(FromVariable(service)) ?? Normalise(FromSiteRow(service));
        }

        /// <summary>
        /// A link into the portal for one case, or null where the site is not known.
        ///
        /// Kept here rather than in the outbox so that the two things that can go wrong - not
        /// knowing the host, and building the path - are answered in one place, and so a
        /// second kind of link has somewhere to be added.
        /// </summary>
        public static string CaseLink(IOrganizationService service, Guid caseId)
        {
            var baseUrl = BaseUrl(service);
            return baseUrl == null
                ? null
                : baseUrl + "/case-details?id=" + caseId.ToString("D");
        }

        /// <summary>
        /// A link into one case's remediation, or null where the site is not known.
        ///
        /// The remedial letter asks the adviser to confirm the action taken, and that is done on
        /// the remediation page, not the case record (project owner, 2026-10-06). The page reads
        /// the case from <c>?case=</c>; see the OT Remediation web template.
        /// </summary>
        public static string RemediationLink(IOrganizationService service, Guid caseId)
        {
            var baseUrl = BaseUrl(service);
            return baseUrl == null
                ? null
                : baseUrl + "/remediation?case=" + caseId.ToString("D");
        }

        /// <summary>
        /// The environment variable's current value, falling back to the default the
        /// definition carries. Swallowed rather than thrown: see the remarks on the class.
        /// </summary>
        private static string FromVariable(IOrganizationService service)
        {
            return EnvironmentVariable.Read(service, BaseUrlVariable);
        }

        /// <summary>
        /// The Power Pages site row's domain - right on DEV, and stale on every environment
        /// the solution was imported into. Kept as the fallback so an environment nobody has
        /// configured behaves as it did before.
        /// </summary>
        private static string FromSiteRow(IOrganizationService service)
        {
            try
            {
                var sites = service.RetrieveMultiple(new QueryExpression(SiteEntity)
                {
                    ColumnSet = new ColumnSet(SiteDomainAttr),
                    TopCount = 1,
                }).Entities;

                return sites.Count == 0
                    ? null
                    : sites[0].GetAttributeValue<string>(SiteDomainAttr);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A host or a URL as one https URL with no trailing slash, or null where there is
        /// nothing to normalise.
        ///
        /// Both forms are accepted because both will be typed. The site row holds a bare host
        /// ("outcometestingtest.powerappsportals.com"), and whoever sets the environment
        /// variable is being asked for "the portal's address", which people write with the
        /// scheme on it. Refusing one of them would be a configuration error that produced a
        /// letter with a broken link rather than a message anyone could act on.
        /// </summary>
        private static string Normalise(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var text = value.Trim().TrimEnd('/');
            if (text.Length == 0)
            {
                return null;
            }

            if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }

            return "https://" + text;
        }
    }
}
