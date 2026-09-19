using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Records who carries a fail, from the portal (item 8, 2026-09-19).
    ///
    /// The portal cannot invoke a Custom API, so it cannot call al_SetFailAccountability the
    /// way the Code App does (AD-053). It PATCHes this JSON onto its own contact row instead
    /// and this plug-in does the write - the same shape as the claim, the sign-off, the
    /// regrade and the case header edit, and for the same reason.
    ///
    /// Its own trigger column rather than a field on al_caseheaderrequest: that request is
    /// the case HEADER, writes al_outcomecase and re-derives the route. This writes
    /// al_outcome and moves nothing. Sharing an envelope would have put two unrelated
    /// decisions behind one guard and one audit line.
    ///
    /// The guard is the one item 5 built: an open review on this case carrying the caller.
    /// Accountability is recorded while the check is being done, so the person recording it
    /// is the person doing it, and once their review is submitted it is no longer theirs to
    /// move.
    /// </summary>
    public class AccountabilityRequestPlugin : PluginBase
    {
        public const string RequestAttr = "al_accountabilityrequest";

        private const string ContactEntity = "contact";
        private const string OutcomeEntity = "al_outcome";
        private const string CaseAttr = "al_outcomecaseid";
        private const int CommandSetFailAccountability = 120910792;

        public AccountabilityRequestPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AccountabilityRequestPlugin))
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

            var payload = AccountabilityRequestPayload.Parse(raw);
            if (payload == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "The accountability request could not be read.");
            }

            Apply(service, context, context.PrimaryEntityId, payload);
        }

        private static void Apply(
            IOrganizationService service,
            IPluginExecutionContext context,
            Guid contactId,
            AccountabilityRequestPayload payload)
        {
            Guid reviewId;
            if (Guid.TryParse(payload.ReviewId, out reviewId) && reviewId != Guid.Empty)
            {
                Park(service, contactId, reviewId, payload);
                return;
            }

            Guid outcomeId;
            if (!Guid.TryParse(payload.OutcomeId, out outcomeId) || outcomeId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "An accountability request must name the check it applies to.");
            }

            var outcome = service.Retrieve(
                OutcomeEntity, outcomeId,
                new ColumnSet(Outcomes.InitialOutcomeAttr, Outcomes.FinalOutcomeAttr, CaseAttr));

            var caseRef = outcome.GetAttributeValue<EntityReference>(CaseAttr);
            if (caseRef == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "That check is not attached to a case.");
            }

            // The same guard the case header edit uses: recording who carries a fail is part
            // of doing the check, so only the checker doing it may record it.
            CaseHeaderRequestPlugin.EnsureAssignedToCase(service, contactId, caseRef.Id);

            var fileQualityFailed = FileQuality.FailedOn(service, caseRef.Id);
            var refusal = SetFailAccountabilityPlugin.RefusalFor(
                Outcomes.EffectiveOutcome(outcome), fileQualityFailed);

            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
            }

            // Cleared before the outcome is written, so the column never keeps a request
            // after it has been acted on. Both writes share this transaction: a refusal
            // rolls the clear back with it.
            service.Update(new Entity(ContactEntity, contactId)
            {
                [RequestAttr] = null,
            });

            var fqContact = ParseOptionalContact(payload.FqContactId, "File Quality");
            var aqContact = ParseOptionalContact(payload.AqContactId, "Advice Quality");

            service.Update(new Entity(OutcomeEntity, outcomeId)
            {
                ["al_fqadviseraccountable"] = payload.FqAdviser,
                ["al_fqparaplanneraccountable"] = payload.FqParaplanner,
                ["al_aqadviseraccountable"] = payload.AqAdviser,
                ["al_aqparaplanneraccountable"] = payload.AqParaplanner,

                // Written every time, including as null, so clearing a named person puts the
                // case's own adviser or paraplanner back into the extract.
                ["al_fqaccountablecontactid"] = fqContact,
                ["al_aqaccountablecontactid"] = aqContact,
            });

            var details = "FQ adviser " + payload.FqAdviser + ", FQ paraplanner " + payload.FqParaplanner
                + ", AQ adviser " + payload.AqAdviser + ", AQ paraplanner " + payload.AqParaplanner
                + ", FQ named " + Describe(fqContact) + ", AQ named " + Describe(aqContact)
                + " (from the portal)";

            // Named to the contact, not the caller: a portal write reaches Dataverse as the
            // site's application user (AD-053).
            var actorName = CommandHelpers.ResolveActorName(service, contactId);
            CommandHelpers.WriteAuditEvent(
                service,
                CommandSetFailAccountability,
                "SetFailAccountability",
                OutcomeEntity,
                outcomeId,
                null,
                details,
                "accountability-" + outcomeId.ToString("N") + "-" + DateTime.UtcNow.Ticks.ToString(),
                context,
                contactId,
                actorName);
        }

        /// <summary>
        /// Holds the choice on the review until the submit creates the outcome to put it on.
        ///
        /// No audit event is written here. Nothing has been decided yet - this is the
        /// checker filling in a field on a form they have not submitted, exactly like every
        /// answer above it. The audit follows when the submit applies it, which is the
        /// moment the record becomes a record.
        /// </summary>
        private static void Park(
            IOrganizationService service,
            Guid contactId,
            Guid reviewId,
            AccountabilityRequestPayload payload)
        {
            var review = service.Retrieve(
                "al_reviewinstance", reviewId, new ColumnSet("al_outcomecaseid", "al_submittedon"));

            if (review.GetAttributeValue<DateTime?>("al_submittedon").HasValue)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "This check has been submitted, so who carries its fail can no longer be changed here.");
            }

            var caseRef = review.GetAttributeValue<EntityReference>("al_outcomecaseid");
            if (caseRef == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "That check is not attached to a case.");
            }

            CaseHeaderRequestPlugin.EnsureAssignedToCase(service, contactId, caseRef.Id);

            // Validated now rather than at submit, so a bad id is refused while the person
            // who chose it is still looking at the form.
            ParseOptionalContact(payload.FqContactId, "File Quality");
            ParseOptionalContact(payload.AqContactId, "Advice Quality");

            service.Update(new Entity(ContactEntity, contactId) { [RequestAttr] = null });

            service.Update(new Entity("al_reviewinstance", reviewId)
            {
                ["al_pendingaccountability"] = Serialise(payload),
            });
        }

        /// <summary>The payload as stored, without the review id it arrived under.</summary>
        private static string Serialise(AccountabilityRequestPayload payload)
        {
            var parked = new AccountabilityRequestPayload
            {
                FqAdviser = payload.FqAdviser,
                FqParaplanner = payload.FqParaplanner,
                AqAdviser = payload.AqAdviser,
                AqParaplanner = payload.AqParaplanner,
                FqContactId = payload.FqContactId,
                AqContactId = payload.AqContactId,
            };

            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(AccountabilityRequestPayload)).WriteObject(stream, parked);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>
        /// Applies a parked choice to the outcome just created, and returns whether there
        /// was one. Called by SubmitReviewPlugin, which owns the moment al_outcome exists.
        /// </summary>
        public static bool ApplyParked(IOrganizationService service, Entity review, Entity outcome)
        {
            if (service == null || review == null || outcome == null)
            {
                return false;
            }

            var parked = AccountabilityRequestPayload.Parse(
                review.GetAttributeValue<string>("al_pendingaccountability"));

            if (parked == null)
            {
                return false;
            }

            outcome["al_fqadviseraccountable"] = parked.FqAdviser;
            outcome["al_fqparaplanneraccountable"] = parked.FqParaplanner;
            outcome["al_aqadviseraccountable"] = parked.AqAdviser;
            outcome["al_aqparaplanneraccountable"] = parked.AqParaplanner;
            outcome["al_fqaccountablecontactid"] = ParseOptionalContact(parked.FqContactId, "File Quality");
            outcome["al_aqaccountablecontactid"] = ParseOptionalContact(parked.AqContactId, "Advice Quality");
            return true;
        }

        private static EntityReference ParseOptionalContact(string raw, string discipline)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            Guid id;
            if (!Guid.TryParse(raw.Trim(), out id) || id == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix +
                    "The person named for " + discipline + " could not be read.");
            }

            return new EntityReference(ContactEntity, id);
        }

        private static string Describe(EntityReference contact)
        {
            return contact == null ? "(the case's own)" : contact.Id.ToString("D");
        }
    }

    /// <summary>
    /// What the portal writes onto <c>contact.al_accountabilityrequest</c>: the check, the
    /// four flags, and the two people. DataContractJsonSerializer for the reason the other
    /// payloads record - the assembly targets net462 and carries no JSON dependency.
    /// </summary>
    [DataContract]
    public sealed class AccountabilityRequestPayload
    {
        [DataMember(Name = "outcomeId")]
        public string OutcomeId { get; set; }

        /// <summary>
        /// The review being checked, when its outcome does not exist yet. The portal names
        /// this one: al_outcome is created by SubmitReviewPlugin at submit, and the checker
        /// chooses who carries the fail BEFORE pressing it. The choice is parked on the
        /// review and applied to the outcome the submit creates.
        /// </summary>
        [DataMember(Name = "reviewId")]
        public string ReviewId { get; set; }

        [DataMember(Name = "fqAdviser")]
        public bool FqAdviser { get; set; }

        [DataMember(Name = "fqParaplanner")]
        public bool FqParaplanner { get; set; }

        [DataMember(Name = "aqAdviser")]
        public bool AqAdviser { get; set; }

        [DataMember(Name = "aqParaplanner")]
        public bool AqParaplanner { get; set; }

        /// <summary>Empty means the case's own adviser or paraplanner.</summary>
        [DataMember(Name = "fqContactId")]
        public string FqContactId { get; set; }

        [DataMember(Name = "aqContactId")]
        public string AqContactId { get; set; }

        public static AccountabilityRequestPayload Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AccountabilityRequestPayload));
                    return serializer.ReadObject(stream) as AccountabilityRequestPayload;
                }
            }
            catch (SerializationException)
            {
                return null;
            }
        }
    }
}
