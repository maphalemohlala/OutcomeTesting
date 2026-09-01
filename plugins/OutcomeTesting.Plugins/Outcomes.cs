using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Reads the effective grade off an al_outcome row.
    ///
    /// BR-007 keeps the initial and final outcomes side by side so both survive a regrade,
    /// which means every reader has to answer the same question — final where one has been
    /// recorded, otherwise initial. Three call sites were spelling that out inline
    /// (SetFailAccountability's fail guard, and both the grade column and the accountability
    /// gate in GenerateExport), and a reader that got the precedence backwards would report
    /// a superseded grade to Trail Light with nothing to catch it.
    ///
    /// Kept apart from <see cref="OutcomeRules"/> on purpose: that class is deliberately
    /// free of Dataverse types, and these take an Entity.
    /// </summary>
    public static class Outcomes
    {
        public const string InitialOutcomeAttr = "al_initialoutcome";
        public const string FinalOutcomeAttr = "al_finaloutcome";

        /// <summary>
        /// The outcome value in force: final where recorded, otherwise initial (BR-007).
        /// Null where the row carries neither, which is a case with no outcome recorded
        /// rather than a pass — callers must not treat it as one.
        /// </summary>
        public static int? EffectiveOutcome(Entity outcome)
        {
            if (outcome == null)
            {
                return null;
            }

            var effective = outcome.GetAttributeValue<OptionSetValue>(FinalOutcomeAttr)
                ?? outcome.GetAttributeValue<OptionSetValue>(InitialOutcomeAttr);

            return effective != null ? effective.Value : (int?)null;
        }

        /// <summary>
        /// The same precedence over the formatted labels, for AD-039 column 15, which
        /// reports the grade as text. Resolved from the same row so the label and the value
        /// can never disagree about which outcome is in force.
        /// </summary>
        public static string EffectiveOutcomeLabel(Entity outcome)
        {
            if (outcome == null)
            {
                return null;
            }

            return Formatted(outcome, FinalOutcomeAttr) ?? Formatted(outcome, InitialOutcomeAttr);
        }

        private static string Formatted(Entity entity, string attribute)
        {
            return entity.FormattedValues.ContainsKey(attribute) ? entity.FormattedValues[attribute] : null;
        }
    }
}
