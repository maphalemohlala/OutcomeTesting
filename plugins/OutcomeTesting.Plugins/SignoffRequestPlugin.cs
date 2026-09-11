using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The T&amp;C attestation from the portal, without an association the browser is not
    /// allowed to make (AD-099). Registered as a synchronous post-operation step on Update
    /// of <c>contact</c>, filtered to <c>al_signoffrequest</c>.
    ///
    /// The page used to POST an <c>al_signoff</c> carrying an <c>@odata.bind</c> to the
    /// remediation action (AD-074). No sign-off was ever recorded that way: the environment
    /// held zero <c>al_signoff</c> rows on 2026-09-09 while a T&amp;C Supervisor was being
    /// answered 403, with the live permission bindings verified identical to source. That is
    /// the third browser-side association on this site, after the answer create (AD-053)
    /// and the claim (AD-076), and Power Pages refused the first two with 90040106 whatever
    /// the table permissions said. This applies the same resolution: the page writes one
    /// text column on the signed-in user's own contact row, and the create happens here,
    /// as the application user, where no portal table permission is consulted.
    ///
    /// <b>Authorization moves to where it can be enforced.</b> AD-074 bound create on
    /// <c>al_signoff</c> to the T&amp;C Supervisor role through a Parent-scoped permission;
    /// on this site that binding was never reached. Here the signatory is
    /// <see cref="IPluginExecutionContext.PrimaryEntityId"/> - the contact row that was
    /// actually written, which the Self-scoped contact permission limits to the signed-in
    /// user - and the role is checked server-side against that contact's web roles, the way
    /// <see cref="ClaimCasePlugin"/> checks the reviewer roles. A reviewer who can write the
    /// contact's other request column cannot sign off through this one.
    ///
    /// Nothing about the sign-off's rules lives here. The row is created with only the
    /// decision, the notes and the action, exactly the shape the browser used to send, so
    /// <see cref="SignoffGuardPlugin"/> and <see cref="SignoffProgressPlugin"/> run unchanged.
    /// </summary>
    public class SignoffRequestPlugin : PluginBase
    {
        private const string ContactEntity = "contact";
        private const string SignoffEntity = "al_signoff";
        private const string ActionEntity = "al_remediationaction";
        public const string RequestAttr = "al_signoffrequest";

        public SignoffRequestPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SignoffRequestPlugin))
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
            // step once at depth 2. An empty request is not a sign-off, so it stops here.
            var raw = entity.GetAttributeValue<string>(RequestAttr);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            Apply(service, context.PrimaryEntityId, raw);
        }

        /// <summary>
        /// Records the sign-off the contact requested. Pure of the plug-in context so the
        /// rules can be tested against the fake service.
        /// </summary>
        public static Guid Apply(IOrganizationService service, Guid contactId, string raw)
        {
            var payload = SignoffRequestPayload.Parse(raw);

            Guid actionId;
            if (payload == null
                || string.IsNullOrWhiteSpace(payload.ActionId)
                || !Guid.TryParse(payload.ActionId.Trim(), out actionId)
                || actionId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A sign-off must name the remediation action it relates to.");
            }

            if (payload.Decision == 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A sign-off must record whether the remediation was approved or rejected.");
            }

            // The role is the contact's, read from the platform, never a claim in the payload.
            // The contact permission the page writes through is shared with the reviewer
            // roles' claim request (one allowlist per table), so the check has to be here.
            EnsureSupervisorRole(service, contactId);

            // Cleared before the sign-off is created, so the column never keeps a request
            // after it has been acted on, and a second sign-off is a real second write rather
            // than an unchanged value Dataverse may not raise an Update for. Both writes share
            // this transaction: a refusal below rolls the clear back with it.
            service.Update(new Entity(ContactEntity, contactId)
            {
                [RequestAttr] = null,
            });

            // Only the decision, the notes and the action - the shape the browser used to
            // POST. The name, code, case and timestamp are stamped by SignoffGuardPlugin.
            var signoff = new Entity(SignoffEntity)
            {
                ["al_signoffdecision"] = new OptionSetValue(payload.Decision),
                ["al_remediationactionid"] = new EntityReference(ActionEntity, actionId),
            };
            if (!string.IsNullOrWhiteSpace(payload.Notes))
            {
                signoff["al_notes"] = payload.Notes.Trim();
            }

            // Carried through unvalidated, exactly as the decision and the notes are: the
            // rules are SignoffGuardPlugin's, which refuses a grade on a rejection and a
            // value the BR-005 scale does not define, and the consequences are
            // SignoffProgressPlugin's. This plug-in only exists because the browser cannot
            // make the association.
            if (payload.FinalOutcome != 0)
            {
                signoff["al_finaloutcome"] = new OptionSetValue(payload.FinalOutcome);
            }

            return service.Create(signoff);
        }

        /// <summary>
        /// BR-008: only the T&amp;C Supervisor attests. Refused with the same words the page
        /// used to give for a 403, now for the reason that is actually true.
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
                "Signing off needs the " + WebRoleRegistry.TcSupervisorRole + " role on your portal account.");
        }
    }

    /// <summary>
    /// What the portal writes onto <c>contact.al_signoffrequest</c>: the action being signed
    /// off, the decision value and the notes. DataContractJsonSerializer for the reason
    /// <see cref="AnswerRequestPayload"/> records: the assembly targets net462 and carries
    /// no JSON dependency.
    /// </summary>
    [DataContract]
    public sealed class SignoffRequestPayload
    {
        [DataMember(Name = "actionId")]
        public string ActionId { get; set; }

        [DataMember(Name = "decision")]
        public int Decision { get; set; }

        [DataMember(Name = "notes")]
        public string Notes { get; set; }

        /// <summary>
        /// The final BR-005 outcome, recorded as the supervisor approves (project owner,
        /// 2026-09-11). Zero when the page sent none, which is every rejection and any
        /// approval on a case with no graded outcome to override - a remediated Tax-only
        /// case has no al_outcome row at all (AD-055).
        /// </summary>
        [DataMember(Name = "finalOutcome")]
        public int FinalOutcome { get; set; }

        public static SignoffRequestPayload Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var serializer = new DataContractJsonSerializer(typeof(SignoffRequestPayload));
                    return serializer.ReadObject(stream) as SignoffRequestPayload;
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
