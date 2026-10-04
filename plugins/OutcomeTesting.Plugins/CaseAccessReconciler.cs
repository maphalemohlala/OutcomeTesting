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
        // The two teams and the AQS queue account are found by NAME, and the name carries the
        // product name: "Outcome Testing - Tax Team" in DEV and TEST, "OTIS - Tax Team" in PROD.
        // See ProductName.TaxTeam, AqsTeam and AqsQueueAccount.

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

        /// <summary>Reconciles one case, reading everything it needs afresh.</summary>
        public static CaseAccessChange Reconcile(IOrganizationService service, Guid caseId, DateTime now)
        {
            return Reconcile(service, caseId, now, new CaseAccessLookups(service));
        }

        /// <summary>
        /// Reconciles one case with <paramref name="lookups"/> that may already hold what does
        /// not depend on the case. A caller reconciling several cases in one transaction passes
        /// the same lookups to each, so the product name, the teams, the queue account and an
        /// adviser email's contact and supervisor are read once rather than once per case.
        /// </summary>
        public static CaseAccessChange Reconcile(IOrganizationService service, Guid caseId, DateTime now, CaseAccessLookups lookups)
        {
            if (lookups == null)
            {
                throw new ArgumentNullException(nameof(lookups));
            }

            // Lookups read through another service would read the teams and the adviser as
            // another principal, so the two must be the one service.
            if (!ReferenceEquals(lookups.Service, service))
            {
                throw new ArgumentException(
                    "The lookups were read through a different service from the one reconciling.", nameof(lookups));
            }

            var taxTeam = lookups.TaxTeam;
            var aqsTeam = lookups.AqsTeam;

            var outcomeCase = service.Retrieve(
                CaseEntity,
                caseId,
                new ColumnSet(
                    "al_casestatus", "al_reviewrouteid", "al_adviseremail", CaseAdviser.NameAttr,
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
                input.AqsQueueAccount = lookups.AqsQueueAccount;
            }

            var released = CaseAccess.IsReleased(input);
            if (released)
            {
                // The adviser email on the row just read: the same contact
                // Remediation.AdviserContact resolves, without reading the email again. The
                // same email sign-off routes by (CaseAdviser), so the supervisor who can read
                // the case is the one who can sign it off.
                var adviserEmail = outcomeCase.GetAttributeValue<string>("al_adviseremail");
                input.ResolvedAdviser = lookups.AdviserFor(adviserEmail);
                input.ResolvedSupervisor = lookups.SupervisorFor(adviserEmail);
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
            Share(service, target, taxTeam, decided.ShareWithTaxTeam);
            Share(service, target, aqsTeam, decided.ShareWithAqsTeam);

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
        internal static EntityReference SupervisorFor(IOrganizationService service, string adviserEmail)
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

        /// <summary>The AQS Team account whose contacts read the AQS queue. Throws if it is missing.</summary>
        public static EntityReference AqsQueueAccount(IOrganizationService service)
        {
            return FindByName(service, "account", ProductName.AqsQueueAccount(ProductName.Read(service)));
        }

        internal static EntityReference FindByName(IOrganizationService service, string entity, string name)
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

    /// <summary>
    /// What <see cref="CaseAccessReconciler"/> reads that does not depend on the case: the
    /// product name, the Tax and AQS teams, the AQS queue account, and the contact and T&amp;C
    /// supervisor an adviser email resolves to. Each is read on first use and kept, so one
    /// instance shared across the cases of a single transaction reads each once.
    ///
    /// Scoped to one transaction, never kept between plug-in runs: nothing here notices a
    /// contact, mapping or team changing, and inside one save nothing it reads changes. A
    /// missing team still refuses, on first use, exactly as a single-case reconcile does.
    ///
    /// Public only because the plug-in assembly is signed and carries no InternalsVisibleTo,
    /// so the tests cannot reach an internal type. Rules for a caller: build it on the same
    /// service you pass to <see cref="CaseAccessReconciler.Reconcile(IOrganizationService, Guid, DateTime, CaseAccessLookups)"/>
    /// (refused otherwise), keep it a local of one plug-in execution, and do not share it
    /// across a write to a contact's email or state, an adviser mapping, the teams or the
    /// product name - it would go on answering from before that write.
    /// </summary>
    public sealed class CaseAccessLookups
    {
        private readonly IOrganizationService _service;
        private readonly Dictionary<string, EntityReference> _advisers =
            new Dictionary<string, EntityReference>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EntityReference> _supervisors =
            new Dictionary<string, EntityReference>(StringComparer.OrdinalIgnoreCase);
        private string _product;
        private EntityReference _taxTeam;
        private EntityReference _aqsTeam;
        private EntityReference _queueAccount;

        public CaseAccessLookups(IOrganizationService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>The service everything here is read through.</summary>
        internal IOrganizationService Service
        {
            get { return _service; }
        }

        private string Product
        {
            get { return _product ?? (_product = ProductName.Read(_service)); }
        }

        public EntityReference TaxTeam
        {
            get { return _taxTeam ?? (_taxTeam = CaseAccessReconciler.FindByName(_service, "team", ProductName.TaxTeam(Product))); }
        }

        public EntityReference AqsTeam
        {
            get { return _aqsTeam ?? (_aqsTeam = CaseAccessReconciler.FindByName(_service, "team", ProductName.AqsTeam(Product))); }
        }

        public EntityReference AqsQueueAccount
        {
            get
            {
                return _queueAccount
                    ?? (_queueAccount = CaseAccessReconciler.FindByName(_service, "account", ProductName.AqsQueueAccount(Product)));
            }
        }

        /// <summary>
        /// The one active contact holding this adviser email, or null for none, two or a blank
        /// email (AD-228) - the same answer <see cref="Remediation.AdviserContact"/> gives.
        /// </summary>
        public EntityReference AdviserFor(string adviserEmail)
        {
            var key = CasePeople.Clean(adviserEmail) ?? string.Empty;
            EntityReference contact;
            if (!_advisers.TryGetValue(key, out contact))
            {
                var match = NotificationOutbox.MatchAdviser(_service, adviserEmail);
                contact = match.IsMatch ? match.Contact : null;
                _advisers[key] = contact;
            }

            return contact;
        }

        /// <summary>The T&amp;C Manager mapped to this adviser email, or null for none or two.</summary>
        public EntityReference SupervisorFor(string adviserEmail)
        {
            var key = CasePeople.Clean(adviserEmail) ?? string.Empty;
            EntityReference supervisor;
            if (!_supervisors.TryGetValue(key, out supervisor))
            {
                supervisor = CaseAccessReconciler.SupervisorFor(_service, CaseAdviser.EmailFor(adviserEmail));
                _supervisors[key] = supervisor;
            }

            return supervisor;
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
