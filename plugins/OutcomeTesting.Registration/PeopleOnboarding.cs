using System.Globalization;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OutcomeTesting.Plugins;

namespace OutcomeTesting.Registration;

/// <summary>
/// One person to give access to, read from a people file.
/// </summary>
internal sealed record Person(string Email, string Name, string StaffCode, IReadOnlyList<string> WebRoles, string TcManager = "", bool Admin = false);

/// <summary>
/// Gives a list of people everything a person needs here, in one pass (owner, 2026-10-01:
/// PROD's people come from a spreadsheet, about 330 of them).
/// </summary>
/// <remarks>
/// Per person, the same four planes the single-person verbs cover one at a time:
/// <list type="number">
/// <item>Dataverse: Basic User and the app's App User role (grantapprole). Both, because an
///   app role without Basic User faults in SecLib::CheckPrivilege.</item>
/// <item>The contact, created with the email, name and staff code when none has the email.</item>
/// <item>Web roles, through al_AssignUserRole as grantrole does, so the association and the
///   al_userrolemapping mirror are written together.</item>
/// <item>Portal sign-in: the binding, the identity username, a security stamp and
///   logonenabled (bindidentity, setsecuritystamp, enableportallogin;
///   docs/reference/portal-access-runbook.md).</item>
/// </list>
/// Additive and idempotent: it never removes a role, never repoints a binding and never
/// rotates a stamp, so a second run finishes what the first could not. Without --apply it
/// writes nothing and says what it would do.
///
/// It cannot add someone to the environment. A person with no systemuser row is skipped
/// whole, and so is any adviser mapping that names them; they are listed for
/// `pac admin assign-user`, and a later run picks them up.
/// </remarks>
internal static class PeopleOnboarding
{
    public const string BasicUser = "Basic User";

    /// <summary>
    /// Reads email,name,staffcode,roles[,tcmanager[,admin]] with a header row. Roles are
    /// separated by ';'. tcmanager is the adviser's T&amp;C Manager's email; admin "yes" adds
    /// the App Admin security role.
    /// </summary>
    public static List<Person> ReadFile(string path)
    {
        var people = new List<Person>();
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = SplitCsv(lines[i]);
            if (cells.Count < 4) throw new InvalidOperationException($"Line {i + 1}: expected email,name,staffcode,roles.");
            var roles = cells[3].Split(';').Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
            var manager = cells.Count > 4 ? cells[4].Trim() : string.Empty;
            var admin = cells.Count > 5 && cells[5].Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
            people.Add(new Person(cells[0].Trim(), cells[1].Trim(), cells[2].Trim(), roles, manager, admin));
        }

        var duplicate = people.GroupBy(p => p.Email, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) throw new InvalidOperationException($"{duplicate.Key} is listed twice; merge the roles onto one line.");
        return people;
    }

    /// <summary>"Paul De Vries" is Paul / De Vries; a single word is a last name alone.</summary>
    public static (string First, string Last) SplitName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length < 2 ? (string.Empty, name.Trim()) : (words[0], string.Join(' ', words.Skip(1)));
    }

    public static int Run(IOrganizationService svc, string orgUrl, List<Person> people, bool apply, string? issuerArg)
    {
        var product = TargetProduct.Read(svc);
        var appUser = ProductName.AppUserRole(product);
        var appAdmin = ProductName.AppAdminRole(product);
        Console.WriteLine($"Product name in {orgUrl}: {product}. {(apply ? "APPLYING" : "Dry run - nothing is written")}.");

        // Every web role named in the file has to exist before anything is written.
        var webRoles = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in people.SelectMany(p => p.WebRoles).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var id = WebRoleId(svc, role);
            if (id == Guid.Empty)
            {
                Console.Error.WriteLine($"No web role is named '{role}' in {orgUrl}. Nothing was written.");
                return 1;
            }

            webRoles[role] = id;
        }

        // The issuer is copied from a binding that already signs in, as bindidentity does.
        var identities = svc.RetrieveMultiple(new QueryExpression("adx_externalidentity")
        {
            ColumnSet = new ColumnSet("adx_username", "adx_identityprovidername", "adx_contactid"),
        }).Entities;
        var providers = identities
            .Select(e => (e.GetAttributeValue<string>("adx_identityprovidername") ?? string.Empty).Trim())
            .Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string? issuer = providers.Count == 1 ? providers[0] : null;
        if (providers.Count > 1)
        {
            Console.Error.WriteLine($"{providers.Count} identity providers are in use; bindings will be skipped.");
        }
        else if (providers.Count == 0 && !string.IsNullOrWhiteSpace(issuerArg))
        {
            issuer = issuerArg.Trim();
            Console.WriteLine($"No binding yet; using --issuer {issuer}.");
        }
        else if (providers.Count == 0)
        {
            Console.WriteLine("No binding exists to copy the issuer from, so no sign-ins are bound this run. " +
                              "Bootstrap one sign-in first (portal-access-runbook.md, 'The first person'), then run again.");
        }

        var bound = identities
            .Where(e => Guid.TryParse(e.GetAttributeValue<string>("adx_username"), out _))
            .ToDictionary(e => Guid.Parse(e.GetAttributeValue<string>("adx_username")), e => e.GetAttributeValue<EntityReference>("adx_contactid")?.Id ?? Guid.Empty);

        var roleIds = new Dictionary<(Guid, string), Guid>();
        Guid SecurityRole(Guid businessUnit, string name)
        {
            if (!roleIds.TryGetValue((businessUnit, name), out var id))
            {
                var q = new QueryExpression("role") { ColumnSet = new ColumnSet(false), TopCount = 1 };
                q.Criteria.AddCondition("name", ConditionOperator.Equal, name);
                q.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, businessUnit);
                id = svc.RetrieveMultiple(q).Entities.FirstOrDefault()?.Id ?? Guid.Empty;
                roleIds[(businessUnit, name)] = id;
            }

            return id;
        }

        var tally = new SortedDictionary<string, int>();
        void Count(string what) => tally[what] = tally.TryGetValue(what, out var n) ? n + 1 : 1;
        var notUsers = new List<Person>();
        var failures = new List<string>();
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var person in people)
        {
            var email = person.Email;
            var did = new List<string>();
            try
            {
                // 1. Dataverse user and security roles.
                var user = FindUser(svc, email);
                if (user == null)
                {
                    // Skipped whole (owner, 2026-10-01: "do not action them"). A contact and web
                    // roles without a user give a portal account nobody can sign in to.
                    notUsers.Add(person);
                    skipped.Add(person.Email);
                    Console.WriteLine($"  SKIPPED  {email}: not a user of this environment");
                    Count("skipped - not a user of this environment");
                    continue;
                }

                {
                    var bu = user.GetAttributeValue<EntityReference>("businessunitid").Id;
                    var held = HeldRoles(svc, user.Id);
                    foreach (var roleName in person.Admin ? new[] { BasicUser, appUser, appAdmin } : new[] { BasicUser, appUser })
                    {
                        if (held.Contains(roleName)) continue;
                        var roleId = SecurityRole(bu, roleName);
                        if (roleId == Guid.Empty) throw new InvalidOperationException($"no '{roleName}' role in the user's business unit");
                        if (apply)
                        {
                            svc.Associate("systemuser", user.Id, new Relationship("systemuserroles_association"),
                                new EntityReferenceCollection { new EntityReference("role", roleId) });
                        }

                        did.Add("+" + roleName);
                        Count("security role granted: " + roleName);
                    }
                }

                // 2. The contact.
                var contacts = svc.RetrieveMultiple(new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet("al_staffcode", "adx_identity_securitystamp", "adx_identity_logonenabled", "adx_identity_username"),
                    TopCount = 2,
                    Criteria = { Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.Equal, email), new ConditionExpression("statecode", ConditionOperator.Equal, 0) } },
                }).Entities;
                if (contacts.Count > 1) throw new InvalidOperationException("two active contacts have this email");

                Entity? contact = contacts.FirstOrDefault();
                var contactId = contact?.Id ?? Guid.Empty;
                if (contact == null)
                {
                    var (first, last) = SplitName(person.Name);
                    var created = new Entity("contact") { ["firstname"] = first, ["lastname"] = last, ["emailaddress1"] = email };
                    if (person.StaffCode.Length > 0) created["al_staffcode"] = person.StaffCode;
                    if (apply) contactId = svc.Create(created);
                    did.Add("+contact");
                    Count("contact created");
                }
                else if (person.StaffCode.Length > 0 && string.IsNullOrWhiteSpace(contact.GetAttributeValue<string>("al_staffcode")))
                {
                    if (apply) svc.Update(new Entity("contact", contactId) { ["al_staffcode"] = person.StaffCode });
                    did.Add("+staff code");
                    Count("staff code set");
                }

                // 3. Web roles.
                var current = contactId == Guid.Empty ? new HashSet<Guid>() : WebRolesOf(svc, contactId);
                foreach (var role in person.WebRoles)
                {
                    if (current.Contains(webRoles[role])) continue;
                    if (apply)
                    {
                        svc.Execute(new OrganizationRequest("al_AssignUserRole")
                        {
                            ["UserEmail"] = email,
                            ["RoleCode"] = role,
                            ["AppRole"] = string.Empty,
                            ["IdempotencyKey"] = "ONBOARD-" + email.ToLowerInvariant() + "-" + role,
                        });
                    }

                    did.Add("+" + role);
                    Count("web role granted: " + role);
                }

                // 4. Sign-in. Needs the person's Entra object id, which only a systemuser carries.
                var objectId = user?.GetAttributeValue<Guid?>("azureactivedirectoryobjectid");
                if (objectId.HasValue && objectId.Value != Guid.Empty)
                {
                    if (bound.TryGetValue(objectId.Value, out var boundTo))
                    {
                        if (contactId != Guid.Empty && boundTo != contactId)
                        {
                            throw new InvalidOperationException("their sign-in is already bound to a different contact; use bindidentity --repoint if that is wrong");
                        }
                    }
                    else if (issuer != null)
                    {
                        if (apply)
                        {
                            svc.Create(new Entity("adx_externalidentity")
                            {
                                ["adx_username"] = objectId.Value.ToString("D"),
                                ["adx_identityprovidername"] = issuer,
                                ["adx_contactid"] = new EntityReference("contact", contactId),
                            });
                            bound[objectId.Value] = contactId;
                        }

                        did.Add("+sign-in");
                        Count("sign-in bound");
                    }
                    else
                    {
                        Count("sign-in waiting for the first binding");
                    }

                    if (issuer != null || bound.ContainsKey(objectId.Value))
                    {
                        var username = objectId.Value.ToString("D");
                        var update = new Entity("contact", contactId);
                        if (!string.Equals(contact?.GetAttributeValue<string>("adx_identity_username"), username, StringComparison.OrdinalIgnoreCase))
                        {
                            update["adx_identity_username"] = username;
                        }

                        if (string.IsNullOrWhiteSpace(contact?.GetAttributeValue<string>("adx_identity_securitystamp")))
                        {
                            update["adx_identity_securitystamp"] = Guid.NewGuid().ToString("D");
                        }

                        if (contact?.GetAttributeValue<bool?>("adx_identity_logonenabled") != true)
                        {
                            update["adx_identity_logonenabled"] = true;
                        }

                        if (update.Attributes.Count > 0)
                        {
                            if (apply) svc.Update(update);
                            did.Add("+sign-in fields");
                        }
                    }
                }

                Console.WriteLine($"  {(did.Count == 0 ? "ok      " : "changed ")} {email}{(did.Count == 0 ? string.Empty : ": " + string.Join(", ", did))}");
            }
            catch (Exception ex)
            {
                failures.Add($"{email}: {ex.Message}");
                Console.Error.WriteLine($"  FAILED   {email}: {ex.Message}");
                Count("failed");

                // The role command lets the first grant through while no application role
                // exists anywhere, and refuses every one after it unless the account running
                // this holds one itself (PROD, 2026-10-01). Every later person would fail the
                // same way, so stop rather than log the same refusal 170 times.
                if (ex.Message.Contains("UNAUTHORIZED", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Stopped: the account running this has no application role here, so al_AssignUserRole " +
                                            "refuses it. Give it one (TEST: an al_userrolemapping row, role code 'Administrators'), then run again.");
                    return 1;
                }
            }
        }

        // 5. Adviser -> T&C Manager mappings, after everyone's contact exists. The sheet is
        // the source, so a mapping that points elsewhere is moved to the sheet's manager.
        foreach (var person in people.Where(p => p.TcManager.Length > 0))
        {
            if (skipped.Contains(person.Email) || skipped.Contains(person.TcManager))
            {
                Console.WriteLine($"  SKIPPED  mapping {person.Email} -> {person.TcManager}: one of them is not a user of this environment");
                Count("skipped - mapping with a non-user");
                continue;
            }

            try
            {
                var manager = FindContact(svc, person.TcManager);
                if (manager == Guid.Empty && apply)
                {
                    throw new InvalidOperationException($"no contact has the T&C Manager's email {person.TcManager}");
                }

                var existing = svc.RetrieveMultiple(new QueryExpression("al_advisermapping")
                {
                    ColumnSet = new ColumnSet("al_tcmanagerid"),
                    TopCount = 1,
                    Criteria = { Conditions = { new ConditionExpression("al_adviseremail", ConditionOperator.Equal, person.Email) } },
                }).Entities.FirstOrDefault();

                if (existing == null)
                {
                    if (apply)
                    {
                        svc.Create(new Entity("al_advisermapping")
                        {
                            ["al_name"] = person.Email,
                            ["al_adviseremail"] = person.Email,
                            ["al_tcmanagerid"] = new EntityReference("contact", manager),
                        });
                    }

                    Count("adviser mapping created");
                }
                else if (manager == Guid.Empty || existing.GetAttributeValue<EntityReference>("al_tcmanagerid")?.Id != manager)
                {
                    if (apply)
                    {
                        svc.Update(new Entity("al_advisermapping", existing.Id) { ["al_tcmanagerid"] = new EntityReference("contact", manager) });
                    }

                    Count("adviser mapping moved to the sheet's manager");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{person.Email} mapping: {ex.Message}");
                Console.Error.WriteLine($"  FAILED   {person.Email} mapping: {ex.Message}");
                Count("failed");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{people.Count} people. {(apply ? "Done" : "Would do")}:");
        foreach (var (what, n) in tally) Console.WriteLine($"  {n,4}  {what}");

        if (notUsers.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{notUsers.Count} are not users of this environment and were skipped entirely. Once " +
                              "they have a licence and are added, run this again. For example:");
            foreach (var p in notUsers)
            {
                Console.WriteLine($"  pac admin assign-user --environment {orgUrl} --user {p.Email} --role \"{BasicUser}\"");
                Console.WriteLine($"  pac admin assign-user --environment {orgUrl} --user {p.Email} --role \"{appUser}\"");
            }
        }

        return failures.Count > 0 ? 1 : 0;
    }

    private static Guid FindContact(IOrganizationService svc, string email)
    {
        var q = new QueryExpression("contact") { ColumnSet = new ColumnSet(false), TopCount = 1 };
        q.Criteria.AddCondition("emailaddress1", ConditionOperator.Equal, email);
        q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        return svc.RetrieveMultiple(q).Entities.FirstOrDefault()?.Id ?? Guid.Empty;
    }

    private static Entity? FindUser(IOrganizationService svc, string email)
    {
        var q = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("businessunitid", "azureactivedirectoryobjectid"),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions = { new ConditionExpression("isdisabled", ConditionOperator.Equal, false) },
                Filters =
                {
                    new FilterExpression(LogicalOperator.Or)
                    {
                        Conditions =
                        {
                            new ConditionExpression("internalemailaddress", ConditionOperator.Equal, email),
                            new ConditionExpression("domainname", ConditionOperator.Equal, email),
                        },
                    },
                },
            },
        };
        return svc.RetrieveMultiple(q).Entities.FirstOrDefault();
    }

    private static HashSet<string> HeldRoles(IOrganizationService svc, Guid userId)
    {
        var q = new QueryExpression("role") { ColumnSet = new ColumnSet("name") };
        var link = q.AddLink("systemuserroles", "roleid", "roleid");
        link.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
        return new HashSet<string>(
            svc.RetrieveMultiple(q).Entities.Select(r => r.GetAttributeValue<string>("name") ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<Guid> WebRolesOf(IOrganizationService svc, Guid contactId)
    {
        var fetch =
            "<fetch><entity name='powerpagecomponent_mspp_webrole_contact'>" +
              "<attribute name='powerpagecomponentid'/>" +
              "<filter><condition attribute='contactid' operator='eq' value='" + contactId.ToString("D") + "'/></filter>" +
            "</entity></fetch>";
        return new HashSet<Guid>(svc.RetrieveMultiple(new FetchExpression(fetch)).Entities
            .Select(e => e.GetAttributeValue<Guid>("powerpagecomponentid")));
    }

    // mspp_webrole does not answer a plain QueryExpression; FetchXML it is (as WebRoleId in Program).
    private static Guid WebRoleId(IOrganizationService svc, string roleName)
    {
        var fetch =
            "<fetch><entity name='mspp_webrole'><attribute name='mspp_webroleid'/>" +
              "<filter><condition attribute='mspp_name' operator='eq' value='" +
                System.Security.SecurityElement.Escape(roleName) + "'/></filter>" +
            "</entity></fetch>";
        return svc.RetrieveMultiple(new FetchExpression(fetch)).Entities.FirstOrDefault()?.Id ?? Guid.Empty;
    }

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }

        cells.Add(cell.ToString());
        return cells;
    }
}
