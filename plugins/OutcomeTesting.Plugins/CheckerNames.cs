using System;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Which case column holds which discipline's checker (item 2, 2026-09-19).
    ///
    /// The case header used to carry ONE checker name, and a case that takes both a Tax check
    /// and an AQS check has two checkers. Whichever was allocated last overwrote the other, so
    /// the header named one of them and gave no hint that it had ever named the other - and a
    /// worklist filtered on it found the case under one checker and not the other.
    ///
    /// Two columns now, each maintained by the same allocation that assigns the review, so the
    /// header cannot disagree with the assignment it came from.
    ///
    /// Deliberately free of Dataverse types, like ResponseRules and GradingRules, so the rule
    /// can be tested without a fake organisation service. Mirrored client-side in
    /// app/src/features/cases/checkerNames.ts.
    /// </summary>
    public static class CheckerNames
    {
        /// <summary>The Tax checker's name on the case header.</summary>
        public const string TaxAttribute = "al_taxcheckername";

        /// <summary>The AQS checker's name on the case header.</summary>
        public const string AqsAttribute = "al_aqscheckername";

        /// <summary>
        /// The single column these two replace.
        ///
        /// Kept in the schema and no longer written, read or offered for editing. Dropping a
        /// column deletes what it holds, and what it holds is the last allocation made under
        /// the old rule - which is evidence about how a case was handled, not noise. It is
        /// left for the project owner to decide on once the two columns have been running
        /// long enough to be trusted.
        /// </summary>
        public const string LegacyAttribute = "al_checkername";

        /// <summary>
        /// The column a review of this discipline stamps, or null for a review type the model
        /// does not define.
        ///
        /// Null rather than a default, for the reason OutcomeRules.TryGradeFromAnswer returns
        /// false rather than a Pass: writing an unrecognised discipline's checker into one of
        /// these columns would put a name against a check nobody made.
        /// </summary>
        public static string AttributeFor(int reviewType)
        {
            switch (reviewType)
            {
                case ResponseRules.ReviewTypeTax:
                    return TaxAttribute;
                case ResponseRules.ReviewTypeAqs:
                    return AqsAttribute;
                default:
                    return null;
            }
        }

        /// <summary>
        /// The column a review of this discipline stamps, where the discipline is known.
        /// False for a review with no type at all, which is a row the allocation should not
        /// be silently writing a name for.
        /// </summary>
        public static bool TryAttributeFor(int? reviewType, out string attribute)
        {
            attribute = reviewType.HasValue ? AttributeFor(reviewType.Value) : null;
            return attribute != null;
        }
    }
}
