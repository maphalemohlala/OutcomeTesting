using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The contact table read as the application user registry (AD-010, AD-041).
    ///
    /// The registry used to be al_user, a second list of people that nothing joined to the
    /// first. Authorisation never depended on it — PermissionHelpers reads
    /// al_userrolemapping keyed on al_useremail and treats a missing registry row as
    /// permitted — so it was a directory, not a gate. Contact is what Power Pages resolves
    /// permissions through (AD-047) and what AssignCasePlugin already demands alongside the
    /// systemuser (OD-003), so keying the commands to it leaves one set of people.
    ///
    /// Free of any organisation service so the naming rules stay unit-testable, matching
    /// CaseLifecycle and ResponseRules.
    /// </summary>
    public static class ContactRegistry
    {
        public const string Entity = "contact";
        public const string EmailAttr = "emailaddress1";
        public const string FirstNameAttr = "firstname";
        public const string LastNameAttr = "lastname";
        public const string FullNameAttr = "fullname";
        public const string StateCodeAttr = "statecode";
        public const string StatusCodeAttr = "statuscode";

        public const int StateActive = 0;
        public const int StateInactive = 1;
        public const int StatusActive = 1;
        public const int StatusInactive = 2;

        /// <summary>
        /// The surname half of a display name.
        ///
        /// contact.fullname is calculated, so it cannot be written; firstname and lastname
        /// are what a caller actually sets, and lastname is the required one. The last
        /// whitespace-separated token is the surname and everything before it the forename,
        /// which is the convention fullname is composed back from. A single word is a
        /// surname, so a mononym still produces a writable, non-empty contact.
        /// </summary>
        public static string LastNameOf(string fullName)
        {
            var parts = Split(fullName);
            return parts.Length == 0 ? string.Empty : parts[parts.Length - 1];
        }

        /// <summary>The forename half, empty when the name is a single word.</summary>
        public static string FirstNameOf(string fullName)
        {
            var parts = Split(fullName);
            return parts.Length < 2 ? string.Empty : string.Join(" ", parts, 0, parts.Length - 1);
        }

        /// <summary>How the platform will compose fullname from the parts written.</summary>
        public static string ComposeName(string fullName)
        {
            var first = FirstNameOf(fullName);
            var last = LastNameOf(fullName);
            return string.IsNullOrEmpty(first) ? last : first + " " + last;
        }

        /// <summary>Writes a display name onto a contact as the parts fullname is built from.</summary>
        public static void SetName(Entity contact, string fullName)
        {
            contact[FirstNameAttr] = FirstNameOf(fullName);
            contact[LastNameAttr] = LastNameOf(fullName);
        }

        /// <summary>
        /// Whether a contact row counts as an active application user. A row with no
        /// statecode is treated as active, matching the permissive reading elsewhere: only
        /// an explicit deactivation withdraws access (OD-010).
        /// </summary>
        public static bool IsActive(Entity contact)
        {
            var state = contact.GetAttributeValue<OptionSetValue>(StateCodeAttr);
            return state == null || state.Value == StateActive;
        }

        /// <summary>The display name a read returned, falling back to the parts and then the email.</summary>
        public static string NameOf(Entity contact)
        {
            var full = contact.GetAttributeValue<string>(FullNameAttr);
            if (!string.IsNullOrWhiteSpace(full)) return full.Trim();

            var first = contact.GetAttributeValue<string>(FirstNameAttr);
            var last = contact.GetAttributeValue<string>(LastNameAttr);
            var composed = string.Join(" ", new[] { first, last }).Trim();
            if (!string.IsNullOrWhiteSpace(composed)) return composed;

            return contact.GetAttributeValue<string>(EmailAttr) ?? string.Empty;
        }

        private static string[] Split(string fullName)
        {
            return (fullName ?? string.Empty).Trim()
                .Split(new[] { ' ', '\t', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
