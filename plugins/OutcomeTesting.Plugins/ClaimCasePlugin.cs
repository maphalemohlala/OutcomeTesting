using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Portal self-claim: a checker picks up an unassigned case from the shared queue
    /// (AD-076, extending the AD-040 queue model, BR-003). Registered as a synchronous
    /// pre-operation step on Create of <c>al_caseassignment</c>.
    ///
    /// A create rather than a trigger column, for the AD-074 reason: the Portals Web API
    /// supports create, and a separate table carries a separate permission. The permission
    /// here is <b>Contact-scoped</b> through <c>contact_al_caseassignment</c>, so the row
    /// the browser posts must point at the signed-in contact. That matters because
    /// authorization cannot be a caller check — a Power Pages write reaches Dataverse under
    /// the site's application user, so <c>InitiatingUserId</c> is not the checker (AD-053).
    ///
    /// The same table is written by the AD-072 <c>al_AssignCase</c> command, which always
    /// sets <c>al_assigneduserid</c> itself. A row arriving without one is therefore a
    /// portal claim, and only those rows are stamped here; the command's own creates pass
    /// straight through.
    ///
    /// The browser sends the case and the contact. Everything else — the business code, the
    /// Dataverse user behind the contact, the review instance, the case status and the
    /// checker name on the case header — is resolved and written here, so a claim cannot be
    /// backdated, forged onto work someone else already holds, or pointed at a case that is
    /// not in the queue.
    /// </summary>
    public class ClaimCasePlugin : PluginBase
    {
        private const string AssignmentEntity = "al_caseassignment";
        private const string CaseEntity = "al_outcomecase";
        private const string ReviewEntity = "al_reviewinstance";
        private const string RouteEntity = "al_reviewroute";
        private const string ChecklistVersionEntity = "al_checklistversion";
        private const string ContactEntity = "contact";
        private const string UserEntity = "systemuser";

        private const string CaseLookup = "al_outcomecaseid";
        private const string AssignedUserAttr = "al_assigneduserid";
        private const string AssignedContactAttr = "al_assignedcontactid";
        private const string AssignedOnAttr = "al_assignedon";
        private const string IsActiveAttr = "al_isactive";
        private const string AssignmentReasonAttr = "al_assignmentreason";
        private const string AssignmentCodeAttr = "al_caseassignmentcode";
        private const string CheckerNameAttr = "al_checkername";
        private const string CaseStatusAttr = "al_casestatus";
        private const string CaseRouteAttr = "al_reviewrouteid";
        private const string RouteRequiresTaxAttr = "al_requirestaxreview";
        private const string RouteRequiresAqsAttr = "al_requiresaqsreview";
        private const string ReviewTypeAttr = "al_reviewtype";
        private const string ReviewStatusAttr = "al_reviewstatus";
        private const string ChecklistVersionAttr = "al_checklistversionid";
        private const string EffectiveFromAttr = "al_effectivefrom";
        private const string EffectiveToAttr = "al_effectiveto";
        private const string SequenceAttr = "al_sequence";
        private const string SubmittedOnAttr = "al_submittedon";

        // al_reviewinstance.al_reviewstatus
        private const int ReviewAssigned = 120910210;

        // Shares the AD-072 allocation audit value: a self-claim is an allocation of the
        // case, and the details line records that the checker took it themselves.
        private const int CommandAssignCase = 120910751;

        public ClaimCasePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ClaimCasePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;

            // The system service throughout: the portal application user has no privilege
            // on the review instance, the case header or the audit table, and the claim
            // must still write all three.
            var service = localPluginContext.PluginUserService;

            object target;
            if (!context.InputParameters.TryGetValue("Target", out target))
            {
                return;
            }

            var assignment = target as Entity;
            if (assignment == null
                || !string.Equals(assignment.LogicalName, AssignmentEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (assignment.GetAttributeValue<EntityReference>(AssignedUserAttr) != null)
            {
                return;
            }

            var caseRef = assignment.GetAttributeValue<EntityReference>(CaseLookup);
            if (caseRef == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A claim must name the case it is for.");
            }

            var contactRef = assignment.GetAttributeValue<EntityReference>(AssignedContactAttr);
            if (contactRef == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A claim must name the checker taking the case.");
            }

            var contact = service.Retrieve(ContactEntity, contactRef.Id, new ColumnSet("fullname", "emailaddress1", "statecode"));
            var email = contact.GetAttributeValue<string>("emailaddress1");
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Your portal contact has no work email recorded, so the case cannot be allocated to your Dataverse account.");
            }

            // AD-010: one work email resolves both identity systems. Reused rather than
            // reimplemented so a claim and a manager allocation cannot drift apart on what
            // counts as a valid assignee.
            var assignee = AssignCasePlugin.ResolveAssignee(service, email.Trim());

            var outcomeCase = service.Retrieve(
                CaseEntity, caseRef.Id, new ColumnSet("al_casereference", CaseStatusAttr, CaseRouteAttr));

            EnsureQueued(outcomeCase);

            var review = ResolveOrCreateReview(service, outcomeCase, assignee);

            // A case returns to Queued for the BR-004 Tax-then-AQS handoff still carrying
            // the Tax leg's active assignment, so a claim that skipped this would leave two
            // rows both claiming to be current.
            AssignCasePlugin.ReleasePriorAssignments(service, caseRef.Id);

            var caseReference = outcomeCase.GetAttributeValue<string>("al_casereference");
            var checkerName = contact.GetAttributeValue<string>("fullname") ?? assignee.UserName;

            assignment["al_name"] = AssignCasePlugin.BuildAssignmentName(caseReference, checkerName);
            assignment[AssignmentCodeAttr] = AssignCasePlugin.BuildAssignmentCode(caseRef.Id, review.Id, assignee.UserId);
            assignment[AssignedUserAttr] = new EntityReference(UserEntity, assignee.UserId);
            assignment[AssignedOnAttr] = DateTime.UtcNow;
            assignment[IsActiveAttr] = true;

            if (string.IsNullOrWhiteSpace(assignment.GetAttributeValue<string>(AssignmentReasonAttr)))
            {
                assignment[AssignmentReasonAttr] = "Self-assigned from the portal queue.";
            }

            StampReview(service, review.Id, assignee);

            // The V8 case header's own checker field, which is what the worklist and the
            // case detail read. Without it the case reads as unchecked to everyone outside
            // the assignment history.
            service.Update(new Entity(CaseEntity, caseRef.Id)
            {
                [CheckerNameAttr] = checkerName,
            });

            CaseTransitions.MoveThrough(service, caseRef.Id, CaseLifecycle.Assigned);

            CommandHelpers.WriteAuditEvent(
                service,
                CommandAssignCase,
                "ClaimCase " + caseRef.Id.ToString("D"),
                CaseEntity,
                caseRef.Id,
                null,
                "Claimed review " + review.Id.ToString("D") + " from the portal queue for " + checkerName
                    + " <" + email.Trim() + ">",
                assignment.GetAttributeValue<string>(AssignmentCodeAttr),
                context);
        }

        /// <summary>
        /// A case may only be claimed out of the shared queue. Stated as an explicit
        /// requirement rather than left to <see cref="CaseLifecycle"/>, which treats
        /// re-stating the current status as allowed — so an already Assigned case would
        /// otherwise pass the transition check and be claimed a second time.
        /// </summary>
        public static void EnsureQueued(Entity outcomeCase)
        {
            var status = outcomeCase.GetAttributeValue<OptionSetValue>(CaseStatusAttr);
            if (status != null && status.Value == CaseLifecycle.Queued)
            {
                return;
            }

            var name = status == null ? "(none)" : CaseLifecycle.NameOf(status.Value);
            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix +
                "This case is " + name + ", not in the queue, so it cannot be picked up. Someone may have taken it already.");
        }

        /// <summary>
        /// The check this claim is for: the earliest unsubmitted review instance on the
        /// case, or a new one for the discipline the route says comes next.
        ///
        /// Creating it here is what makes the queue self-service. Nothing else in the
        /// solution creates a review instance, so a queued case generally has none, and a
        /// claim that could only ever attach to an existing instance would refuse every
        /// case in the queue. The discipline is taken from the route rather than from the
        /// page the checker was on, so BR-004's ordering — Tax before AQS — is decided
        /// server-side.
        /// </summary>
        private static Entity ResolveOrCreateReview(
            IOrganizationService service, Entity outcomeCase, AssignCasePlugin.Assignee assignee)
        {
            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(ReviewTypeAttr, AssignedContactAttr),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0),
                        new ConditionExpression(CaseLookup, ConditionOperator.Equal, outcomeCase.Id),
                        new ConditionExpression(SubmittedOnAttr, ConditionOperator.Null),
                    },
                },
                Orders = { new OrderExpression(SequenceAttr, OrderType.Ascending) },
                TopCount = 1,
            };

            var existing = service.RetrieveMultiple(query).Entities;
            if (existing.Count > 0)
            {
                // The narrow race two checkers can hit: both read a queued case, one wins.
                // The loser is told rather than silently made a co-assignee.
                if (existing[0].GetAttributeValue<EntityReference>(AssignedContactAttr) != null)
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix +
                        "Another checker has already started checks on this case.");
                }

                return existing[0];
            }

            var reviewType = NextDiscipline(service, outcomeCase);
            var isTax = reviewType == ResponseRules.ReviewTypeTax;
            var label = isTax ? "Tax" : "AQS";

            var review = new Entity(ReviewEntity)
            {
                ["al_name"] = label + " check",
                // Deterministic per (case, discipline) so a double-click collides on the
                // al_reviewinstancecode alternate key instead of opening two checks.
                ["al_reviewinstancecode"] = "RI-" + label.ToUpperInvariant() + "-" + outcomeCase.Id.ToString("N").Substring(0, 20),
                [CaseLookup] = new EntityReference(CaseEntity, outcomeCase.Id),
                [ChecklistVersionAttr] = new EntityReference(ChecklistVersionEntity, ResolveChecklistVersion(service)),
                [ReviewTypeAttr] = new OptionSetValue(reviewType),
                [SequenceAttr] = isTax ? 1 : 2,
                [ReviewStatusAttr] = new OptionSetValue(ReviewAssigned),
                ["al_startedon"] = DateTime.UtcNow,
                [AssignedContactAttr] = new EntityReference(ContactEntity, assignee.ContactId),
                ["ownerid"] = new EntityReference(UserEntity, assignee.UserId),
            };

            review.Id = service.Create(review);
            return review;
        }

        /// <summary>
        /// The checklist version in force today (BR-013, AD-004). A review instance without
        /// one renders no questions, so a claim that could not resolve it is refused rather
        /// than opening an empty check.
        ///
        /// The window is applied in memory rather than in the query: there are a handful of
        /// versions, and a date-range condition would put the answer at the mercy of how the
        /// platform compares a date-only column to a UTC timestamp.
        /// </summary>
        private static Guid ResolveChecklistVersion(IOrganizationService service)
        {
            var query = new QueryExpression(ChecklistVersionEntity)
            {
                ColumnSet = new ColumnSet(EffectiveFromAttr, EffectiveToAttr),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("statecode", ConditionOperator.Equal, 0) },
                },
                Orders = { new OrderExpression(EffectiveFromAttr, OrderType.Descending) },
            };

            var today = DateTime.UtcNow.Date;
            foreach (var version in service.RetrieveMultiple(query).Entities)
            {
                var from = version.GetAttributeValue<DateTime?>(EffectiveFromAttr);
                var to = version.GetAttributeValue<DateTime?>(EffectiveToAttr);

                if ((!from.HasValue || from.Value.Date <= today) && (!to.HasValue || to.Value.Date >= today))
                {
                    return version.Id;
                }
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix +
                "No checklist version is in force, so a check cannot be opened. Ask an administrator to publish one (BR-013).");
        }

        /// <summary>
        /// Which discipline a case with no review instance yet owes. Tax first where the
        /// route requires it (BR-004); AQS where the route requires only that. A case whose
        /// route requires neither, or that carries no route at all, is refused rather than
        /// guessed at — routing is <c>UpdateCaseDetailsPlugin.DeriveRoute</c>'s job.
        /// </summary>
        public static int NextDiscipline(IOrganizationService service, Entity outcomeCase)
        {
            var routeRef = outcomeCase.GetAttributeValue<EntityReference>(CaseRouteAttr);
            if (routeRef == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "This case has no review route, so which check is due cannot be determined. Ask a manager to set the route (BR-004).");
            }

            var route = service.Retrieve(
                RouteEntity, routeRef.Id, new ColumnSet(RouteRequiresTaxAttr, RouteRequiresAqsAttr));

            if (route.GetAttributeValue<bool?>(RouteRequiresTaxAttr) ?? false)
            {
                return ResponseRules.ReviewTypeTax;
            }

            if (route.GetAttributeValue<bool?>(RouteRequiresAqsAttr) ?? false)
            {
                return ResponseRules.ReviewTypeAqs;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix +
                "This case's review route requires no check, so there is nothing to pick up.");
        }

        /// <summary>
        /// Points the review at the claimant in both identity systems, the same pair
        /// <c>al_AssignCase</c> writes (AD-072): the contact is what Power Pages resolves
        /// through, the owner is what the Code App reads.
        /// </summary>
        private static void StampReview(IOrganizationService service, Guid reviewId, AssignCasePlugin.Assignee assignee)
        {
            service.Update(new Entity(ReviewEntity, reviewId)
            {
                [AssignedContactAttr] = new EntityReference(ContactEntity, assignee.ContactId),
                [ReviewStatusAttr] = new OptionSetValue(ReviewAssigned),
                ["ownerid"] = new EntityReference(UserEntity, assignee.UserId),
            });
        }
    }
}
