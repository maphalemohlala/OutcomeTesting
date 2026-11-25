using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The T&amp;C Manager's half of the BR-008 loop, done from the portal (PP-12, FR-023,
    /// AD-045). Registered as a synchronous pre-operation step on Create of
    /// <c>al_signoff</c>.
    ///
    /// Unlike the review submit and the adviser completion, this is a **create**, and the
    /// Portals Web API does support create — so no trigger column is needed. That
    /// difference is not cosmetic, it is what makes the permission model work: a trigger
    /// column on <c>al_remediationaction</c> would sit behind one site-wide field
    /// allowlist shared with the adviser, and an adviser could then approve their own
    /// remediation. A separate table carries a separate permission, so create on
    /// <c>al_signoff</c> is bound to <c>AL Portal - T&amp;C Supervisor</c> alone.
    ///
    /// The page sends only the decision, the notes and the action. Everything identifying —
    /// the name, the business code, the case and the timestamp — is stamped here, so the
    /// browser cannot choose a key or backdate a sign-off.
    /// </summary>
    public class SignoffGuardPlugin : PluginBase
    {
        private const string SignoffEntity = "al_signoff";
        private const string ActionEntity = "al_remediationaction";
        private const string ActionStatus = "al_actionstatus";
        private const string DecisionAttr = "al_signoffdecision";
        private const string NotesAttr = "al_notes";
        private const string ActionLookup = "al_remediationactionid";
        private const string CaseLookup = "al_outcomecaseid";
        private const string FinalOutcomeAttr = "al_finaloutcome";
        private const string ClockStartedOnAttr = "al_clockstartedon";

        private const int StatusCompleted = Remediation.StatusCompleted;
        private const int DecisionRejectedValue = 120910721;

        public SignoffGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SignoffGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var service = localPluginContext.PluginUserService;

            object target;
            if (!context.InputParameters.TryGetValue("Target", out target))
            {
                return;
            }

            var signoff = target as Entity;
            if (signoff == null || !string.Equals(signoff.LogicalName, SignoffEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // A create made straight on the table skips al_SignOffRemediation's own gate, and the
            // app's Dataverse role creates here at Global depth.
            var system = localPluginContext.OrgSvcFactory.CreateOrganizationService(null);
            WriteProvenance.EnsureTrustedOrPermitted(
                system, context, SignOffRemediationPlugin.SignOffResource, PermissionHelpers.AccessEdit);

            var decision = signoff.GetAttributeValue<OptionSetValue>(DecisionAttr);
            var notes = signoff.GetAttributeValue<string>(NotesAttr);
            var finalOutcome = signoff.GetAttributeValue<OptionSetValue>(FinalOutcomeAttr);

            var refusal = DecisionRefusal(
                decision == null ? (int?)null : decision.Value,
                finalOutcome == null ? (int?)null : finalOutcome.Value,
                notes);
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(refusal);
            }

            var actionRef = signoff.GetAttributeValue<EntityReference>(ActionLookup);
            if (actionRef == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A sign-off must name the remediation action it relates to.");
            }

            var action = service.Retrieve(ActionEntity, actionRef.Id, new ColumnSet(ActionStatus, CaseLookup, ClockStartedOnAttr));

            var status = action.GetAttributeValue<OptionSetValue>(ActionStatus);
            if (status == null || status.Value != StatusCompleted)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "Only a completed remediation action can be signed off.");
            }

            // The action alone is not enough: a Pass-with-issues case closes on its last
            // completion, and a rejection recorded after that reopened the action on a Closed
            // case that can never move again.
            var caseRef = action.GetAttributeValue<EntityReference>(CaseLookup);
            if (caseRef != null)
            {
                var caseStatus = CaseTransitions.CurrentStatus(service, caseRef.Id);
                // A rejection moves the case to Awaiting Remediation on the first action of the
                // sitting (SignoffProgressPlugin.MoveCase), and the portal then rejects the rest
                // one by one (TEST case 258841914, 2026-11-19).
                var restOfRejectionSitting = caseStatus == CaseLifecycle.AwaitingRemediation
                    && decision != null && decision.Value == DecisionRejectedValue;
                if (caseStatus != CaseLifecycle.AwaitingSignoff && !restOfRejectionSitting)
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + "This case is "
                        + (caseStatus.HasValue ? CaseLifecycle.NameOf(caseStatus.Value) : "(none)")
                        + ", not Awaiting Sign-off, so its remediation cannot be signed off.");
                }
            }

            // An approval happens once. Without this the same action can be approved twice,
            // and a later rejection would reopen an already-approved action, leaving two
            // contradictory sign-offs with nothing recording which is authoritative.
            //
            // A rejection is deliberately not caught here: it is the start of the BR-008 loop
            // rather than the end of it. This asked whether the action carried *any* sign-off
            // until 2026-09-11, which made every rejection terminal - the adviser reworked,
            // the case returned to Awaiting Sign-off, and the supervisor was then refused the
            // decision that would have closed it. IO-300003 and IO-300006 sat there with the
            // rework done, and no action in the environment had ever reached a second
            // decision.
            if (AlreadySettled(service, actionRef.Id))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "This remediation action has already been approved. Reopening an approved sign-off is a privileged correction.");
            }

            signoff["al_name"] = "SignOff " + actionRef.Id.ToString("D");
            signoff["al_signoffcode"] = SignoffCode(actionRef.Id, action.GetAttributeValue<DateTime?>(ClockStartedOnAttr));
            signoff["al_signedoffon"] = DateTime.UtcNow;

            if (caseRef != null)
            {
                signoff[CaseLookup] = caseRef;
            }
        }

        /// <summary>
        /// One code per action per decision round, so two decisions racing on the same round
        /// collide on the al_signoffcode alternate key instead of both being recorded - the
        /// approved-already check cannot see a sign-off that has not committed yet. A rejection
        /// restarts the action's clock (<see cref="SignoffProgressPlugin.ReopenedAction"/>),
        /// and that is what makes the reworked action's next decision a new code.
        /// </summary>
        public static string SignoffCode(Guid actionId, DateTime? clockStartedOn)
        {
            var code = "SO-" + actionId.ToString("N");
            return clockStartedOn.HasValue
                ? code + "-" + clockStartedOn.Value.ToUniversalTime().ToString("yyyyMMddHHmmssfff")
                : code;
        }

        /// <summary>
        /// Why a decision cannot be recorded as written, prefixed for the caller, or null
        /// when it can. Plain values, so the rules can be tested without a fake service -
        /// they were decided inline in Execute until 2026-09-13, and none of the three
        /// final-outcome refusals had a test.
        ///
        /// BR-008: a rejected sign-off returns to the adviser, so it must say why.
        ///
        /// The final outcome is the one the supervisor records as they approve (project
        /// owner, 2026-09-11, settling OD-041). Approving used to leave the case at Awaiting
        /// Recheck for a separate regrade, and a case that never got one never reached
        /// Closed - so it never reached the export either, which collects Closed cases. A
        /// rejection grades nothing: it sends the work back to the adviser, and the check it
        /// belongs to has not finished - refused rather than ignored, because a page that
        /// sends one is wrong about what it is doing. AD-031: overriding a grade records why,
        /// and the notes are where the supervisor says it; required here as well as in
        /// RegradeCasePlugin.Regrade, so the supervisor is told before the sign-off is
        /// written rather than after.
        /// </summary>
        public static string DecisionRefusal(int? decision, int? finalOutcome, string notes)
        {
            if (!decision.HasValue)
            {
                return CommandHelpers.ValidationPrefix + "A sign-off must record whether the remediation was approved or rejected.";
            }

            var noNotes = string.IsNullOrWhiteSpace(notes);
            if (decision.Value == DecisionRejectedValue && noNotes)
            {
                return CommandHelpers.PreconditionPrefix + "A rejected sign-off must record notes explaining the return.";
            }

            if (!finalOutcome.HasValue)
            {
                return null;
            }

            if (decision.Value == DecisionRejectedValue)
            {
                return CommandHelpers.ValidationPrefix +
                    "A rejected sign-off returns the remediation to the adviser, so it cannot record a final outcome.";
            }

            // An approval settles the case, so only Pass or Potential harm (requirements log row
            // 25): an outstanding point is a rejection with notes, not an unclear final grade.
            // The page already offered only these two; this refuses a payload sent by hand.
            if (finalOutcome.Value != OutcomeRules.FinalOutcomePass
                && finalOutcome.Value != OutcomeRules.FinalOutcomePotentialHarm)
            {
                return CommandHelpers.ValidationPrefix +
                    "An approved sign-off's final outcome must be Pass or Potential harm.";
            }

            if (noNotes)
            {
                return CommandHelpers.PreconditionPrefix + "Recording a final outcome must say why the grade was set.";
            }

            return null;
        }

        /// <summary>
        /// True when this action has already been <b>approved</b>, which is the only decision
        /// that ends it. A rejection leaves the action live - it reopens for the adviser
        /// (<see cref="SignoffProgressPlugin"/>) and must be decidable again once reworked -
        /// so it is not counted here.
        ///
        /// Read with the system service so the answer does not depend on the caller's read
        /// privileges: a sign-off they cannot see is still a sign-off.
        /// </summary>
        public static bool AlreadySettled(IOrganizationService service, Guid actionId)
        {
            var query = new QueryExpression(SignoffEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(ActionLookup, ConditionOperator.Equal, actionId);
            query.Criteria.AddCondition(
                DecisionAttr, ConditionOperator.Equal, SignoffProgressPlugin.DecisionApprovedValue);

            return service.RetrieveMultiple(query).Entities.Count > 0;
        }
    }
}
