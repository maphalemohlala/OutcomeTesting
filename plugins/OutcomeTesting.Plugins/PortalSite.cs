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

        private const string DefinitionEntity = "environmentvariabledefinition";
        private const string ValueEntity = "environmentvariablevalue";
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
        /// The environment variable's current value, falling back to the default the
        /// definition carries.
        ///
        /// A definition with no value row is the ordinary state straight after an import, and
        /// the default is what the solution shipped - which is why both are read. Swallowed
        /// rather than thrown: see the remarks on the class.
        /// </summary>
        private static string FromVariable(IOrganizationService service)
        {
            try
            {
                var definitions = new QueryExpression(DefinitionEntity)
                {
                    ColumnSet = new ColumnSet("defaultvalue"),
                    TopCount = 1,
                    Criteria = new FilterExpression(),
                };
                definitions.Criteria.AddCondition(
                    "schemaname", ConditionOperator.Equal, BaseUrlVariable);

                var found = service.RetrieveMultiple(definitions).Entities;
                if (found.Count == 0)
                {
                    return null;
                }

                var definition = found[0];

                // Two reads rather than an outer join. The value row is usually there and its
                // absence is the ordinary state straight after an import, so the join would
                // have to be a LeftOuter - and a join whose whole purpose is to tolerate a
                // missing row is harder to read, and harder to be sure of, than asking twice.
                var values = new QueryExpression(ValueEntity)
                {
                    ColumnSet = new ColumnSet("value"),
                    TopCount = 1,
                    Criteria = new FilterExpression(),
                };
                values.Criteria.AddCondition(
                    "environmentvariabledefinitionid", ConditionOperator.Equal, definition.Id);

                var set = service.RetrieveMultiple(values).Entities;
                var current = set.Count == 0
                    ? null
                    : set[0].GetAttributeValue<string>("value");

                // The shipped default is what the solution carried, and it is the right answer
                // where nobody has set one - which is how DEV can work with no configuration.
                return string.IsNullOrWhiteSpace(current)
                    ? definition.GetAttributeValue<string>("defaultvalue")
                    : current;
            }
            catch (Exception)
            {
                return null;
            }
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
