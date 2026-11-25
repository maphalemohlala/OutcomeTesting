using System;
using System.Collections.Generic;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The three remediation categories on the Tax check, which replace the single Tax Remedial
    /// box (client decision, 2026-11-06; cut from four to three at UAT, 2026-11-25: Taxation
    /// Narrative reworded to Wording, Omissions retired). Each is a Yes/No with a comments box;
    /// a Yes adds a row to the remedial actions card, which raises its action(s) (owner,
    /// 2026-11-23). Q-TAX-09 / Q-TAX-10 (Omissions) stay answered on reviews submitted before.
    ///
    /// Pure, so the rule can be tested without a fake organisation service. Mirrored by
    /// otRemedialItems in OT Review Detail; when one changes, change both.
    /// </summary>
    public static class TaxCategories
    {
        /// <summary>One category: the name the adviser reads and its two questions.</summary>
        public sealed class Category
        {
            public Category(string name, string answerCode, string commentCode)
            {
                Name = name;
                AnswerCode = answerCode;
                CommentCode = commentCode;
            }

            public string Name { get; private set; }

            public string AnswerCode { get; private set; }

            public string CommentCode { get; private set; }
        }

        public static readonly IList<Category> All = new[]
        {
            new Category("Calculations", "Q-TAX-05", "Q-TAX-06"),
            new Category("Wording", "Q-TAX-07", "Q-TAX-08"),
            new Category("Other", "Q-TAX-11", "Q-TAX-12"),
        };

        /// <summary>
        /// Each category answered Yes, in form order. A review predating the categories
        /// answers none of them, so it returns nothing.
        /// </summary>
        public static List<string> Raised(Func<string, int?> choiceFor)
        {
            var raised = new List<string>();
            foreach (var category in All)
            {
                if (choiceFor(category.AnswerCode) == ResponseRules.ChoiceYes)
                {
                    raised.Add(category.Name);
                }
            }

            return raised;
        }

        /// <summary>
        /// Each Yes category is a row on the remedial actions card, its remedial action (and
        /// any further ones, "name ##2") written there like any other row (owner, 2026-11-23,
        /// replacing "the comments are its words"). Drops the words of categories no longer
        /// Yes from <paramref name="map"/>, then returns why the review cannot be submitted, or
        /// null when every Yes category has its remedial action.
        /// </summary>
        public static string Settle(IList<string> raised, IDictionary<string, string> map)
        {
            foreach (var category in All)
            {
                if (raised.Contains(category.Name))
                {
                    continue;
                }

                var stem = category.Name + RemedialActions.ExtraMark;
                foreach (var key in new List<string>(map.Keys))
                {
                    if (key == category.Name || key.StartsWith(stem, StringComparison.Ordinal))
                    {
                        map.Remove(key);
                    }
                }
            }

            foreach (var name in raised)
            {
                string text;
                if (!map.TryGetValue(name, out text) || string.IsNullOrWhiteSpace(text))
                {
                    return "Write the remedial action for '" + name + "' before submitting.";
                }
            }

            return null;
        }

        /// <summary>The categories among a review's parked words, in form order.</summary>
        public static List<string> Parked(IDictionary<string, string> map)
        {
            var names = new List<string>();
            foreach (var category in All)
            {
                if (map != null && map.ContainsKey(category.Name))
                {
                    names.Add(category.Name);
                }
            }

            return names;
        }
    }
}
