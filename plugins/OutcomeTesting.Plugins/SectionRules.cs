using System;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Section-level rules (AD-123), deliberately free of Dataverse types so they can be
    /// tested without a fake organisation service — the same choice ResponseRules makes.
    ///
    /// Two rules, and both were previously absent rather than wrong: al_section carried no
    /// effective dates at all, and owner role was matched by strict equality in five
    /// places, which is why a section could be neither retired nor shared between the two
    /// disciplines.
    /// </summary>
    public static class SectionRules
    {
        /// <summary>
        /// A section owed by the Tax review and the AQS review alike. Each answers its own
        /// copy, because responses hang off the review instance and not off the section.
        /// AD-020 already described the File Quality fail points this way.
        /// </summary>
        public const int OwnerRoleBoth = 120910105;

        /// <summary>
        /// Whether a section is in force on a given day. Identical in meaning to a question
        /// version's window and delegated to it outright, so the two can never diverge:
        /// in force from the start of effective-from, out of force from the start of
        /// effective-to.
        /// </summary>
        public static bool IsSectionEffective(DateTime? effectiveFrom, DateTime? effectiveTo, DateTime asOf)
        {
            return ResponseRules.IsVersionEffective(effectiveFrom, effectiveTo, asOf);
        }

        /// <summary>
        /// Whether a section belongs to the discipline running this review. True for the
        /// discipline's own owner role and for Both; false for everything else, including
        /// the owner roles no review is ever opened as (Adviser, T&amp;C Manager,
        /// Manager / Admin) and any review type that is not Tax or AQS.
        /// </summary>
        public static bool OwnerRoleServes(int sectionOwnerRole, int reviewType)
        {
            int disciplineRole;
            if (!OutcomeRules.TryOwnerRoleForReviewType(reviewType, out disciplineRole))
            {
                return false;
            }

            return sectionOwnerRole == disciplineRole || sectionOwnerRole == OwnerRoleBoth;
        }
    }
}
