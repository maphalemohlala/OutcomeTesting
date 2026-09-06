using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Registration;

/// <summary>
/// Puts the web role model into effect (spec
/// docs/superpowers/specs/2026-09-06-web-roles-as-the-role-model-design.md).
///
/// Two phases:
///   1. Mirror every contact-to-web-role association into al_UserRoleMapping, so the
///      mapping table holds the same facts the intersect does.
///   2. Seed al_PagePermission rules for the web roles.
///
/// Order matters. The existing rules are keyed on the al_approle picklist and are what
/// currently grants the administrators their access; the web roles grant nothing
/// server-side until phase 2 writes rules against al_rolecode. Seeding before anyone
/// depends on the web roles is what keeps the cut-over from being a lockout. The old rules
/// are left in place for the same reason — they are the floor to fall back to.
/// </summary>
public static class WebRoleSeed
{
    private const string MappingEntity = "al_userrolemapping";
    private const string PermissionEntity = "al_pagepermission";

    private const int AccessView = 120910767;
    private const int AccessEdit = 120910768;
    private const int AccessManage = 120910769;

    private const string TaxReviewer = "AL Portal - Tax Reviewer";
    private const string AqsReviewer = "AL Portal - AQS Reviewer";
    private const string AdviserRemediation = "AL Portal - Adviser Remediation";
    private const string TcSupervisor = "AL Portal - T&C Supervisor";
    private const string OutcomeTestingManager = "AL Portal - Outcome Testing Manager";
    private const string Planner = "AL Portal - Planner";
    private const string PortalAdministrator = "AL Portal - Portal Administrator";
    private const string Administrators = "Administrators";

    private static readonly string[] AllRoles =
    {
        TaxReviewer, AqsReviewer, AdviserRemediation, TcSupervisor,
        OutcomeTestingManager, Planner, PortalAdministrator, Administrators,
    };

    /// <summary>
    /// The same matrix as DEFAULT_PERMISSIONS in app/src/types/permissions.ts. Duplicated
    /// rather than shared because that file is TypeScript in the Code App; the two are the
    /// client mirror and the server seed of one decision, and they have to be edited
    /// together.
    /// </summary>
    private static IEnumerable<(string Role, string Resource, int Level)> Matrix()
    {
        foreach (var role in AllRoles)
        {
            yield return (role, "page.dashboard", AccessView);
        }

        yield return (TaxReviewer, "page.cases", AccessView);
        yield return (TaxReviewer, "page.reviews", AccessEdit);
        yield return (AqsReviewer, "page.cases", AccessView);
        yield return (AqsReviewer, "page.reviews", AccessEdit);

        yield return (AdviserRemediation, "page.remediation", AccessEdit);
        yield return (AdviserRemediation, "remediation.complete", AccessEdit);

        yield return (TcSupervisor, "page.cases", AccessEdit);
        yield return (TcSupervisor, "page.remediation", AccessEdit);
        yield return (TcSupervisor, "page.reports", AccessView);
        yield return (TcSupervisor, "command.regrade", AccessEdit);
        yield return (TcSupervisor, "command.signoff", AccessEdit);
        yield return (TcSupervisor, "command.assign", AccessEdit);

        yield return (OutcomeTestingManager, "page.cases", AccessEdit);
        yield return (OutcomeTestingManager, "page.imports", AccessEdit);
        yield return (OutcomeTestingManager, "page.remediation", AccessView);
        yield return (OutcomeTestingManager, "page.reports", AccessView);
        yield return (OutcomeTestingManager, "page.exports", AccessManage);
        yield return (OutcomeTestingManager, "command.assign", AccessEdit);
        yield return (OutcomeTestingManager, "export.generate", AccessEdit);

        // Planner and Adviser Remediation share remediation routing (OD-019, implemented
        // 2026-08-31): the portal binds both to the same Contact-scoped remediation
        // permission and page rule, so they carry the same authority here.
        yield return (Planner, "page.cases", AccessView);
        yield return (Planner, "page.remediation", AccessEdit);
        yield return (Planner, "remediation.complete", AccessEdit);

        foreach (var admin in new[] { PortalAdministrator, Administrators })
        {
            yield return (admin, "page.cases", AccessView);
            yield return (admin, "page.imports", AccessEdit);
            yield return (admin, "page.remediation", AccessView);
            yield return (admin, "page.reports", AccessView);
            yield return (admin, "page.exports", AccessManage);
            yield return (admin, "page.admin.questions", AccessManage);
            yield return (admin, "page.admin.security", AccessManage);
            yield return (admin, "page.admin.users", AccessManage);
            yield return (admin, "question.retire", AccessEdit);
            yield return (admin, "export.generate", AccessEdit);
            yield return (admin, "permission.manage", AccessManage);
        }
    }

    public static int Run(IOrganizationService svc)
    {
        MirrorAssignments(svc);
        SeedPermissions(svc);
        Console.WriteLine();
        Console.WriteLine("Web role model seeded.");
        return 0;
    }

    /// <summary>
    /// Phase 1. Every contact-to-web-role association becomes an al_UserRoleMapping row
    /// carrying the web role name in al_rolecode, which is what the permission rules match.
    /// </summary>
    private static void MirrorAssignments(IOrganizationService svc)
    {
        Console.WriteLine("1. Mirroring web role assignments into al_userrolemapping…");

        // Starts at the contact: the intersect hangs off contact, not off the role.
        const string fetch =
            "<fetch>" +
              "<entity name='contact'>" +
                "<attribute name='contactid'/>" +
                "<attribute name='emailaddress1'/>" +
                "<link-entity name='powerpagecomponent_mspp_webrole_contact' from='contactid' to='contactid' intersect='true'>" +
                  "<link-entity name='powerpagecomponent' from='powerpagecomponentid' to='powerpagecomponentid' alias='role'>" +
                    "<attribute name='name'/>" +
                  "</link-entity>" +
                "</link-entity>" +
              "</entity>" +
            "</fetch>";

        var written = 0;
        foreach (var row in svc.RetrieveMultiple(new FetchExpression(fetch)).Entities)
        {
            var email = (row.GetAttributeValue<string>("emailaddress1") ?? string.Empty).Trim();
            var aliased = row.GetAttributeValue<AliasedValue>("role.name");
            var roleName = (aliased?.Value as string ?? string.Empty).Trim();

            if (email.Length == 0 || roleName.Length == 0)
            {
                continue;
            }

            var code = "URM-" + email.ToLowerInvariant() + "-" + roleName;
            var mapping = new Entity(MappingEntity)
            {
                ["al_name"] = roleName + " - " + email,
                ["al_useremail"] = email,
                ["al_rolecode"] = roleName,
                // Explicitly null: al_approle carries a schema default, so a mirror row
                // written without it comes back carrying a picklist role nobody assigned.
                ["al_approle"] = null,
                ["al_userrolemappingcode"] = code,
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1),
            };

            Upsert(svc, MappingEntity, "al_userrolemappingcode", code, mapping);
            Console.WriteLine($"   {email} -> {roleName}");
            written++;
        }

        Console.WriteLine($"   {written} assignment(s) mirrored.");
        Console.WriteLine();
    }

    /// <summary>
    /// Phase 2. Writes the matrix as al_PagePermission rows keyed on al_rolecode, which is
    /// how a web role reaches the server-side gate.
    /// </summary>
    private static void SeedPermissions(IOrganizationService svc)
    {
        Console.WriteLine("2. Seeding al_pagepermission rules for the web roles…");

        var written = 0;
        foreach (var (role, resource, level) in Matrix())
        {
            var code = "PP-" + Slug(role) + "-" + resource;
            var rule = new Entity(PermissionEntity)
            {
                ["al_name"] = role + " / " + resource,
                ["al_pagepermissioncode"] = code,
                ["al_rolecode"] = role,
                ["al_resourcekey"] = resource,
                ["al_accesslevel"] = new OptionSetValue(level),
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1),
            };

            Upsert(svc, PermissionEntity, "al_pagepermissioncode", code, rule);
            written++;
        }

        Console.WriteLine($"   {written} rule(s) written.");
        Console.WriteLine();
    }

    private static string Slug(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '-').ToArray();
        return new string(chars).Replace("--", "-").Trim('-');
    }

    /// <summary>Create or update on a business code, so a re-run changes nothing twice.</summary>
    private static Guid Upsert(IOrganizationService svc, string entity, string keyAttr, string keyValue, Entity values)
    {
        var query = new QueryExpression(entity)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
            Criteria = new FilterExpression(),
        };
        query.Criteria.AddCondition(keyAttr, ConditionOperator.Equal, keyValue);

        var found = svc.RetrieveMultiple(query).Entities;
        if (found.Count == 0)
        {
            return svc.Create(values);
        }

        values.Id = found[0].Id;
        values.LogicalName = entity;
        // statecode/statuscode are set through SetState, not Update, and re-sending them on
        // an existing row is refused by the platform.
        values.Attributes.Remove("statecode");
        values.Attributes.Remove("statuscode");
        svc.Update(values);
        return values.Id;
    }
}
