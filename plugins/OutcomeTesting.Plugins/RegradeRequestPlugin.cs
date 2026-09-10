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
    /// The final regraded outcome, recorded from the portal (OD-041, AD-031, BR-005,
    /// BR-007). Registered as a synchronous post-operation step on Update of
    /// <c>contact</c>, filtered to <c>al_regraderequest</c>.
    ///
    /// This is the fourth browser-driven write on the site and it takes the shape the first
    /// three settled on. The page writes one text column on the signed-in user's own contact
    /// row and the work happens here, as the application user. A Custom API cannot be invoked
    /// from a Power Pages page at all, so <c>al_RegradeCase</c> was reachable only from the
    /// Code App — which is what OD-041 records: approve and reject were a portal act while
    /// the regrade that follows them was a back-office one, and a supervisor holding only the
    /// portal could not finish the case they had just approved.
    ///
    /// <b>Authorization is the contact's web role, not the caller's privileges.</b> A Power
    /// Pages write reaches Dataverse as the site's application user, so
    /// <c>InitiatingUserId</c> names the site and a caller check would enforce nothing
    /// (AD-053). The signatory is <see cref="IPluginExecutionContext.PrimaryEntityId"/> — the
    /// contact row actually written, which the Self-scoped contact permission confines to the
    /// signed-in user — and the role is checked against that contact's web roles, exactly as
    /// <see cref="SignoffRequestPlugin"/> checks it for the attestation. The contact column
    /// allowlist is shared across every trigger column on the table, so a reviewer who can
    /// write the claim request cannot regrade through this one.
    ///
    /// None of the regrade's rules live here. <see cref="RegradeCasePlugin.Regrade"/> holds
    /// them, and the Custom API calls the same method, so the two front ends cannot drift on
    /// what a regrade means: the mandatory reason, the preserved initial outcome (BR-007),
    /// the refusal to regrade something never graded, the audit event, and the close of a
    /// case sitting at Awaiting Recheck.
    /// </summary>
    public class RegradeRequestPlugin : PluginBase
    {
        private const string ContactEntity = "contact";
        private const string OutcomeEntity = "al_outcome";
        public const string RequestAttr = "al_regraderequest";

        public RegradeRequestPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(RegradeRequestPlugin))
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
            if (entity == null || !string.Equals(entity.LogicalName, ContactEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!entity.Contains(RequestAttr))
            {
                return;
            }

            // The clear-down in Apply writes this column back as empty, which re-enters this
            // step once at depth 2. An empty request is not a regrade, so it stops here.
            var raw = entity.GetAttributeValue<string>(RequestAttr);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            Apply(service, context.PrimaryEntityId, raw, context);
        }

        /// <summary>
        /// Records the regrade the contact requested. Pure of the plug-in context's own
        /// parsing so the rules can be tested against the fake service.
        /// </summary>
        public static RegradeResult Apply(
            IOrganizationService service, Guid contactId, string raw, IPluginExecutionContext context)
        {
            var payload = RegradeRequestPayload.Parse(raw);

            Guid outcomeId;
            if (payload == null
                || string.IsNullOrWhiteSpace(payload.OutcomeId)
                || !Guid.TryParse(payload.OutcomeId.Trim(), out outcomeId)
                || outcomeId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A regrade must name the outcome it applies to.");
            }

            if (string.IsNullOrWhiteSpace(payload.FinalOutcome))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A regrade must record the final outcome (BR-005).");
            }

            // AD-031 makes the reason mandatory and RegradeCasePlugin refuses without one.
            // Saying so here names the field the page left blank rather than letting the
            // shared rule report it after the clear-down has already run.
            if (string.IsNullOrWhiteSpace(payload.Reason))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A regrade must record why the outcome was changed (AD-031).");
            }

            // The role is the contact's, read from the platform, never a claim in the
            // payload. BR-005's final outcome is the T&C Supervisor's to set, the same
            // authority that approved the sign-off which brought the case to recheck.
            EnsureSupervisorRole(service, contactId);

            var actorName = ActorName(service, contactId);

            // Cleared before the regrade is applied, so the column never keeps a request
            // after it has been acted on, and a second regrade is a real second write rather
            // than an unchanged value Dataverse may not raise an Update for. Both writes
            // share this transaction: a refusal below rolls the clear back with it.
            service.Update(new Entity(ContactEntity, contactId)
            {
                [RequestAttr] = null,
            });

            // One intent per outcome and grade, so a retry after a dropped response replays
            // the original regrade instead of writing a second Audit Event (NFR-REL-01). The
            // browser cannot supply a stable key across page loads, so it is derived here —
            // the same reasoning, and the same shape, as the portal submit's key.
            var idempotencyKey = payload.IdempotencyKey;
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                idempotencyKey = "portal-regrade-" + outcomeId.ToString("N");
            }

            // The application user for both: it holds the privileges, and on this path there
            // is no caller whose privileges could mean anything (AD-053). The contact is the
            // actor on the audit event, which is where the person's identity has to survive.
            return RegradeCasePlugin.Regrade(
                service,
                service,
                outcomeId,
                payload.FinalOutcome,
                payload.Reason,
                payload.ExpectedRowVersion,
                idempotencyKey.Trim(),
                context,
                contactId,
                actorName);
        }

        /// <summary>
        /// AD-031: the final outcome is the T&amp;C authority's to set. Refused with the
        /// words the page can show the supervisor directly.
        /// </summary>
        public static void EnsureSupervisorRole(IOrganizationService service, Guid contactId)
        {
            foreach (var role in WebRoleRegistry.RolesForContact(service, contactId))
            {
                if (string.Equals(role, WebRoleRegistry.TcSupervisorRole, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix +
                "Recording the final outcome needs the " + WebRoleRegistry.TcSupervisorRole + " role on your portal account.");
        }

        private static string ActorName(IOrganizationService service, Guid contactId)
        {
            try
            {
                var contact = service.Retrieve(ContactEntity, contactId, new ColumnSet("fullname"));
                return contact.GetAttributeValue<string>("fullname");
            }
            catch (Exception)
            {
                // A missing name must not cost the regrade; the audit event still carries the
                // contact id as the actor.
                return null;
            }
        }
    }

    /// <summary>
    /// What the portal writes onto <c>contact.al_regraderequest</c>: the outcome being
    /// regraded, the BR-005 grade in words, the mandatory reason, and the outcome's row
    /// version so a regrade against a stale read is refused rather than overwriting another
    /// correction. DataContractJsonSerializer for the reason
    /// <see cref="SignoffRequestPayload"/> records: the assembly targets net462 and carries
    /// no JSON dependency.
    /// </summary>
    [DataContract]
    public sealed class RegradeRequestPayload
    {
        [DataMember(Name = "outcomeId")]
        public string OutcomeId { get; set; }

        [DataMember(Name = "finalOutcome")]
        public string FinalOutcome { get; set; }

        [DataMember(Name = "reason")]
        public string Reason { get; set; }

        [DataMember(Name = "expectedRowVersion")]
        public string ExpectedRowVersion { get; set; }

        [DataMember(Name = "idempotencyKey")]
        public string IdempotencyKey { get; set; }

        public static RegradeRequestPayload Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var serializer = new DataContractJsonSerializer(typeof(RegradeRequestPayload));
                    return serializer.ReadObject(stream) as RegradeRequestPayload;
                }
            }
            catch (SerializationException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
