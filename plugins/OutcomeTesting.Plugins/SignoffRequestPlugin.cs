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
    ///
    /// <b>One thing was added on 2026-09-21</b>, and for the same reason the plug-in exists
    /// at all. The project owner directed that "Recheck required?" and "Do the remedial
    /// actions change the advice?" are the T&amp;C Manager's answers, not the adviser's, and
    /// this is the only place on the portal where that role has already been proved. So the
    /// payload carries them and <see cref="RecordFormAnswers"/> writes them; every other
    /// route to those two columns is refused by
    /// <see cref="RemediationResponseGuardPlugin.TcOnlyRefusal"/>.
    /// </summary>
    public class SignoffRequestPlugin : PluginBase
    {
        private const string ContactEntity = "contact";
        private const string SignoffEntity = "al_signoff";
        private const string ActionEntity = "al_remediationaction";
        private const string CaseLookup = "al_outcomecaseid";
        private const string ReviewLookup = "al_reviewinstanceid";
        public const string RequestAttr = "al_signoffrequest";

        // Who signed. An id and a name rather than a lookup, which is the shape
        // al_auditevent already uses for al_ActorId and al_ActorName, and for the same
        // reason: the signatory is a Contact on the portal path and a systemuser on a
        // command path, so no one lookup spans both.
        //
        // Until this existed al_signoff recorded no signatory at all. The Code App fell back
        // to owneridname, and because a portal write reaches Dataverse as the site's
        // application user (AD-053) every sign-off made through the portal read as
        // "# PowerPages Data Runtime PROD" - the row genuinely did not know who had approved
        // it, which is not a record BR-008 or AD-031 can rest on.
        public const string SignedByIdAttr = "al_signedbycontactid";
        public const string SignedByNameAttr = "al_signedbyname";

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

            // ...and holding the role is no longer enough: it must be THIS case's supervisor
            // (project owner, 2026-09-21).
            EnsureMappedToCase(service, contactId, actionId);

            // The two answers only this role may give (project owner, 2026-09-21), written
            // BEFORE the sign-off is created: SignoffProgressPlugin runs on that Create and
            // reads al_recheckrequired to decide whether the case closes here or waits at
            // Awaiting Recheck, so writing them afterwards would route the case on the
            // previous answer. Same transaction, so a refusal below rolls them back.
            RecordFormAnswers(service, actionId, payload);

            // Cleared before the sign-off is created, so the column never keeps a request
            // after it has been acted on, and a second sign-off is a real second write rather
            // than an unchanged value Dataverse may not raise an Update for. Both writes share
            // this transaction: a refusal below rolls the clear back with it.
            service.Update(new Entity(ContactEntity, contactId)
            {
                [RequestAttr] = null,
            });

            // Only the decision, the notes, the action and the signatory - the shape the
            // browser used to POST, plus who signed it. The name, code, case and timestamp
            // are stamped by SignoffGuardPlugin.
            //
            // The signatory is contactId, never anything the payload supplied: it is
            // PrimaryEntityId, the contact row the Self-scoped permission limited to the
            // signed-in user, and it is the same value EnsureSupervisorRole was checked
            // against just above. A page cannot sign in someone else's name.
            var signoff = new Entity(SignoffEntity)
            {
                ["al_signoffdecision"] = new OptionSetValue(payload.Decision),
                ["al_remediationactionid"] = new EntityReference(ActionEntity, actionId),
                [SignedByIdAttr] = contactId.ToString("D"),
            };

            var signatory = CommandHelpers.ResolveActorName(service, contactId);
            if (!string.IsNullOrWhiteSpace(signatory))
            {
                signoff[SignedByNameAttr] = signatory;
            }
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
        /// Writes the T&amp;C Manager's two remediation-form answers onto every action of the
        /// check being signed off (project owner, 2026-09-21).
        ///
        /// Onto the whole check, not the one action, because the form asks them once: the
        /// agreed paper form carries a single "Recheck required?" and a single "Do the
        /// remedial actions change the advice?" under a table of numbered issues, and the
        /// portal draws it the same way. <see cref="SignoffProgressPlugin.RecheckWaived"/>
        /// already reads them across the check for that reason.
        ///
        /// Scoped to the check by <c>al_reviewinstanceid</c>, matching RecheckWaived exactly:
        /// the two legs of a Tax-then-AQS route are separate remediations and the Tax leg's
        /// answer is not this check's. An action carrying no review — every action raised
        /// before the column existed — is written only when the signed action carries none
        /// either, so the two agree about what "this check" means.
        ///
        /// Zero means the page sent nothing, and nothing is left alone rather than cleared: a
        /// rejection does not ask these questions, and a rejection must not erase the answers
        /// the previous approval attempt gave.
        /// </summary>
        public static void RecordFormAnswers(
            IOrganizationService service, Guid actionId, SignoffRequestPayload payload)
        {
            if (service == null || payload == null
                || (payload.RecheckRequired == 0 && payload.ChangesAdvice == 0))
            {
                return;
            }

            var signed = service.Retrieve(
                ActionEntity, actionId, new ColumnSet(CaseLookup, ReviewLookup));

            var caseRef = signed.GetAttributeValue<EntityReference>(CaseLookup);
            if (caseRef == null)
            {
                return;
            }

            var reviewRef = signed.GetAttributeValue<EntityReference>(ReviewLookup);

            var siblings = new QueryExpression(ActionEntity)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = new FilterExpression(),
            };
            siblings.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            siblings.Criteria.AddCondition(CaseLookup, ConditionOperator.Equal, caseRef.Id);
            siblings.Criteria.AddCondition(
                ReviewLookup,
                reviewRef == null ? ConditionOperator.Null : ConditionOperator.Equal,
                reviewRef == null ? new object[0] : new object[] { reviewRef.Id });

            foreach (var action in service.RetrieveMultiple(siblings).Entities)
            {
                var write = new Entity(ActionEntity, action.Id);
                if (payload.RecheckRequired != 0)
                {
                    write["al_recheckrequired"] = new OptionSetValue(payload.RecheckRequired);
                }

                if (payload.ChangesAdvice != 0)
                {
                    write["al_changesadvice"] = new OptionSetValue(payload.ChangesAdvice);
                }

                service.Update(write);
            }
        }

        /// <summary>
        /// BR-008: the signatory must be the T&amp;C Manager mapped to the case's own adviser,
        /// not merely somebody holding the supervisor role.
        ///
        /// <para>
        /// <b>This reverses the Phase 1 position</b> that <see cref="TcManagerRouting"/> still
        /// describes for its own purposes: every T&amp;C Manager reads every case, and the
        /// mapping decided only who was TOLD a sign-off was waiting. The owner changed it on
        /// 2026-09-21, having found that a service account holding the role could attest to a
        /// case whose adviser it supervises nothing of: <i>"service accounts holds the T&amp;C
        /// role but they are not linked to the adviser handling the case, so they should not
        /// be able to perform sign off"</i>. Reading stays open; attesting does not. An
        /// attestation is a supervisory act by a named person over a named adviser's work, and
        /// a signature from outside that relationship records something that never happened.
        /// </para>
        /// <para>
        /// A mapped manager with no work email is still the right person. Whether they can be
        /// EMAILED is a routing concern - the reason <c>ManagerNotReachable</c> exists - and
        /// has nothing to do with whether they may sign, so the manager is compared whenever
        /// the mapping resolved one at all.
        /// </para>
        /// <para>
        /// No mapping means nobody may sign, which is deliberate and is why the refusal
        /// carries the routing's own sentence: the gap is in <c>al_advisermapping</c> and the
        /// person reading the message is the one who can have it filled in (F58).
        /// </para>
        /// </summary>
        public static void EnsureMappedToCase(
            IOrganizationService service, Guid contactId, Guid actionId)
        {
            var action = service.Retrieve(ActionEntity, actionId, new ColumnSet(CaseLookup));
            var caseRef = action.GetAttributeValue<EntityReference>(CaseLookup);
            if (caseRef == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "This remedial action is not attached to a case, so its T&C Manager cannot be resolved.");
            }

            var routing = TcManagerRouting.ForCase(service, caseRef);

            if (routing.Manager != null && routing.Manager.Id == contactId)
            {
                return;
            }

            // The adviser's own address is NOT echoed when a manager exists: the refusal only
            // has to say that it is not this account's case to sign, and naming the adviser
            // would let anyone holding the role enumerate who supervises whom.
            var detail = routing.Manager == null
                ? " " + routing.Reason
                : " Your portal account is not the T&C Manager mapped to this case's adviser.";

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix +
                "Signing a case off is the T&C Manager mapped to its adviser." + detail);
        }

        /// <summary>
        /// BR-008: only the T&amp;C Supervisor attests. Refused with the same words the page
        /// used to give for a 403, now for the reason that is actually true.
        ///
        /// The first of two gates. <see cref="EnsureMappedToCase"/> is the second, and the
        /// order matters for the message: "you do not hold the role" is a different problem
        /// from "you hold it but not over this adviser", and answering the second to somebody
        /// who has neither would send them looking for a mapping they could not use.
        /// </summary>
        public static void EnsureSupervisorRole(IOrganizationService service, Guid contactId)
        {
            if (WebRoleRegistry.HasRole(service, contactId, WebRoleRegistry.TcSupervisorRole))
            {
                return;
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
        /// "Recheck required?" — <c>al_remediationaction.al_recheckrequired</c>. Zero when the
        /// page sent none, which leaves whatever the actions already carry.
        ///
        /// The T&amp;C Manager's answer from 2026-09-21, not the adviser's. It always decided
        /// something the supervisor owns — <see cref="SignoffProgressPlugin.RecheckWaived"/>
        /// reads it to choose between closing the case on this approval and stopping it at
        /// Awaiting Recheck (AD-138) — so it now travels with the attestation that acts on it.
        /// </summary>
        [DataMember(Name = "recheckRequired")]
        public int RecheckRequired { get; set; }

        /// <summary>
        /// "Do the remedial actions change the advice?" —
        /// <c>al_remediationaction.al_changesadvice</c>. Zero when the page sent none.
        /// </summary>
        [DataMember(Name = "changesAdvice")]
        public int ChangesAdvice { get; set; }

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
