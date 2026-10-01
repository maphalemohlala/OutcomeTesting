using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OutcomeTesting.Plugins;

namespace OutcomeTesting.Registration;

/// <summary>
/// The product name of the environment a verb is about to write to.
/// </summary>
/// <remarks>
/// <see cref="ProductName.Read"/> falls back to the default wherever the variable is
/// missing, which is right inside a plug-in. Here it is wrong: these verbs create roles,
/// teams, an account and permission rules that are found by name, and a target whose
/// solution predates al_ProductName gets them under the default name. On 2026-10-01 PROD was
/// given 1.0.8.0 by mistake, and ensureaccessprincipals then created the "Outcome Testing"
/// teams and account there instead of the OTIS ones. So the verbs refuse such a target.
/// </remarks>
internal static class TargetProduct
{
    public static string Read(IOrganizationService svc)
    {
        var definitions = svc.RetrieveMultiple(new QueryExpression("environmentvariabledefinition")
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
            Criteria = { Conditions = { new ConditionExpression("schemaname", ConditionOperator.Equal, ProductName.Variable) } },
        }).Entities;

        if (definitions.Count == 0)
        {
            throw new InvalidOperationException(
                $"This environment has no {ProductName.Variable} variable, so its product name is unknown. " +
                "Its OutcomeTesting solution is older than 1.0.18.0: import the current package first.");
        }

        return ProductName.Read(svc);
    }
}
