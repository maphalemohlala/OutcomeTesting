using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Reads a Dataverse environment variable: the value set on this environment, else the
    /// default its definition shipped with. The one shape for anything that has to differ
    /// between environments while the solution stays the same - see <see cref="PortalSite"/>
    /// and <see cref="ProductName"/>.
    /// </summary>
    public static class EnvironmentVariable
    {
        private const string DefinitionEntity = "environmentvariabledefinition";
        private const string ValueEntity = "environmentvariablevalue";

        /// <summary>
        /// The current value, else the shipped default, else null. Never throws: a caller
        /// reading configuration inside someone's save must not cost them the save.
        /// </summary>
        public static string Read(IOrganizationService service, string schemaName)
        {
            try
            {
                var definitions = new QueryExpression(DefinitionEntity)
                {
                    ColumnSet = new ColumnSet("defaultvalue"),
                    TopCount = 1,
                    Criteria = new FilterExpression(),
                };
                definitions.Criteria.AddCondition("schemaname", ConditionOperator.Equal, schemaName);

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
    }
}
