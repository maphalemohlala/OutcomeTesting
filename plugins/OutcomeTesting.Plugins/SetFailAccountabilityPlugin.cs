using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command SetFailAccountability (AD-003, OD-024). Records who is
    /// accountable for a File Quality or Advice Quality fail on an Outcome, which is the
    /// judgement AD-039's export columns 11-14 and 17-20 report and which nothing in the
    /// model captured before.
    ///
    /// Set after submission rather than during the review, because the Outcome does not
    /// exist until the review is submitted. al_GenerateExport refuses a non-pass Outcome
    /// that records no accountability, so the step cannot be silently skipped.
    /// </summary>
    public class SetFailAccountabilityPlugin : PluginBase
    {
        private const string InTargetId = "TargetId";
        private const string InFqAdviser = "FqAdviser";
        private const string InFqParaplanner = "FqParaplanner";
        private const string InAqAdviser = "AqAdviser";
        private const string InAqParaplanner = "AqParaplanner";
        // Optional (item 8, 2026-09-19): the contact carrying the fail, where it is
        // someone other than the adviser or paraplanner the case names. An empty string
        // clears it and puts the case's own person back in the extract.
        private const string InFqContactId = "FqContactId";
        private const string InAqContactId = "AqContactId";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";

        private const string OutcomeEntity = "al_outcome";
        private const string CaseAttr = "al_outcomecaseid";
        // Its own value since 2026-09-05 (OD-032). It previously reused 120910788, which the
        // option set labels SetRoleAssignmentActive, so every row this command wrote was
        // labelled as a role change and CommandHelpers.FindAuditByKey could not keep the two
        // commands' idempotency keys apart. Rows written before the cut-over stay on
        // 120910788 and are immutable (NFR-AUD-01); they are identified by
        // al_name = "SetFailAccountability" with al_targettable = "al_outcome".
        private const int CommandSetFailAccountability = 120910792;

        public SetFailAccountabilityPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SetFailAccountabilityPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService;
            var systemService = localPluginContext.PluginUserService;

            var targetId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var fqAdviser = CommandHelpers.GetRequiredBool(context, InFqAdviser);
            var fqParaplanner = CommandHelpers.GetRequiredBool(context, InFqParaplanner);
            var aqAdviser = CommandHelpers.GetRequiredBool(context, InAqAdviser);
            var aqParaplanner = CommandHelpers.GetRequiredBool(context, InAqParaplanner);
            var fqContactId = ParseOptionalContact(context, InFqContactId);
            var aqContactId = ParseOptionalContact(context, InAqContactId);

            PermissionHelpers.EnsureAppPermission(systemService, context, "page.cases", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandSetFailAccountability);
            if (existingAudit != null)
            {
                SetResponse(context, "Recorded", existingAudit.Id);
                return;
            }

            var outcome = userService.Retrieve(
                OutcomeEntity, targetId,
                new ColumnSet(Outcomes.InitialOutcomeAttr, Outcomes.FinalOutcomeAttr, CaseAttr));

            var caseRef = outcome.GetAttributeValue<EntityReference>(CaseAttr);
            var fileQualityFailed = caseRef != null && FileQuality.FailedOn(userService, caseRef.Id);

            var refusal = RefusalFor(Outcomes.EffectiveOutcome(outcome), fileQualityFailed);
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
            }

            userService.Update(new Entity(OutcomeEntity, targetId)
            {
                ["al_fqadviseraccountable"] = fqAdviser,
                ["al_fqparaplanneraccountable"] = fqParaplanner,
                ["al_aqadviseraccountable"] = aqAdviser,
                ["al_aqparaplanneraccountable"] = aqParaplanner,

                // Written on every save, including as null, so choosing "the case's own
                // adviser" after naming someone else actually clears them rather than
                // leaving a stale name the extract would keep using.
                ["al_fqaccountablecontactid"] = fqContactId,
                ["al_aqaccountablecontactid"] = aqContactId,
            });

            var details = "FQ adviser " + fqAdviser + ", FQ paraplanner " + fqParaplanner
                + ", AQ adviser " + aqAdviser + ", AQ paraplanner " + aqParaplanner
                + ", FQ named " + Describe(fqContactId, NameOf(userService, fqContactId))
                + ", AQ named " + Describe(aqContactId, NameOf(userService, aqContactId));

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandSetFailAccountability, "SetFailAccountability", OutcomeEntity, targetId,
                null, details, idempotencyKey, context);

            SetResponse(context, "Recorded", auditId);
        }

        /// <summary>
        /// Why there is no fail on this case to attribute, or null when there is one.
        ///
        /// Accountability describes a fail: recording it against a clean case would put a
        /// name in a Trail Light column that AD-039 only ever fills for a fail.
        ///
        /// A file quality fail counts on its own. The guard used to read the advice quality
        /// outcome alone, which refused every case that passed on advice and failed on the
        /// file - five of the seven closed cases in DEV, and exactly the population the
        /// export now names the paraplanner for. Refusing to override a pair the export had
        /// already derived is the wrong way round.
        ///
        /// Pure so the decision can be read off a test without a fake organisation service,
        /// as GenerateExportPlugin.IsAccountable is.
        /// </summary>
        /// <summary>
        /// The contact a parameter names, or null where none was sent or it was cleared.
        ///
        /// Optional and string-typed, like every other parameter these commands take, so
        /// the portal and the Code App can send one the same way. An unparseable value is
        /// refused rather than ignored: silently dropping it would record the flags without
        /// the person and put the case's own adviser back in the extract.
        /// </summary>
        private static EntityReference ParseOptionalContact(IPluginExecutionContext context, string parameter)
        {
            var raw = CommandHelpers.GetOptionalString(context, parameter);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            Guid id;
            if (!Guid.TryParse(raw.Trim(), out id) || id == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + parameter + " must name a contact.");
            }

            return new EntityReference("contact", id);
        }

        /// <summary>
        /// How a named person is written into the audit line.
        ///
        /// This wrote the contact's GUID alone, which is unreadable to the auditor the line
        /// exists for (F21). It went unnoticed because the parameter never arrived until
        /// F15 was fixed, so every line said "(the case's own)" and this branch was never
        /// reached in the product.
        ///
        /// The id stays beside the name: names are not unique and contacts are renamed, so
        /// a name alone would be readable without being evidence. A name that cannot be
        /// read falls back to the id, which is worse to read but still true.
        /// </summary>
        public static string Describe(EntityReference contact, string fullName)
        {
            if (contact == null)
            {
                return "(the case's own)";
            }

            var id = contact.Id.ToString("D");
            return string.IsNullOrWhiteSpace(fullName) ? id : fullName.Trim() + " (" + id + ")";
        }

        /// <summary>
        /// The contact's name for the audit line, or null when it cannot be read.
        ///
        /// Read through the caller's service, so it sees what they may see. A failure is not
        /// escalated: the judgement has already been written by this point, and refusing the
        /// command because its audit line would be less readable would be the wrong trade.
        /// </summary>
        private static string NameOf(IOrganizationService service, EntityReference contact)
        {
            if (contact == null)
            {
                return null;
            }

            try
            {
                var row = service.Retrieve("contact", contact.Id, new ColumnSet("fullname"));
                return row.GetAttributeValue<string>("fullname");
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string RefusalFor(int? effectiveOutcome, bool fileQualityFailed)
        {
            if (fileQualityFailed)
            {
                return null;
            }

            if (!effectiveOutcome.HasValue)
            {
                return "This case has no outcome recorded, so there is no fail to attribute.";
            }

            if (!OutcomeRules.RequiresRemediation(effectiveOutcome.Value))
            {
                return "This case passed, so there is no fail to attribute.";
            }

            return null;
        }

        private static void SetResponse(IPluginExecutionContext context, string status, Guid auditId)
        {
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
