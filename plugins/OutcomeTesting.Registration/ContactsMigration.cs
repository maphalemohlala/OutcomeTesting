using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Registration;

/// <summary>
/// One-off DEV migration onto the contact registry (spec
/// docs/superpowers/specs/2026-09-06-contacts-registry-design.md).
///
/// Four phases, each reported and each idempotent enough to re-run:
///   1. Remove the al_user rows that no contact matches.
///   2. Rewrite the adviser and paraplanner recorded on every case to the real people.
///   3. Allocate most cases, leaving a few in the queue.
///   4. Take the caller's own allocations through review to Closed, so the Trail Light
///      export has something to snapshot.
///
/// Allocation goes through the claim path — an al_caseassignment carrying only the case
/// and the contact — rather than writing the assignment out in full. ClaimCasePlugin then
/// resolves the systemuser, creates the review instance, stamps the checker onto the case
/// header and moves the status. Writing those by hand would produce rows that look
/// allocated without the review instance the submit path needs.
///
/// Option-set values are duplicated from ResponseRules, CaseLifecycle and OutcomeRules
/// because that assembly targets net462 and this tool targets net8.0, so it cannot be
/// referenced. Those types remain the source of truth.
/// </summary>
public static class ContactsMigration
{
    private const int CaseStatusQueued = 120910583;
    private const int CaseStatusClosed = 120910591;

    private const int ReviewTypeAqs = 120910201;
    private const int OwnerRoleAqsChecker = 120910101;

    // al_questionversion.al_responsetype
    private const int TypeText = 120910000;
    private const int TypeMultilineText = 120910001;
    private const int TypeDate = 120910002;
    private const int TypeSingleSelect = 120910003;
    private const int TypeMultiSelect = 120910004;
    private const int TypePassFail = 120910005;
    private const int TypePassFailInsufficient = 120910006;
    private const int TypeYesNo = 120910007;
    private const int TypeYesNoNa = 120910008;
    private const int TypeYesNoInsufficient = 120910009;
    private const int TypeGrade = 120910010;

    // al_response.al_answerchoice — the first value of each permitted list, which is the
    // clean answer in every scale: Pass, Yes, and the first root cause / tax reason.
    private const int ChoicePass = 120910300;
    private const int ChoiceYes = 120910305;
    private const int ChoiceFirstRootCause = 120910320;
    private const int ChoiceFirstTaxReason = 120910340;

    /// <summary>Cases to allocate; the rest stay in the queue on purpose.</summary>
    private const int AllocateCount = 9;

    private sealed record Person(Guid ContactId, string Name, string Email);

    public static int Run(IOrganizationService svc, string callerEmail)
    {
        var people = ActiveContacts(svc);
        if (people.Count == 0)
        {
            Console.Error.WriteLine("No active contact carries a work email. Nothing can be migrated onto.");
            return 1;
        }

        Console.WriteLine($"Registry: {people.Count} contact(s) — {string.Join(", ", people.Select(p => p.Name))}");
        Console.WriteLine();

        RemoveUnmatchedUsers(svc, people);
        var cases = AllCases(svc);
        RewriteCasePeople(svc, cases, people);
        var allocated = Allocate(svc, cases, people);
        CloseOwnCases(svc, allocated, callerEmail);

        Console.WriteLine();
        Console.WriteLine("Migration complete.");
        return 0;
    }

    private static List<Person> ActiveContacts(IOrganizationService svc)
    {
        var query = new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet("contactid", "fullname", "emailaddress1"),
            Criteria = new FilterExpression(),
            Orders = { new OrderExpression("emailaddress1", OrderType.Ascending) },
        };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition("emailaddress1", ConditionOperator.NotNull);

        return svc.RetrieveMultiple(query).Entities
            .Select(c => new Person(
                c.Id,
                c.GetAttributeValue<string>("fullname") ?? c.GetAttributeValue<string>("emailaddress1") ?? "",
                (c.GetAttributeValue<string>("emailaddress1") ?? "").Trim()))
            .Where(p => p.Email.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Phase 1. Deletes every al_user row whose work email no contact carries.
    ///
    /// Safe because authorisation never read this table: PermissionHelpers resolves roles
    /// from al_userrolemapping keyed on al_useremail and treats a missing registry row as
    /// permitted. The environment's only role mapping is for an email that never had an
    /// al_user row, which is the proof.
    /// </summary>
    private static void RemoveUnmatchedUsers(IOrganizationService svc, List<Person> people)
    {
        Console.WriteLine("1. Removing al_user rows no contact matches…");

        var known = new HashSet<string>(people.Select(p => p.Email), StringComparer.OrdinalIgnoreCase);
        var users = svc.RetrieveMultiple(new QueryExpression("al_user")
        {
            ColumnSet = new ColumnSet("al_name", "al_workemail"),
        }).Entities;

        var removed = 0;
        foreach (var user in users)
        {
            var email = (user.GetAttributeValue<string>("al_workemail") ?? "").Trim();
            if (email.Length > 0 && known.Contains(email))
            {
                Console.WriteLine($"   kept {user.GetAttributeValue<string>("al_name")} <{email}> — a contact matches.");
                continue;
            }

            svc.Delete("al_user", user.Id);
            Console.WriteLine($"   deleted {user.GetAttributeValue<string>("al_name")} <{(email.Length == 0 ? "(no email)" : email)}>");
            removed++;
        }

        Console.WriteLine($"   {removed} removed, {users.Count - removed} kept.");
        Console.WriteLine();
    }

    private static List<Entity> AllCases(IOrganizationService svc)
    {
        return svc.RetrieveMultiple(new QueryExpression("al_outcomecase")
        {
            ColumnSet = new ColumnSet("al_casereference", "al_casestatus", "al_reviewrouteid"),
            Orders = { new OrderExpression("al_casereference", OrderType.Ascending) },
        }).Entities.ToList();
    }

    /// <summary>
    /// Phase 2. Rewrites the adviser and paraplanner names to the real people.
    ///
    /// The People view groups by the name recorded on the case (AD-029 keeps those fields
    /// as text from the intake extract), so leaving the fictional names would keep listing
    /// people the registry no longer holds. Adviser and paraplanner are offset so no case
    /// has the same person in both positions.
    ///
    /// The checker is taken from the case's live allocation, and cleared where there is
    /// none. ClaimCasePlugin stamps it at allocation time, but a re-run skips cases already
    /// allocated, so deriving it here is what keeps the header true on a second pass.
    /// Clearing an unallocated case matters as much: the old value would otherwise keep a
    /// checker in the People view who is not in the registry and holds no allocation.
    /// </summary>
    private static void RewriteCasePeople(IOrganizationService svc, List<Entity> cases, List<Person> people)
    {
        Console.WriteLine("2. Rewriting the people recorded on every case…");

        for (var i = 0; i < cases.Count; i++)
        {
            var adviser = people[i % people.Count];
            var paraplanner = people[(i + 1) % people.Count];
            var checker = AssigneeOf(svc, cases[i].Id, people);

            svc.Update(new Entity("al_outcomecase", cases[i].Id)
            {
                ["al_advisername"] = adviser.Name,
                ["al_advisercode"] = Code("ADV", adviser),
                ["al_paraplanner"] = paraplanner.Name,
                ["al_paraplannercode"] = Code("PP", paraplanner),
                ["al_checkername"] = checker?.Name,
            });
        }

        Console.WriteLine($"   {cases.Count} case(s) updated.");
        Console.WriteLine();
    }

    /// <summary>A stable code per person, so the export's code columns are not blank.</summary>
    private static string Code(string prefix, Person person)
    {
        return prefix + "-" + person.ContactId.ToString("N")[..6].ToUpperInvariant();
    }

    /// <summary>
    /// Phase 3. Allocates the first <see cref="AllocateCount"/> cases round-robin, leaving
    /// the rest in the queue so the allocation screen has work to show.
    ///
    /// Each case is put onto the AQS-only route first. The route decides the discipline the
    /// claim opens, and an AQS review is what writes the Outcome carrying the advice quality
    /// grade on submit — a Tax-only case would close with both graded export columns blank,
    /// which AD-075 permits but proves nothing about the export.
    /// </summary>
    private static List<(Entity Case, Person Assignee)> Allocate(
        IOrganizationService svc, List<Entity> cases, List<Person> people)
    {
        Console.WriteLine("3. Allocating cases…");

        var route = AqsOnlyRoute(svc);
        var allocated = new List<(Entity, Person)>();

        for (var i = 0; i < cases.Count && allocated.Count < AllocateCount; i++)
        {
            var outcomeCase = cases[i];
            var reference = outcomeCase.GetAttributeValue<string>("al_casereference") ?? outcomeCase.Id.ToString("D");
            var assignee = people[i % people.Count];

            var status = outcomeCase.GetAttributeValue<OptionSetValue>("al_casestatus");
            if (status != null && status.Value == CaseStatusClosed)
            {
                Console.WriteLine($"   {reference} is Closed — left alone.");
                allocated.Add((outcomeCase, AssigneeOf(svc, outcomeCase.Id, people) ?? assignee));
                continue;
            }

            if (HasActiveAssignment(svc, outcomeCase.Id))
            {
                Console.WriteLine($"   {reference} already allocated — left alone.");
                allocated.Add((outcomeCase, AssigneeOf(svc, outcomeCase.Id, people) ?? assignee));
                continue;
            }

            try
            {
                var update = new Entity("al_outcomecase", outcomeCase.Id)
                {
                    ["al_casestatus"] = new OptionSetValue(CaseStatusQueued),
                };
                if (route != Guid.Empty)
                {
                    update["al_reviewrouteid"] = new EntityReference("al_reviewroute", route);
                }
                svc.Update(update);

                // Only the case and the contact: ClaimCasePlugin resolves and stamps the rest.
                svc.Create(new Entity("al_caseassignment")
                {
                    ["al_outcomecaseid"] = new EntityReference("al_outcomecase", outcomeCase.Id),
                    ["al_assignedcontactid"] = new EntityReference("contact", assignee.ContactId),
                });

                Console.WriteLine($"   {reference} -> {assignee.Name}");
                allocated.Add((outcomeCase, assignee));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"   {reference} FAILED: {Flatten(ex)}");
            }
        }

        var left = cases.Count - allocated.Count;
        Console.WriteLine($"   {allocated.Count} allocated, {left} left unassigned on purpose.");
        Console.WriteLine();
        return allocated;
    }

    /// <summary>Whether the case already holds a live allocation, so a re-run leaves it be.</summary>
    private static bool HasActiveAssignment(IOrganizationService svc, Guid caseId)
    {
        var query = new QueryExpression("al_caseassignment")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression(),
            TopCount = 1,
        };
        query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
        query.Criteria.AddCondition("al_isactive", ConditionOperator.Equal, true);

        return svc.RetrieveMultiple(query).Entities.Count > 0;
    }

    /// <summary>Who currently holds the case, so a re-run reports the real assignee.</summary>
    private static Person? AssigneeOf(IOrganizationService svc, Guid caseId, List<Person> people)
    {
        var query = new QueryExpression("al_caseassignment")
        {
            ColumnSet = new ColumnSet("al_assignedcontactid"),
            Criteria = new FilterExpression(),
            TopCount = 1,
        };
        query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
        query.Criteria.AddCondition("al_isactive", ConditionOperator.Equal, true);

        var found = svc.RetrieveMultiple(query).Entities;
        if (found.Count == 0) return null;

        var contact = found[0].GetAttributeValue<EntityReference>("al_assignedcontactid");
        return contact == null ? null : people.FirstOrDefault(p => p.ContactId == contact.Id);
    }

    private static Guid AqsOnlyRoute(IOrganizationService svc)
    {
        var query = new QueryExpression("al_reviewroute")
        {
            ColumnSet = new ColumnSet("al_reviewrouteid"),
            Criteria = new FilterExpression(),
            TopCount = 1,
        };
        query.Criteria.AddCondition("al_requiresaqsreview", ConditionOperator.Equal, true);
        query.Criteria.AddCondition("al_requirestaxreview", ConditionOperator.Equal, false);

        var found = svc.RetrieveMultiple(query).Entities;
        return found.Count == 0 ? Guid.Empty : found[0].Id;
    }

    /// <summary>
    /// Phase 4. Answers the mandatory questions and submits, which closes the case on a
    /// Pass (OutcomeRules.NextCaseStatusForAqs).
    ///
    /// Only the caller's own allocations: SubmitReviewPlugin.EnsureCaller lets the assigned
    /// checker submit and nobody else, so submitting another person's review would be
    /// refused — correctly.
    /// </summary>
    private static void CloseOwnCases(
        IOrganizationService svc, List<(Entity Case, Person Assignee)> allocated, string callerEmail)
    {
        Console.WriteLine("4. Taking the caller's own allocations through to Closed…");

        var mine = allocated
            .Where(a => string.Equals(a.Assignee.Email, callerEmail, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (mine.Count == 0)
        {
            Console.WriteLine($"   None of the allocations are to {callerEmail}, so none can be submitted here.");
            Console.WriteLine();
            return;
        }

        var closed = 0;
        foreach (var (outcomeCase, _) in mine)
        {
            var reference = outcomeCase.GetAttributeValue<string>("al_casereference") ?? outcomeCase.Id.ToString("D");
            try
            {
                var current = svc.Retrieve("al_outcomecase", outcomeCase.Id, new ColumnSet("al_casestatus"))
                    .GetAttributeValue<OptionSetValue>("al_casestatus");
                if (current != null && current.Value == CaseStatusClosed)
                {
                    Console.WriteLine($"   {reference}: already Closed — left alone.");
                    closed++;
                    continue;
                }

                var review = OpenReview(svc, outcomeCase.Id);
                if (review == null)
                {
                    Console.Error.WriteLine($"   {reference}: no unsubmitted review to close.");
                    continue;
                }

                var answered = AnswerMandatory(svc, review);
                svc.Execute(new OrganizationRequest("al_SubmitReview")
                {
                    ["TargetId"] = review.Id.ToString("D"),
                    ["IdempotencyKey"] = "MIGRATE-SUBMIT-" + review.Id.ToString("N"),
                });

                var status = svc.Retrieve("al_outcomecase", outcomeCase.Id, new ColumnSet("al_casestatus"))
                    .GetAttributeValue<OptionSetValue>("al_casestatus");
                var isClosed = status != null && status.Value == CaseStatusClosed;
                Console.WriteLine(
                    $"   {reference}: answered {answered} question(s), submitted, case is {(isClosed ? "Closed" : "not Closed")}.");
                if (isClosed) closed++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"   {reference} FAILED: {Flatten(ex)}");
            }
        }

        Console.WriteLine($"   {closed} case(s) closed and available to the Trail Light export.");
        Console.WriteLine();
    }

    private static Entity? OpenReview(IOrganizationService svc, Guid caseId)
    {
        var query = new QueryExpression("al_reviewinstance")
        {
            ColumnSet = new ColumnSet("al_checklistversionid", "al_reviewtype"),
            Criteria = new FilterExpression(),
            Orders = { new OrderExpression("al_sequence", OrderType.Ascending) },
            TopCount = 1,
        };
        query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
        query.Criteria.AddCondition("al_submittedon", ConditionOperator.Null);

        var found = svc.RetrieveMultiple(query).Entities;
        return found.Count == 0 ? null : found[0];
    }

    /// <summary>
    /// Writes a clean answer to every mandatory question the review's discipline owns,
    /// mirroring SubmitReviewPlugin.EnsureMandatoryQuestionsAnswered so the same set is
    /// covered. The answer is the first permitted value of each scale, which is Pass or
    /// Yes — the case closes rather than routing into remediation.
    /// </summary>
    private static int AnswerMandatory(IOrganizationService svc, Entity review)
    {
        var checklistVersion = review.GetAttributeValue<EntityReference>("al_checklistversionid");
        if (checklistVersion == null) return 0;

        var reviewType = review.GetAttributeValue<OptionSetValue>("al_reviewtype");
        var ownerRole = reviewType != null && reviewType.Value == ReviewTypeAqs
            ? OwnerRoleAqsChecker
            : 120910100; // OwnerRoleTaxTeam

        var query = new QueryExpression("al_questionversion")
        {
            ColumnSet = new ColumnSet("al_questionversionid", "al_responsetype"),
            Criteria = new FilterExpression(),
        };
        query.Criteria.AddCondition("al_ismandatory", ConditionOperator.Equal, true);

        var questionLink = query.AddLink("al_question", "al_questionid", "al_questionid");
        var sectionLink = questionLink.AddLink("al_section", "al_sectionid", "al_sectionid");
        sectionLink.LinkCriteria.AddCondition("al_checklistversionid", ConditionOperator.Equal, checklistVersion.Id);
        sectionLink.LinkCriteria.AddCondition("al_ownerrole", ConditionOperator.Equal, ownerRole);

        var written = 0;
        foreach (var version in svc.RetrieveMultiple(query).Entities)
        {
            var responseType = version.GetAttributeValue<OptionSetValue>("al_responsetype");
            if (responseType == null) continue;

            var code = "RSP-" + review.Id.ToString("N")[..12] + "-" + version.Id.ToString("N")[..12];
            var response = new Entity("al_response")
            {
                ["al_name"] = "Seeded answer",
                ["al_responsecode"] = code,
                ["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", review.Id),
                ["al_questionversionid"] = new EntityReference("al_questionversion", version.Id),
            };
            ApplyAnswer(response, responseType.Value);

            try
            {
                svc.Create(response);
                written++;
            }
            catch (Exception)
            {
                // Already answered on a re-run: the alternate key collides, which is the
                // idempotent outcome, not a failure.
            }
        }

        return written;
    }

    private static void ApplyAnswer(Entity response, int responseType)
    {
        switch (responseType)
        {
            case TypeText:
            case TypeMultilineText:
                response["al_answertext"] = "Seeded for the DEV export proof.";
                break;
            case TypeDate:
                response["al_answerdate"] = DateTime.UtcNow.Date;
                break;
            case TypeMultiSelect:
                response["al_answerchoices"] = new OptionSetValueCollection
                {
                    new OptionSetValue(ChoiceFirstTaxReason),
                };
                break;
            case TypeSingleSelect:
                response["al_answerchoice"] = new OptionSetValue(ChoiceFirstRootCause);
                break;
            case TypeYesNo:
            case TypeYesNoNa:
            case TypeYesNoInsufficient:
                response["al_answerchoice"] = new OptionSetValue(ChoiceYes);
                break;
            case TypePassFail:
            case TypePassFailInsufficient:
            case TypeGrade:
                response["al_answerchoice"] = new OptionSetValue(ChoicePass);
                break;
            default:
                response["al_answertext"] = "Seeded for the DEV export proof.";
                break;
        }
    }

    private static string Flatten(Exception ex)
    {
        var message = ex.Message.Replace("\r", " ").Replace("\n", " ");
        return message.Length > 300 ? message[..300] + "…" : message;
    }
}
