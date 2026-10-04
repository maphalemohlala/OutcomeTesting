namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The email a case's adviser is known by (AD-228). The stored al_adviseremail, and only
    /// that: it keys al_advisermapping (the T&amp;C Manager, sign-off, regrade, the supervisor)
    /// and, through NotificationOutbox.MatchAdviser, the adviser's contact (remediation and
    /// access). It is never derived from al_advisername - that derivation is what left PROD
    /// case 256497798 with nobody when the contact's name was misspelled.
    /// </summary>
    public static class CaseAdviser
    {
        public const string EmailAttr = CasePeople.AdviserEmailAttr;
        public const string NameAttr = CasePeople.AdviserNameAttr;

        /// <summary>The adviser's email as the case stores it, trimmed, or null.</summary>
        public static string EmailFor(string storedEmail)
        {
            return CasePeople.Clean(storedEmail);
        }
    }
}
