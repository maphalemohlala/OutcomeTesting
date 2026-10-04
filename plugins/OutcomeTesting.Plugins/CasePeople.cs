using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The people on a case are identified by EMAIL, never by name (AD-228, owner 2026-10-02:
    /// "two people can have the same name"). al_advisername and al_paraplanner are labels,
    /// written beside the email and never used to find anyone.
    /// </summary>
    public static class CasePeople
    {
        public const string AdviserNameAttr = "al_advisername";
        public const string AdviserEmailAttr = "al_adviseremail";
        public const string ParaplannerNameAttr = "al_paraplanner";
        public const string ParaplannerEmailAttr = "al_paraplanneremail";

        /// <summary>
        /// Every column that names a person on a case, each label beside its email - the
        /// columns <see cref="EnsureEmails"/> and <see cref="AdviserEmailChanged"/> read off
        /// the case before an edit. An edit path's before-read takes them from here, so a
        /// person added to the rule cannot be missing from the row it is checked against.
        /// </summary>
        public static readonly ReadOnlyCollection<string> Columns = new ReadOnlyCollection<string>(new[]
        {
            AdviserNameAttr, AdviserEmailAttr, ParaplannerNameAttr, ParaplannerEmailAttr,
        });

        // One address: something@something.something, no spaces, one @. Deliberately loose -
        // the import and the edits refuse a NAME typed into an email column, not every
        // address RFC 5322 would refuse.
        private static readonly Regex EmailShape =
            new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant);

        /// <summary>The value trimmed, or null when it is blank.</summary>
        public static string Clean(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        /// <summary>True for one email address, spaces around it ignored.</summary>
        public static bool IsEmail(string value)
        {
            var trimmed = Clean(value);
            return trimmed != null && EmailShape.IsMatch(trimmed);
        }

        /// <summary>
        /// Refuses an edit that names a person without their email, or gives an email that is
        /// not one (AD-228). Only the people the update touches are checked, so an unrelated
        /// edit to a case imported before emails were required still saves.
        /// </summary>
        public static void EnsureEmails(Entity before, Entity update)
        {
            EnsurePair(before, update, AdviserNameAttr, AdviserEmailAttr, "adviser");
            EnsurePair(before, update, ParaplannerNameAttr, ParaplannerEmailAttr, "paraplanner");
        }

        /// <summary>True when this update moves the adviser email (case and spaces ignored).</summary>
        public static bool AdviserEmailChanged(Entity before, Entity update)
        {
            if (update == null || !update.Contains(AdviserEmailAttr))
            {
                return false;
            }

            var was = Clean(before == null ? null : before.GetAttributeValue<string>(AdviserEmailAttr));
            var now = Clean(update.GetAttributeValue<string>(AdviserEmailAttr));
            return !string.Equals(was ?? string.Empty, now ?? string.Empty, System.StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsurePair(Entity before, Entity update, string nameAttr, string emailAttr, string who)
        {
            if (update == null || (!update.Contains(nameAttr) && !update.Contains(emailAttr)))
            {
                return;
            }

            if (update.Contains(nameAttr) && !update.Contains(emailAttr))
            {
                // A cleared name names nobody, so it is not a name without its email: the
                // email already on the case stays the identity, and only the label goes.
                if (Clean(update.GetAttributeValue<string>(nameAttr)) == null)
                {
                    return;
                }

                throw new InvalidPluginExecutionException(CommandHelpers.ValidationPrefix
                    + "Give the " + who + "'s email as well as their name. People on a case are "
                    + "identified by email, because two people can share a name.");
            }

            var name = Clean(update.Contains(nameAttr)
                ? update.GetAttributeValue<string>(nameAttr)
                : before == null ? null : before.GetAttributeValue<string>(nameAttr));
            var email = Clean(update.GetAttributeValue<string>(emailAttr));

            if (email == null)
            {
                if (name != null)
                {
                    throw new InvalidPluginExecutionException(CommandHelpers.ValidationPrefix
                        + "The " + who + "'s email cannot be cleared while the case still names them.");
                }

                return;
            }

            if (!IsEmail(email))
            {
                throw new InvalidPluginExecutionException(CommandHelpers.ValidationPrefix
                    + "The " + who + "'s email \"" + email + "\" is not an email address.");
            }
        }
    }
}
