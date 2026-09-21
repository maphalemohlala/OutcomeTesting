using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The gate both supervisor commands pass: the signatory must be the T&amp;C Manager that
    /// <c>al_advisermapping</c> names for THIS case's adviser (AD-198, AD-202).
    ///
    /// <para>
    /// <b>Unlike <see cref="TcManagerRouting"/>, this type does decide.</b> Routing answers
    /// "who is the manager" and draws no conclusion; this turns that answer into a refusal.
    /// The two are kept apart deliberately - reading who supervises whom stays open, acting
    /// on a case does not.
    /// </para>
    /// <para>
    /// It exists as one method rather than two copies because the rule is one rule. It was
    /// briefly two: <see cref="SignoffRequestPlugin"/> gained the mapping check on 2026-09-21
    /// and <see cref="RegradeRequestPlugin"/> did not, which left anybody holding the
    /// supervisor role able to record the regraded outcome on any case - including their own
    /// cases, and including the very cases whose sign-off they had just been refused. That
    /// was found on the portal rather than reasoned about: the Regraded outcome form rendered
    /// on two cases whose mapped manager was somebody else, while the sign-off form on the
    /// same pages was correctly withheld.
    /// </para>
    /// <para>
    /// The consequence, which is the same one the sign-off already carried: a case whose
    /// adviser has no mapping can be regraded by nobody. That is intended - the final outcome
    /// is an attestation and an attestation needs a supervisor - and it is another reason
    /// <c>al_advisermapping</c> is operational data rather than notification convenience (F58).
    /// </para>
    /// <para>
    /// Only the two PORTAL paths pass through here. <see cref="RegradeCasePlugin.Regrade"/>
    /// and the <c>al_RegradeCase</c> Custom API behind the Code App are untouched: a
    /// back-office user is authorised by their Dataverse role, which is a different boundary
    /// and not the one that was leaking.
    /// </para>
    /// </summary>
    public static class SupervisorMapping
    {
        /// <summary>
        /// Refuses unless <paramref name="contactId"/> is the T&amp;C Manager mapped to the
        /// adviser on <paramref name="caseRef"/>.
        /// </summary>
        /// <param name="act">
        /// What is being refused, as the sentence's subject - "Signing a case off",
        /// "Recording the final outcome". The message names the act because the reader is
        /// looking at one of two controls and has to know which one this is about.
        /// </param>
        public static void EnsureManagesCase(
            IOrganizationService service, Guid contactId, EntityReference caseRef, string act)
        {
            var routing = TcManagerRouting.ForCase(service, caseRef);

            // Matched on the manager alone, never on whether they can be EMAILED. That is a
            // routing concern - the reason ManagerNotReachable exists - and has nothing to do
            // with whether this account may act.
            if (routing.Manager != null && routing.Manager.Id == contactId)
            {
                return;
            }

            // The adviser's own address is NOT echoed when a manager exists: the refusal only
            // has to say that this is not the account's case to act on, and naming the adviser
            // would let anyone holding the role enumerate who supervises whom. Where there is
            // no mapping at all the routing's own sentence IS the useful answer - the gap is
            // in al_advisermapping and the reader is the person who can have it filled in.
            var detail = routing.Manager == null
                ? " " + routing.Reason
                : " Your portal account is not the T&C Manager mapped to this case's adviser.";

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix +
                act + " is the T&C Manager mapped to its adviser." + detail);
        }
    }
}
