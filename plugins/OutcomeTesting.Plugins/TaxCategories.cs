using System;
using System.Collections.Generic;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The four remediation categories on the Tax check, which replace the single Tax Remedial
    /// box (client decision, 2026-11-06). Each is a Yes/No with a comments box, and a Yes raises
    /// its own action carrying those comments, so the checker writes them once.
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
            new Category("Taxation Narrative", "Q-TAX-07", "Q-TAX-08"),
            new Category("Omissions", "Q-TAX-09", "Q-TAX-10"),
            new Category("Other", "Q-TAX-11", "Q-TAX-12"),
        };

        /// <summary>
        /// Each category answered Yes, with its comments. A review predating the categories
        /// answers none of them, so it returns nothing.
        /// </summary>
        public static Dictionary<string, string> Raised(
            Func<string, int?> choiceFor,
            Func<string, string> commentFor)
        {
            var raised = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var category in All)
            {
                if (choiceFor(category.AnswerCode) == ResponseRules.ChoiceYes)
                {
                    raised[category.Name] = (commentFor(category.CommentCode) ?? string.Empty).Trim();
                }
            }

            return raised;
        }

        /// <summary>Why the review cannot be submitted, or null when every Yes has its comments.</summary>
        public static string Refusal(IDictionary<string, string> raised)
        {
            foreach (var pair in raised)
            {
                if (pair.Value.Length == 0)
                {
                    return "Write the remediation comments for '" + pair.Key + "' before submitting.";
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
