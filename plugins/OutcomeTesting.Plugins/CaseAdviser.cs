using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The email a case's adviser is known by, and keeping it in step with the adviser's name
    /// (project owner, 2026-09-30).
    ///
    /// <para>
    /// A case names its adviser twice: <c>al_advisername</c>, which routes remediation and the
    /// adviser's own access (Remediation.AdviserContact resolves it to one contact), and
    /// <c>al_adviseremail</c>, which is the key <c>al_advisermapping</c> is looked up by - so it
    /// decides the T&amp;C Manager, who may sign off and regrade, who the supervisor on the case
    /// is, and who is told. Nothing kept the two in step. TEST's 29 Sep import carried names
    /// and no addresses, so 24 cases had an adviser who could complete their actions and a
    /// T&amp;C Manager nobody could resolve: case 256617620 could not be signed off by Adam
    /// Strumidlo although he is its adviser and mapped as his own manager. And changing the
    /// adviser moved the actions to the new person while the email stayed the old one's, which
    /// would have sent the sign-off to the previous adviser's manager.
    /// </para>
    /// <para>
    /// One rule, the one <see cref="NotificationOutbox.MatchPerson"/> already applies: email
    /// first, name second. The stored email where there is one; otherwise the email of the one
    /// active contact the name resolves to; otherwise none. Never a guess between two people of
    /// the same name.
    /// </para>
    /// </summary>
    public static class CaseAdviser
    {
        public const string EmailAttr = "al_adviseremail";
        public const string NameAttr = "al_advisername";

        /// <summary>The adviser's email for a case carrying these two values, or null.</summary>
        public static string EmailFor(IOrganizationService service, string storedEmail, string name)
        {
            var stored = (storedEmail ?? string.Empty).Trim();
            if (stored.Length > 0)
            {
                return stored;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var match = NotificationOutbox.MatchAdviser(service, null, name);
            return match.IsMatch && !string.IsNullOrWhiteSpace(match.Email) ? match.Email.Trim() : null;
        }

        /// <summary>
        /// Gives a case about to be created the adviser's email where the row names the adviser
        /// but carries no address. An address the row does carry is kept: it came from IO.
        /// </summary>
        public static void FillMissingEmail(IOrganizationService service, Entity record)
        {
            if (record == null || !string.IsNullOrWhiteSpace(record.GetAttributeValue<string>(EmailAttr)))
            {
                return;
            }

            var email = EmailFor(service, null, record.GetAttributeValue<string>(NameAttr));
            if (email != null)
            {
                record[EmailAttr] = email;
            }
        }

        /// <summary>
        /// When an edit changes the adviser's name, puts the new adviser's email on the same
        /// update - or clears it where the new name matches nobody, because the old address
        /// belongs to somebody who is no longer on the case. An edit that sets the email itself,
        /// or re-sends the name already recorded, is left alone.
        /// </summary>
        public static void FollowName(
            IOrganizationService service, Entity before, Entity update, List<string> changes)
        {
            if (update == null || !update.Contains(NameAttr) || update.Contains(EmailAttr))
            {
                return;
            }

            var newName = (update.GetAttributeValue<string>(NameAttr) ?? string.Empty).Trim();
            var oldName = before == null ? string.Empty : (before.GetAttributeValue<string>(NameAttr) ?? string.Empty).Trim();
            if (string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var oldEmail = before == null ? null : before.GetAttributeValue<string>(EmailAttr);
            var newEmail = EmailFor(service, null, newName);
            update[EmailAttr] = newEmail;

            if (!string.Equals(oldEmail ?? string.Empty, newEmail ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add("Adviser email '" + (oldEmail ?? "(none)") + "' -> '" + (newEmail ?? "(none)") + "'");
            }
        }
    }
}
