using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The Tax team's two header fields, edited from the portal (project owner, 2026-09-13).
    /// Registered as a synchronous post-operation step on Update of <c>contact</c>, filtered
    /// to <c>al_caseheaderrequest</c>.
    ///
    /// <b>Why a trigger column rather than a write on the case.</b> The same reason the
    /// answer (AD-053), the claim (AD-076) and the sign-off (AD-099) each ended here: Power
    /// Pages refused a browser write on the business table with 90040106 whatever the table
    /// permissions said. The page writes one column on the signed-in user's own contact row
    /// and the update happens here, as the application user, where no portal table permission
    /// is consulted.
    ///
    /// <b>What the Tax team decides.</b> The Checker Checklist labels these two fields "Tax
    /// check required (Tax team to complete)" and "For Tax team usage", so the document
    /// already assigns both to the Tax team; until now only the Code App could set them.
    /// "Return to paraplanner" is what ends a case at Tax with no AQS check owed
    /// (UpdateCaseDetailsPlugin.DeriveRoute), which is the decision the project owner asked
    /// to put in the Tax checker's hands.
    ///
    /// <b>Locked once their review is submitted.</b> A submitted review is immutable
    /// (BR-012), and the route was already acted on at submit - SubmitReviewPlugin decided
    /// there and then whether the case handed off to AQS, went to remediation or finalised.
    /// Letting the disposition move afterwards would contradict something that has already
    /// happened, so the guard is the review's own status rather than the case's.
    /// </summary>
    public class CaseHeaderRequestPlugin : PluginBase
    {
        private const string ContactEntity = "contact";
        private const string CaseEntity = "al_outcomecase";
        private const string ReviewEntity = "al_reviewinstance";
        public const string RequestAttr = "al_caseheaderrequest";

        private const string TaxRequiredAttr = "al_taxcheckrequired";
        private const string DispositionAttr = "al_taxteamdisposition";
        private const string ReviewStatusAttr = "al_reviewstatus";
        private const string ReviewTypeAttr = "al_reviewtype";
        private const string ReviewCaseAttr = "al_outcomecaseid";
        private const string AssignedContactAttr = "al_assignedcontactid";

        private const int StatusSubmitted = 120910212;
        // The same event the command path writes, taken from it rather than re-declared: a
        // re-declaration here carried 120910752, which is ReturnCase on al_command, so every
        // portal header edit was audited as a return until 2026-09-13.
        public const int CommandUpdateCaseDetails = UpdateCaseDetailsPlugin.CommandUpdateCaseDetails;

        /// <summary>
        /// The two option sets the page may set, pinned here so a value the browser sends
        /// is checked before it is written. The command path parses labels against the
        /// option set; this path took any integer, and DeriveRoute silently did nothing
        /// with one it did not know, leaving a blank header under an audit line that named
        /// a change.
        /// </summary>
        public const int TaxCheckRequiredYes = 120910560;
        public const int TaxCheckRequiredNo = 120910561;
        public const int DispositionSubmitToAqs = 120910570;
        public const int DispositionReturnToParaplanner = 120910571;

        public CaseHeaderRequestPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(CaseHeaderRequestPlugin))
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

            var entity = target as Entity;
            if (entity == null
                || !string.Equals(entity.LogicalName, ContactEntity, StringComparison.OrdinalIgnoreCase)
                || !entity.Contains(RequestAttr))
            {
                return;
            }

            var raw = entity.GetAttributeValue<string>(RequestAttr);
            if (string.IsNullOrWhiteSpace(raw))
            {
                // The clear this plug-in makes below comes back through the pipeline as an
                // update to null. Returning here is what stops it acting on its own write.
                return;
            }

            var payload = CaseHeaderRequestPayload.Parse(raw);
            if (payload == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "The case header request could not be read.");
            }

            // The contact whose row was written, never a value the page supplied. The Self
            // scope on the site's contact permission is what makes this the signed-in user.
            var contactId = context.PrimaryEntityId;

            Apply(service, context, contactId, payload);
        }

        /// <summary>
        /// Validates and applies the edit. Separated from the pipeline wiring so the rules
        /// can be read in one place.
        /// </summary>
        private static void Apply(
            IOrganizationService service,
            IPluginExecutionContext context,
            Guid contactId,
            CaseHeaderRequestPayload payload)
        {
            Guid caseId;
            if (!Guid.TryParse(payload.CaseId, out caseId) || caseId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A case header edit must name the case it applies to.");
            }

            var fields = payload.ParsedFields();
            var editsTaxFields = payload.TaxCheckRequired != 0 || payload.TaxTeamDisposition != 0;

            if (!editsTaxFields && fields.Count == 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A case header edit must change at least one field.");
            }

            var unknown = UnknownOptionRefusal(payload);
            if (unknown != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.ValidationPrefix + unknown);
            }

            EnsureCheckerEditable(fields);

            // Assignment first, and for EVERY payload (audit finding 2, 2026-09-20).
            //
            // This used to run only when `fields.Count > 0`, so a payload carrying nothing
            // but TaxCheckRequired or TaxTeamDisposition skipped it entirely - and those two
            // re-derive the review route (BR-004, AD-036). Any contact holding the Tax
            // Reviewer role could therefore reroute ANY case in the system whose Tax review
            // was unsubmitted, including one assigned to somebody else. Reading every case
            // is deliberate (project owner, 2026-09-20) and reading is all it may buy:
            // "tax reviewers can only work on cases assigned to them".
            //
            // The contact permission the page writes through is shared with the sign-off and
            // the claim - one allowlist per table - so this check has to be here. The contact
            // is read from the platform, never claimed in the payload.
            EnsureAssignedToCase(service, contactId, caseId);

            // The ROLE check stays gated on what the edit actually touches. A checker editing
            // the client's name is not doing the Tax team's work and must not need the Tax
            // team's role. That was always the right shape; assignment was not.
            if (editsTaxFields)
            {
                EnsureTaxReviewerRole(service, contactId);
                EnsureTaxReviewOpen(service, contactId, caseId);
            }

            // Cleared before the case is updated, so the column never keeps a request after
            // it has been acted on and a repeat of the same edit is a real second write
            // rather than an unchanged value Dataverse may not raise an Update for. Both
            // writes share this transaction: a refusal below rolls the clear back with it.
            service.Update(new Entity(ContactEntity, contactId)
            {
                [RequestAttr] = null,
            });

            var columns = new List<string>
            {
                TaxRequiredAttr, DispositionAttr, "al_reviewrouteid", "al_casestatus",
            };
            foreach (var field in fields.Keys)
            {
                if (!columns.Contains(field))
                {
                    columns.Add(field);
                }
            }

            var before = service.Retrieve(CaseEntity, caseId, new ColumnSet(columns.ToArray()));

            var update = new Entity(CaseEntity, caseId);
            var changes = new List<string>();

            if (payload.TaxCheckRequired != 0)
            {
                Record(before, update, changes, TaxRequiredAttr, "Tax check required", payload.TaxCheckRequired);
            }

            if (payload.TaxTeamDisposition != 0)
            {
                Record(before, update, changes, DispositionAttr, "For Tax team usage", payload.TaxTeamDisposition);
            }

            if (fields.Count > 0)
            {
                // The Custom API's own applier, so a field coerces and reads the same way
                // whichever front end sent it.
                UpdateCaseDetailsPlugin.ApplyFields(
                    service, fields, before, update, changes, new OptionLabels(service));
            }

            if (changes.Count == 0)
            {
                // Nothing actually moved. Not an error: a page that re-sends what is already
                // recorded has asked for the state it already has.
                return;
            }

            // The route follows the answers, exactly as it does on the command path. This is
            // the whole point of the edit: "Return to paraplanner" is what leaves a case
            // finished at Tax with no AQS check owed, and deriving it anywhere but here
            // would let the two front ends disagree about what the same answers mean.
            UpdateCaseDetailsPlugin.DeriveRoute(service, before, update, changes);

            service.Update(update);

            UpdateCaseDetailsPlugin.RequeueAfterRouteChange(service, before, update, changes);

            // Named to the contact, not the caller: a portal write reaches Dataverse as the
            // site's application user (AD-053), the same reason SignoffProgressPlugin and
            // CompleteRequestPlugin name theirs.
            var actorName = CommandHelpers.ResolveActorName(service, contactId);
            CommandHelpers.WriteAuditEvent(
                service,
                CommandUpdateCaseDetails,
                "UpdateCaseDetails " + caseId.ToString("D"),
                CaseEntity,
                caseId,
                null,
                "Case header edited from the portal: " + string.Join("; ", changes.ToArray()) + ".",
                "caseheader-" + caseId.ToString("N") + "-" + DateTime.UtcNow.Ticks.ToString(),
                context,
                contactId,
                actorName);
        }

        /// <summary>
        /// Puts one field on the update and describes the move, or leaves both alone where
        /// the value is already what was asked for. Recording only real changes is what keeps
        /// the audit line honest and stops DeriveRoute running for an edit that moved nothing.
        /// </summary>
        private static void Record(
            Entity before,
            Entity update,
            List<string> changes,
            string attribute,
            string label,
            int value)
        {
            var current = before.GetAttributeValue<OptionSetValue>(attribute);
            if (current != null && current.Value == value)
            {
                return;
            }

            update[attribute] = new OptionSetValue(value);
            changes.Add(label);
        }

        /// <summary>
        /// Why the payload's option values cannot be written, or null when each is either
        /// unsent (zero) or a value its column actually carries.
        /// </summary>
        public static string UnknownOptionRefusal(CaseHeaderRequestPayload payload)
        {
            if (payload.TaxCheckRequired != 0
                && payload.TaxCheckRequired != TaxCheckRequiredYes
                && payload.TaxCheckRequired != TaxCheckRequiredNo)
            {
                return "'" + payload.TaxCheckRequired + "' is not a Tax check required option.";
            }

            if (payload.TaxTeamDisposition != 0
                && payload.TaxTeamDisposition != DispositionSubmitToAqs
                && payload.TaxTeamDisposition != DispositionReturnToParaplanner)
            {
                return "'" + payload.TaxTeamDisposition + "' is not a For Tax team usage option.";
            }

            return null;
        }

        /// <summary>
        /// The header fields a checker may edit from the portal (project owner, 2026-09-19:
        /// "other fields should be editable by the checkers excluding the references and
        /// IDs").
        ///
        /// What is deliberately NOT here, and why:
        ///
        ///   references and IDs   al_casereference and al_ioreference identify the case to
        ///                        the business and key the import (BR-001). They are not in
        ///                        the Custom API's editable map either, so nothing anywhere
        ///                        can move them.
        ///   al_duedate           derived from the upload, and movable only by a manager in
        ///                        the Code App (item 6, 2026-09-19: "3 days, only editable
        ///                        by managers in codeapps"). "In codeapps" is half the
        ///                        instruction, so the portal refuses it whoever is asking -
        ///                        a T&C Manager signed into the portal is refused here and
        ///                        allowed in the app, which is the point.
        ///   al_checkdate         derived from the submit (project owner, 2026-09-21), so
        ///                        nobody types it - see SubmitReviewPlugin.StampCheckDate.
        ///   al_casestatus        the lifecycle, moved by the commands that own each
        ///   al_priority          transition, and by a manager - not a field on the check.
        ///   the two Tax fields   edited on their own path above, which is role-gated,
        ///                        because they decide the route rather than describe the case.
        ///   the two codes        al_advisercode and al_paraplannercode were retired from
        ///                        2026-09-22. A code is a property of the PERSON now, held
        ///                        on contact.al_staffcode and maintained on the People page
        ///                        - not something to restore here as an apparent omission.
        ///
        /// Checked against this set BEFORE the caller's own permissions, so a checker naming
        /// a field they may never edit is told that, rather than being told they are not
        /// assigned to a case they may well be assigned to.
        /// </summary>
        private static readonly HashSet<string> CheckerEditable =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "al_clientname",
                "al_advisername",
                "al_adviserstatus",
                "al_paraplanner",
                "al_products",
                "al_casetype",
                "al_advicedate",
                "al_productsolutiontype",
                // The managed-list lookup the portal's Product / solution type select now
                // writes (AD-187). The choice column above stays, because a case imported
                // before the migration carries only that and has to remain correctable.
                //
                // Being on this list buys nothing on its own: al_UpdateCaseDetails still
                // decides whether the option exists, belongs to this list and is offered
                // today, and the assignment and role checks above still run.
                ListOptionRules.ProductTypeAttribute,
                ListOptionRules.SampleSourceAttribute,
                ListOptionRules.CaseTypeAttribute,
                ListOptionRules.PreOrPostCheckAttribute,
                // Not a column: the set of products a case covers, applied by association.
                ListOptionRules.ProductsField,
                "al_samplesource",
                "al_preorpostcheck",
                "al_vulnerableclient",
            };

        /// <summary>
        /// Refuses a field the portal has no business editing, naming it.
        /// </summary>
        public static void EnsureCheckerEditable(Dictionary<string, string> fields)
        {
            if (fields == null)
            {
                return;
            }

            foreach (var field in fields.Keys)
            {
                if (CheckerEditable.Contains(field))
                {
                    continue;
                }

                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix +
                    "'" + field + "' is not a field that can be edited from the portal.");
            }
        }

        /// <summary>
        /// Refuses a header edit on a case the caller is not checking (item 5, 2026-09-19).
        ///
        /// "Assigned to them" means holding an open review on the case: al_reviewinstance
        /// carries al_assignedcontactid, and an unsubmitted one is work still in this
        /// checker's hands. Deliberately NOT al_checkername - that column comes from the
        /// upload file and names whoever the paraplanner expected, so it never proves the
        /// case was allocated to anyone.
        ///
        /// Server-side and not merely hidden on the page: the portal writes through a
        /// contact permission shared with the sign-off and the claim, so a request can be
        /// made by hand for any case id. Without this a signed-in checker could edit the
        /// header of a case they had never been given.
        ///
        /// Once their review is submitted the assignment stops counting, which is the same
        /// line BR-012 draws for the answers: a checker's work is done, and what they
        /// recorded is no longer theirs to move.
        /// </summary>
        public static void EnsureAssignedToCase(IOrganizationService service, Guid contactId, Guid caseId)
        {
            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(ReviewCaseAttr, ConditionOperator.Equal, caseId);
            query.Criteria.AddCondition("al_assignedcontactid", ConditionOperator.Equal, contactId);
            query.Criteria.AddCondition("al_submittedon", ConditionOperator.Null);

            if (service.RetrieveMultiple(query).Entities.Count > 0)
            {
                return;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix +
                "You can only edit the header of a case you are checking. This case is not assigned to you, " +
                "or your check on it has already been submitted.");
        }

        /// <summary>
        /// Only the Tax team edits the Tax team's fields. Refused in the words the page can
        /// show, for the reason that is actually true.
        /// </summary>
        public static void EnsureTaxReviewerRole(IOrganizationService service, Guid contactId)
        {
            if (WebRoleRegistry.HasRole(service, contactId, WebRoleRegistry.TaxReviewerRole))
            {
                return;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix +
                "Editing the Tax team's fields needs the " + WebRoleRegistry.TaxReviewerRole +
                " role on your portal account.");
        }

        /// <summary>
        /// Refuses the edit once the case's Tax review has been submitted.
        /// </summary>
        /// <remarks>
        /// The guard is the review's status and not the case's, because the case moves on
        /// after a submit - to Queued, Awaiting Remediation or Closed - and each of those
        /// would need its own rule. The review answers the question directly: the Tax
        /// checker's work is done, the route was acted on at that moment, and BR-012 makes
        /// what they submitted immutable.
        ///
        /// A case with no Tax review at all is allowed. That is a case whose Tax check has
        /// not been created yet, where "is a Tax check required?" is exactly the question
        /// these two fields exist to answer.
        /// </remarks>
        public static void EnsureTaxReviewOpen(IOrganizationService service, Guid contactId, Guid caseId)
        {
            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(ReviewCaseAttr, ConditionOperator.Equal, caseId);
            query.Criteria.AddCondition(ReviewTypeAttr, ConditionOperator.Equal, ResponseRules.ReviewTypeTax);
            query.Criteria.AddCondition(ReviewStatusAttr, ConditionOperator.Equal, StatusSubmitted);

            if (service.RetrieveMultiple(query).Entities.Count > 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "The Tax check on this case has been submitted, so its header fields can no longer be changed.");
            }

            // The caller's own Tax review, not just anybody's (audit finding 2). The check
            // above asks whether the case's Tax work is still open; this asks whether it is
            // THEIRS. Apart they are two half-checks that read as one whole one - which is
            // exactly how the hole survived review.
            //
            // Kept here rather than left to EnsureAssignedToCase alone: that method accepts
            // an open review of EITHER discipline, so an AQS checker assigned to the case
            // would pass it. Editing the Tax fields is the Tax team's work.
            var mine = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            mine.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            mine.Criteria.AddCondition(ReviewCaseAttr, ConditionOperator.Equal, caseId);
            mine.Criteria.AddCondition(ReviewTypeAttr, ConditionOperator.Equal, ResponseRules.ReviewTypeTax);
            mine.Criteria.AddCondition(AssignedContactAttr, ConditionOperator.Equal, contactId);

            if (service.RetrieveMultiple(mine).Entities.Count == 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.UnauthorizedPrefix +
                    "You can only change the Tax fields on a case whose Tax check is assigned to you.");
            }
        }
    }

    /// <summary>
    /// What the portal writes onto <c>contact.al_caseheaderrequest</c>: the case and the two
    /// Tax team option values. Zero means "not sent", so a page may move one field without
    /// naming the other. DataContractJsonSerializer for the reason the other payloads record:
    /// the assembly targets net462 and carries no JSON dependency.
    /// </summary>
    [DataContract]
    public sealed class CaseHeaderRequestPayload
    {
        [DataMember(Name = "caseId")]
        public string CaseId { get; set; }

        [DataMember(Name = "taxCheckRequired")]
        public int TaxCheckRequired { get; set; }

        [DataMember(Name = "taxTeamDisposition")]
        public int TaxTeamDisposition { get; set; }

        /// <summary>
        /// The rest of the header, as a flat JSON object of logical name to value - the
        /// same shape al_UpdateCaseDetails takes for its Fields parameter (item 5,
        /// 2026-09-19), so the portal and the Code App send a header edit the same way.
        /// Absent or empty means "this edit is only about the Tax fields above".
        /// </summary>
        [DataMember(Name = "fields")]
        public string Fields { get; set; }

        /// <summary>The Fields payload as a dictionary, empty when none was sent.</summary>
        public Dictionary<string, string> ParsedFields()
        {
            if (string.IsNullOrWhiteSpace(Fields))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                return UpdateCaseDetailsPlugin.SimpleJson.ParseObject(Fields);
            }
            catch (FormatException)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "The case header edit could not be read.");
            }
        }

        public static CaseHeaderRequestPayload Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var serializer = new DataContractJsonSerializer(typeof(CaseHeaderRequestPayload));
                    return serializer.ReadObject(stream) as CaseHeaderRequestPayload;
                }
            }
            catch (SerializationException)
            {
                return null;
            }
        }
    }
}
