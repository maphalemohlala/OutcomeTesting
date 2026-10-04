using System.Text.RegularExpressions;

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
    }
}
