using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command AssignCase (AD-003, OD-029). Registered against the Custom API
    /// message <c>al_AssignCase</c>. A team lead or manager allocates a queued case to a
    /// named member of the review team (BR-003, AD-040), which is the missing link in the
    /// BR-004 Tax-then-AQS route: without it a two-discipline case parks at Queued and the
    /// only way onward is editing <c>al_casestatus</c> directly, bypassing both the
    /// assignment history and the audit trail.
    ///
    /// The command writes a new <c>al_caseassignment</c> row and releases the previous one
    /// rather than editing it, so reassignment never loses the trail (BR-003, BR-012). It
    /// also stamps the review instance, because the two front ends resolve identity
    /// differently: the Code App reads <c>ownerid</c> (a systemuser) and Power Pages can
    /// only resolve through Contact relationships (AD-047). Both are derived from one work
    /// email, the canonical cross-system identifier (OD-003, AD-010).
    ///
    /// Not addressed, and deliberately so: OD-029 records that per-team scoping is unsolved.
    /// <c>al_assignedteam</c> is free text and Senior Checker carries no team affiliation,
    /// so nothing here stops a Tax lead allocating an AQS review. Inventing a team model to
    /// close that would be inventing a business rule.
    /// </summary>
    public class AssignCasePlugin : PluginBase
    {
        private const string InTargetId = "TargetId";
        private const string InAssigneeEmail = "AssigneeEmail";
        private const string InReviewInstanceId = "ReviewInstanceId";
        private const string InTeam = "Team";
        private const string InReason = "Reason";
        private const string InExpectedRowVersion = "ExpectedRowVersion";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutAssignmentId = "AssignmentId";
        private const string OutReviewInstanceId = "ReviewInstanceId";
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string CaseEntity = "al_outcomecase";
        private const string AssignmentEntity = "al_caseassignment";
        private const string ReviewEntity = "al_reviewinstance";
        private const string UserEntity = "systemuser";
        private const string ContactEntity = "contact";

        private const string AssignedUserAttr = "al_assigneduserid";
        private const string AssignedContactAttr = "al_assignedcontactid";
        private const string AssignedTeamAttr = "al_assignedteam";
        private const string AssignedOnAttr = "al_assignedon";
        private const string ReleasedOnAttr = "al_releasedon";
        private const string IsActiveAttr = "al_isactive";
        private const string AssignmentReasonAttr = "al_assignmentreason";
        private const string AssignmentCodeAttr = "al_caseassignmentcode";
        private const string CheckerNameAttr = "al_checkername";
        private const string CaseRefAttr = "al_casereference";
        private const string ReviewStatusAttr = "al_reviewstatus";
        private const string ReviewTypeAttr = "al_reviewtype";
        private const string SequenceAttr = "al_sequence";
        private const string SubmittedOnAttr = "al_submittedon";

        // al_reviewinstance.al_reviewstatus
        private const int ReviewAssigned = 120910210;

        // Reuses the audit command value reserved for this command when the option set was authored.
        private const int CommandAssignCase = 120910751;

        public AssignCasePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AssignCasePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService; // caller privileges gate the writes
            var systemService = localPluginContext.PluginUserService;   // audit + identity resolution

            var caseId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var assigneeEmail = CommandHelpers.GetRequiredString(context, InAssigneeEmail).Trim();
            var reason = CommandHelpers.GetOptionalString(context, InReason);
            var team = CommandHelpers.GetOptionalString(context, InTeam);
            var expectedRowVersion = CommandHelpers.GetOptionalString(context, InExpectedRowVersion);
            var requestedReviewId = ParseOptionalGuid(context, InReviewInstanceId);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandAssignCase);
            if (existingAudit != null)
            {
                SetResponse(context, null, requestedReviewId, "AlreadyAssigned", existingAudit.Id, false);
                return;
            }

            PermissionHelpers.EnsureAppPermission(systemService, context, "command.assign", PermissionHelpers.AccessEdit);

            // Retrieved through the caller's service so read privilege on the case is part of the gate.
            var outcomeCase = userService.Retrieve(CaseEntity, caseId, new ColumnSet(CaseRefAttr, "al_reviewrouteid"));
            var caseReference = outcomeCase.GetAttributeValue<string>(CaseRefAttr);

            // Identity resolution happens before anything is written: a half-assigned case -
            // a Code App owner with no portal contact - would look allocated and leave the
            // reviewer unable to open it.
            var assignee = ResolveAssignee(systemService, assigneeEmail);

            var review = ResolveReviewInstance(userService, systemService, outcomeCase, requestedReviewId, assignee);
            var reviewId = review.Id;

            ReleasePriorAssignments(userService, caseId);

            var assignmentId = AllocateAssignment(
                userService, caseId, reviewId, assignee, caseReference, team, reason, context.CorrelationId);

            StampReviewInstance(userService, reviewId, assignee, expectedRowVersion);

            // The checklist header and the case detail read this, not the assignment row,
            // so an allocation that did not write it left the case reading as unchecked to
            // everyone outside the assignment history. ClaimCasePlugin has always stamped
            // it on the portal self-claim; the app's Allocate never did, which is why the
            // case edit form carried a Checker field for a manager to type the same name in
            // by hand - and why editing that name was mistaken for allocating the check
            // (2026-09-09). The allocation is the one thing that knows who holds the check,
            // so it is what says so. The field was removed from the form on 2026-09-10.
            StampCheckerName(userService, caseId, assignee.UserName);

            // Queued -> Assigned, refused by AD-057 if the case is not somewhere the
            // lifecycle allows it from. Deliberately after the assignment row exists: a case
            // that reads Assigned with no assignment behind it is the state BR-003 forbids.
            CaseTransitions.MoveThrough(systemService, caseId, CaseLifecycle.Assigned);

            var details = string.Concat(
                "Assigned review ", reviewId.ToString("D"),
                " to ", assignee.UserName, " <", assigneeEmail, ">",
                string.IsNullOrWhiteSpace(team) ? string.Empty : " on queue " + team.Trim());

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService,
                CommandAssignCase,
                "AssignCase " + caseId.ToString("D"),
                CaseEntity,
                caseId,
                reason,
                details,
                idempotencyKey,
                context);

            SetResponse(context, assignmentId, reviewId, "Assigned", auditId, false);
        }

        /// <summary>
        /// The systemuser and contact behind one work email (OD-003, AD-010). Both must
        /// exist: the systemuser owns the row for Code App visibility, the contact is what
        /// Power Pages Contact-scoped permissions resolve through (AD-047), and an
        /// assignment carrying only one of them is allocated in one front end and invisible
        /// in the other.
        /// </summary>
        public static Assignee ResolveAssignee(IOrganizationService service, string email)
        {
            var user = FindSingle(
                service,
                UserEntity,
                new ColumnSet("fullname", "isdisabled"),
                new FilterExpression(LogicalOperator.Or)
                {
                    Conditions =
                    {
                        new ConditionExpression("internalemailaddress", ConditionOperator.Equal, email),
                        new ConditionExpression("domainname", ConditionOperator.Equal, email),
                    },
                });

            if (user == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "No Dataverse user has the work email " + email + ", so the case cannot be allocated to them.");
            }

            if (user.GetAttributeValue<bool>("isdisabled"))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "That user account is disabled, so work cannot be allocated to it.");
            }

            var contact = FindSingle(
                service,
                ContactEntity,
                new ColumnSet("fullname"),
                new FilterExpression
                {
                    Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.Equal, email) },
                });

            if (contact == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "No portal contact has the work email " + email + ". Allocating without one would leave them unable to open the review.");
            }

            return new Assignee
            {
                UserId = user.Id,
                ContactId = contact.Id,
                UserName = user.GetAttributeValue<string>("fullname") ?? email,
            };
        }

        /// <summary>
        /// The review instance this allocation is for. An explicit id wins; otherwise the
        /// earliest unsubmitted check by sequence, which is the Tax leg on a Tax-then-AQS
        /// route and the AQS leg once Tax has been submitted (BR-004); and where the case
        /// carries no open check at all, the one the route says it owes next, opened here.
        ///
        /// Opening it is what makes manager allocation reach a fresh case. Only the portal
        /// self-claim (AD-076) created review instances, so a queued case nobody had claimed
        /// — and every Tax-then-AQS case returning to the queue for its AQS leg — refused
        /// allocation with "no unsubmitted check", leaving BR-003's named allocation
        /// reachable only after a checker had first taken the case themselves. The instance
        /// is opened with <see cref="ClaimCasePlugin.OpenNextReview"/>, the same shape the
        /// self-claim writes, and as the plug-in user for the same reason that path does:
        /// the row is owned by the assignee, and assigning on create is not a privilege the
        /// allocating manager needs to hold.
        ///
        /// Public and static so the three outcomes are testable without a plug-in context.
        /// </summary>
        public static Entity ResolveReviewInstance(
            IOrganizationService userService,
            IOrganizationService systemService,
            Entity outcomeCase,
            Guid? requestedReviewId,
            Assignee assignee)
        {
            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(ReviewTypeAttr, SequenceAttr, SubmittedOnAttr),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("al_outcomecaseid", ConditionOperator.Equal, outcomeCase.Id),
                        new ConditionExpression(SubmittedOnAttr, ConditionOperator.Null),
                    },
                },
                Orders = { new OrderExpression(SequenceAttr, OrderType.Ascending) },
            };

            if (requestedReviewId.HasValue)
            {
                query.Criteria.AddCondition(
                    "al_reviewinstanceid", ConditionOperator.Equal, requestedReviewId.Value);
            }

            var matches = userService.RetrieveMultiple(query).Entities;
            if (matches.Count > 0)
            {
                return matches[0];
            }

            if (requestedReviewId.HasValue)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "That check is already submitted, or does not belong to this case.");
            }

            return ClaimCasePlugin.OpenNextReview(systemService, outcomeCase, assignee);
        }

        /// <summary>
        /// Closes the currently active assignment rows. Superseding rather than deleting is
        /// what preserves the allocation trail (BR-003, BR-012); a reassignment therefore
        /// leaves two rows, one released and one active.
        /// </summary>
        public static void ReleasePriorAssignments(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression(AssignmentEntity)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("al_outcomecaseid", ConditionOperator.Equal, caseId),
                        new ConditionExpression(IsActiveAttr, ConditionOperator.Equal, true),
                    },
                },
            };

            foreach (var prior in CommandHelpers.RetrieveAll(service, query))
            {
                service.Update(new Entity(AssignmentEntity, prior.Id)
                {
                    [IsActiveAttr] = false,
                    [ReleasedOnAttr] = DateTime.UtcNow,
                });
            }
        }

        /// <summary>
        /// Writes the allocated checker's name onto the case (<c>al_checkername</c>), the
        /// name the checklist header and the case detail print. Mirrors what
        /// ClaimCasePlugin does for a portal self-claim, so a check reads the same however
        /// it was allocated.
        /// </summary>
        public static void StampCheckerName(IOrganizationService service, Guid caseId, string checkerName)
        {
            service.Update(new Entity(CaseEntity, caseId)
            {
                [CheckerNameAttr] = checkerName,
            });
        }

        /// <summary>
        /// Points the review instance at the assignee in both identity systems and moves it
        /// to Assigned. Optimistic concurrency applies here rather than to the case, because
        /// this is the row a second allocator would be racing for.
        /// </summary>
        private static void StampReviewInstance(
            IOrganizationService service,
            Guid reviewId,
            Assignee assignee,
            string expectedRowVersion)
        {
            var update = new Entity(ReviewEntity, reviewId)
            {
                [AssignedContactAttr] = new EntityReference(ContactEntity, assignee.ContactId),
                [ReviewStatusAttr] = new OptionSetValue(ReviewAssigned),
                ["ownerid"] = new EntityReference(UserEntity, assignee.UserId),
            };

            if (string.IsNullOrEmpty(expectedRowVersion))
            {
                service.Update(update);
                return;
            }

            update.RowVersion = expectedRowVersion;
            try
            {
                service.Execute(new UpdateRequest
                {
                    Target = update,
                    ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
                });
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> fault)
            {
                if (CommandHelpers.IsConcurrencyFault(fault))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ConflictPrefix +
                        "This check was allocated by someone else while you were working. Reload and try again.");
                }

                throw;
            }
        }

        /// <summary>
        /// The active assignment row for this (case, review, person), created if they have
        /// never held this check and brought back to active if they have.
        ///
        /// <c>al_caseassignmentcode</c> is an alternate key unique per those three, so a
        /// second create for the same three faulted with "Assignment code key violated" -
        /// which made allocating a check back to someone who held it before impossible.
        /// That is ordinary work: a manager moves a check to cover an absence and moves it
        /// back. The row is reused instead, so a person holds one row per check whose
        /// active/released state says whether it is theirs now, and the history of who held
        /// it when is the Audit Event trail, which records every allocation either way.
        ///
        /// Reassignment between two different people still leaves a row each, one released
        /// and one active, which is what BR-003 asks for.
        ///
        /// Public and static so both outcomes are testable without a plug-in context.
        /// </summary>
        public static Guid AllocateAssignment(
            IOrganizationService service,
            Guid caseId,
            Guid reviewId,
            Assignee assignee,
            string caseReference,
            string team,
            string reason,
            Guid correlationId = default(Guid))
        {
            var code = BuildAssignmentCode(caseId, reviewId, assignee.UserId);

            var assignment = new Entity(AssignmentEntity)
            {
                ["al_name"] = BuildAssignmentName(caseReference, assignee.UserName),
                [AssignmentCodeAttr] = code,
                ["al_outcomecaseid"] = new EntityReference(CaseEntity, caseId),
                [AssignedUserAttr] = new EntityReference(UserEntity, assignee.UserId),
                [AssignedContactAttr] = new EntityReference(ContactEntity, assignee.ContactId),
                [AssignedOnAttr] = DateTime.UtcNow,
                [IsActiveAttr] = true,
            };

            if (!string.IsNullOrWhiteSpace(team))
            {
                assignment[AssignedTeamAttr] = team.Trim();
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                assignment[AssignmentReasonAttr] = reason.Trim();
            }

            var existing = FindAssignmentByCode(service, code);
            if (existing == Guid.Empty)
            {
                return service.Create(assignment);
            }

            // Held before. Clearing al_releasedon is what makes the row current again
            // rather than a released one that merely says it is active.
            assignment.Id = existing;
            assignment.Attributes.Remove(AssignmentCodeAttr);
            assignment[ReleasedOnAttr] = null;
            service.Update(assignment);

            // NotificationEmitterPlugin hangs off the create, which this is not, and the
            // checker is owed the same email either way (project owner, 2026-09-10). The
            // outbox code carries al_assignedon, so this is a new notification rather than
            // the earlier one being recognised as already queued.
            NotificationEmitterPlugin.QueueAllocation(service, correlationId, existing);
            return existing;
        }

        /// <summary>The assignment carrying this code, or empty when there is none.</summary>
        public static Guid FindAssignmentByCode(IOrganizationService service, string code)
        {
            var query = new QueryExpression(AssignmentEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(AssignmentCodeAttr, ConditionOperator.Equal, code);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count > 0 ? found[0].Id : Guid.Empty;
        }

        /// <summary>
        /// Stable per (case, review, assignee) so a replayed create collides on the
        /// <c>al_caseassignmentcode</c> alternate key rather than writing a duplicate
        /// history row (NFR-REL-01). Truncated to the column's 100 characters.
        /// </summary>
        public static string BuildAssignmentCode(Guid caseId, Guid reviewId, Guid userId)
        {
            var code = string.Concat(
                "ASG-",
                caseId.ToString("N").Substring(0, 12), "-",
                reviewId.ToString("N").Substring(0, 12), "-",
                userId.ToString("N").Substring(0, 12));
            return code.Length <= 100 ? code : code.Substring(0, 100);
        }

        public static string BuildAssignmentName(string caseReference, string assigneeName)
        {
            var name = string.Concat(
                string.IsNullOrWhiteSpace(caseReference) ? "Case" : caseReference,
                " -> ",
                string.IsNullOrWhiteSpace(assigneeName) ? "Unnamed" : assigneeName);
            return name.Length <= 100 ? name : name.Substring(0, 100);
        }

        private static Entity FindSingle(
            IOrganizationService service,
            string entity,
            ColumnSet columns,
            FilterExpression criteria)
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = columns,
                Criteria = criteria,
                TopCount = 1,
            };

            var matches = service.RetrieveMultiple(query).Entities;
            return matches.Count > 0 ? matches[0] : null;
        }

        private static Guid? ParseOptionalGuid(IPluginExecutionContext context, string name)
        {
            var raw = CommandHelpers.GetOptionalString(context, name);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            Guid value;
            if (!Guid.TryParse(raw.Trim(), out value))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + name + " must be a GUID.");
            }

            return value;
        }

        private static void SetResponse(
            IPluginExecutionContext context,
            Guid? assignmentId,
            Guid? reviewId,
            string status,
            Guid auditId,
            bool conflict)
        {
            context.OutputParameters[OutAssignmentId] = assignmentId.HasValue
                ? assignmentId.Value.ToString("D")
                : string.Empty;
            context.OutputParameters[OutReviewInstanceId] = reviewId.HasValue
                ? reviewId.Value.ToString("D")
                : string.Empty;
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }

        public sealed class Assignee
        {
            public Guid UserId { get; set; }

            public Guid ContactId { get; set; }

            public string UserName { get; set; }
        }
    }
}
