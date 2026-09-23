using System;
using System.Collections.Generic;
using Microsoft.Crm.Sdk;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Makes a case's access columns and team shares match what <see cref="CaseAccess"/>
    /// says they should be (AD-218). Writes only what differs, so it is safe to run on every
    /// write that could change the answer, and safe to run again as a backfill.
    ///
    /// Run as SYSTEM. Portal writes arrive as the site's application user (AD-053), and
    /// sharing needs a privilege no person using the product should hold; the bookkeeping is
    /// derived from a write the calling command has already authorised.
    /// </summary>
    public static class CaseAccessReconciler
    {
        public const string TaxTeamName = "Outcome Testing - Tax Team";
        public const string AqsTeamName = "Outcome Testing - AQS Team";
        public const string AqsQueueAccountName = "Outcome Testing - AQS Team";

        public const string TaxCheckerAttr = "al_taxcheckercontactid";
        public const string AqsCheckerAttr = "al_aqscheckercontactid";
        public const string QueueAccountAttr = "al_aqsqueueaccountid";
        public const string QueuedOnAttr = "al_aqsqueuedon";
        public const string AdviserAttr = "al_advisercontactid";
        public const string SupervisorAttr = "al_tcsupervisorcontactid";

        private const string CaseEntity = "al_outcomecase";

        /// <summary>
        /// Read, write, append, append-to and assign. <c>al_AssignCase</c> writes the review
        /// instance and changes its owner through the caller's own service, so a manager with a
        /// read-only share could see their team's work and not allocate it (spec amendment 7).
        /// </summary>
        public const AccessRights ShareMask =
            AccessRights.ReadAccess | AccessRights.WriteAccess | AccessRights.AppendAccess
            | AccessRights.AppendToAccess | AccessRights.AssignAccess;

        public static CaseAccessChange Reconcile(IOrganizationService service, Guid caseId, DateTime now)
        {
            var teams = new Dictionary<string, EntityReference>
            {
                { TaxTeamName, FindByName(service, "team", TaxTeamName) },
                { AqsTeamName, FindByName(service, "team", AqsTeamName) },
            };

            var outcomeCase = service.Retrieve(
                CaseEntity,
                caseId,
                new ColumnSet(
                    "al_casestatus", "al_reviewrouteid", "al_adviseremail",
                    TaxCheckerAttr, AqsCheckerAttr, QueueAccountAttr, QueuedOnAttr, AdviserAttr, SupervisorAttr));

            var status = outcomeCase.GetAttributeValue<OptionSetValue>("al_casestatus");
            var input = new CaseAccessInput
            {
                CaseStatus = status == null ? (int?)null : status.Value,
                Reviews = ReadReviews(service, caseId),
                HasRemediation = HasRemediation(service, caseId),
                CurrentQueuedOn = outcomeCase.GetAttributeValue<DateTime?>(QueuedOnAttr),
                Now = now,
            };

            var routeRef = outcomeCase.GetAttributeValue<EntityReference>("al_reviewrouteid");
            if (routeRef != null)
            {
                var route = service.Retrieve("al_reviewroute", routeRef.Id, new ColumnSet("al_requirestaxreview", "al_requiresaqsreview"));
                input.RouteRequiresTax = route.GetAttributeValue<bool>("al_requirestaxreview");
                input.RouteRequiresAqs = route.GetAttributeValue<bool>("al_requiresaqsreview");
            }

            // Looked up only when used, so a case that never reaches the queue or remediation
            // costs no extra reads and needs no account to exist.
            if (CaseAccess.InAqsQueue(input))
            {
                input.AqsQueueAccount = FindByName(service, "account", AqsQueueAccountName);
            }

            var released = CaseAccess.IsReleased(input);
            if (released)
            {
                var caseRef = new EntityReference(CaseEntity, caseId);
                input.ResolvedAdviser = Remediation.AdviserContact(service, caseRef);
                input.ResolvedSupervisor = SupervisorFor(service, outcomeCase.GetAttributeValue<string>("al_adviseremail"));
            }

            var decided = CaseAccess.Decide(input);

            var update = new Entity(CaseEntity, caseId);
            var changed = new List<string>();
            SetIfChanged(update, changed, outcomeCase, TaxCheckerAttr, decided.TaxChecker);
            SetIfChanged(update, changed, outcomeCase, AqsCheckerAttr, decided.AqsChecker);
            SetIfChanged(update, changed, outcomeCase, QueueAccountAttr, decided.QueueAccount);
            SetIfChanged(update, changed, outcomeCase, AdviserAttr, decided.Adviser);
            SetIfChanged(update, changed, outcomeCase, SupervisorAttr, decided.Supervisor);

            var queuedOn = outcomeCase.GetAttributeValue<DateTime?>(QueuedOnAttr);
            if (queuedOn != decided.QueuedOn)
            {
                update[QueuedOnAttr] = decided.QueuedOn;
                changed.Add(QueuedOnAttr);
            }

            if (changed.Count > 0)
            {
                service.Update(update);
            }

            var target = new EntityReference(CaseEntity, caseId);
            Share(service, target, teams[TaxTeamName], decided.ShareWithTaxTeam);
            Share(service, target, teams[AqsTeamName], decided.ShareWithAqsTeam);

            return new CaseAccessChange
            {
                ChangedColumns = changed,
                Released = released,
                AdviserUnmatched = released && decided.Adviser == null,
                SupervisorUnmatched = released && decided.Supervisor == null,
            };
        }

        private static List<ReviewFact> ReadReviews(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression("al_reviewinstance")
            {
                ColumnSet = new ColumnSet("al_reviewtype", "al_assignedcontactid", "al_submittedon", "al_sequence", "statecode"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);

            var facts = new List<ReviewFact>();
            foreach (var row in CommandHelpers.RetrieveAll(service, query))
            {
                var type = row.GetAttributeValue<OptionSetValue>("al_reviewtype");
                var contact = row.GetAttributeValue<EntityReference>("al_assignedcontactid");
                var state = row.GetAttributeValue<OptionSetValue>("statecode");
                facts.Add(new ReviewFact
                {
                    ReviewType = type == null ? 0 : type.Value,
                    AssignedContactId = contact == null ? (Guid?)null : contact.Id,
                    Submitted = row.GetAttributeValue<DateTime?>("al_submittedon").HasValue,
                    Active = state == null || state.Value == 0,
                    Sequence = row.GetAttributeValue<int>("al_sequence"),
                });
            }

            return facts;
        }

        private static bool HasRemediation(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression("al_remediationaction")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        /// <summary>
        /// The T&amp;C Manager mapped to this adviser (AD-162), on the same email key the
        /// portal's sign-off gate reads. None or two means nobody: never guessed.
        /// </summary>
        private static EntityReference SupervisorFor(IOrganizationService service, string adviserEmail)
        {
            if (string.IsNullOrWhiteSpace(adviserEmail))
            {
                return null;
            }

            var query = new QueryExpression("al_advisermapping")
            {
                ColumnSet = new ColumnSet("al_tcmanagerid"),
                TopCount = 2,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_adviseremail", ConditionOperator.Equal, adviserEmail.Trim());
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var rows = service.RetrieveMultiple(query).Entities;
            return rows.Count == 1 ? rows[0].GetAttributeValue<EntityReference>("al_tcmanagerid") : null;
        }

        private static EntityReference FindByName(IOrganizationService service, string entity, string name)
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 2,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, name);

            var rows = service.RetrieveMultiple(query).Entities;
            if (rows.Count != 1)
            {
                // Refused rather than skipped: a case the reconciler silently could not share
                // or queue is a case nobody can see, and nothing would say why.
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix
                    + "Expected exactly one " + entity + " named \"" + name + "\" and found " + rows.Count
                    + ". Run the registration tool's ensureaccessprincipals verb for this environment.");
            }

            return rows[0].ToEntityReference();
        }

        private static void SetIfChanged(
            Entity update, List<string> changed, Entity current, string attribute, EntityReference wanted)
        {
            var existing = current.GetAttributeValue<EntityReference>(attribute);
            var same = existing == null
                ? wanted == null
                : wanted != null && existing.Id == wanted.Id;
            if (same)
            {
                return;
            }

            update[attribute] = wanted;
            changed.Add(attribute);
        }

        private static void Share(IOrganizationService service, EntityReference target, EntityReference team, bool wanted)
        {
            if (wanted)
            {
                service.Execute(new GrantAccessRequest
                {
                    Target = target,
                    PrincipalAccess = new PrincipalAccess { Principal = team, AccessMask = ShareMask },
                });
                return;
            }

            // Revoking a principal the row was never shared with is a no-op on the platform,
            // which is what lets this be idempotent without first reading the shares.
            service.Execute(new RevokeAccessRequest { Target = target, Revokee = team });
        }
    }

    public sealed class CaseAccessChange
    {
        public List<string> ChangedColumns { get; set; } = new List<string>();

        public bool Released { get; set; }

        public bool AdviserUnmatched { get; set; }

        public bool SupervisorUnmatched { get; set; }
    }
}
