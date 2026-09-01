using System;
using System.Collections.Generic;
using System.Linq;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Pure decision logic for a checklist answer (AD-023, AD-053). Deliberately free of
    /// Dataverse types so it can be unit-tested without a fake organisation service; the
    /// plug-ins are thin wiring over this. Mirrors the pure-guard pattern in
    /// app/src/types/domain.ts, where the client-side mirror of a command rule is a
    /// function over primitives.
    /// </summary>
    public static class ResponseRules
    {
        public enum AnswerColumn { Text, Date, Choice, Choices }

        // al_questionversion.al_responsetype
        public const int TypeText = 120910000;
        public const int TypeMultilineText = 120910001;
        public const int TypeDate = 120910002;
        public const int TypeSingleSelect = 120910003;
        public const int TypeMultiSelect = 120910004;
        public const int TypePassFail = 120910005;
        public const int TypePassFailInsufficient = 120910006;
        public const int TypeYesNo = 120910007;
        public const int TypeYesNoNa = 120910008;
        public const int TypeYesNoInsufficient = 120910009;
        public const int TypeGrade = 120910010;

        // al_response.al_answerchoice
        public const int ChoicePass = 120910300;
        public const int ChoiceFail = 120910301;
        public const int ChoiceInsufficient = 120910302;
        public const int ChoicePassWithIssues = 120910303;
        public const int ChoicePotentialHarm = 120910304;
        public const int ChoiceYes = 120910305;
        public const int ChoiceNo = 120910306;
        public const int ChoiceNa = 120910307;

        // al_reviewinstance.al_reviewstatus
        public const int StatusAssigned = 120910210;
        public const int StatusInProgress = 120910211;
        public const int StatusSubmitted = 120910212;

        // al_reviewinstance.al_reviewtype and al_section.al_ownerrole
        public const int ReviewTypeTax = 120910200;
        public const int ReviewTypeAqs = 120910201;
        public const int OwnerRoleTaxTeam = 120910100;
        public const int OwnerRoleAqsChecker = 120910101;

        private static readonly int[] RootCauses =
        {
            120910320, 120910321, 120910322, 120910323, 120910324,
            120910325, 120910326, 120910327, 120910328,
        };

        private static readonly int[] TaxReasons =
        {
            120910340, 120910341, 120910342, 120910343, 120910344,
        };

        /// <summary>The typed column an answer of this response type must occupy (AD-023).</summary>
        public static AnswerColumn ColumnFor(int responseType)
        {
            switch (responseType)
            {
                case TypeText:
                case TypeMultilineText:
                    return AnswerColumn.Text;
                case TypeDate:
                    return AnswerColumn.Date;
                case TypeMultiSelect:
                    return AnswerColumn.Choices;
                case TypeSingleSelect:
                case TypePassFail:
                case TypePassFailInsufficient:
                case TypeYesNo:
                case TypeYesNoNa:
                case TypeYesNoInsufficient:
                case TypeGrade:
                    return AnswerColumn.Choice;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(responseType), responseType, "Unknown response type.");
            }
        }

        /// <summary>
        /// The subset of the al_response_answerchoice union set this response type may use.
        /// The constraint lives on the question version rather than the schema (AD-023), so
        /// it is enforced here and nowhere else.
        /// </summary>
        public static IReadOnlyList<int> PermittedChoices(int responseType)
        {
            switch (responseType)
            {
                case TypePassFail:
                    return new[] { ChoicePass, ChoiceFail };
                case TypePassFailInsufficient:
                    return new[] { ChoicePass, ChoiceFail, ChoiceInsufficient };
                case TypeYesNo:
                    return new[] { ChoiceYes, ChoiceNo };
                case TypeYesNoNa:
                    return new[] { ChoiceYes, ChoiceNo, ChoiceNa };
                case TypeYesNoInsufficient:
                    return new[] { ChoiceYes, ChoiceNo, ChoiceInsufficient };
                case TypeGrade:
                    // BR-005 order: Pass, Pass with issues, Insufficient evidence, Potential harm.
                    return new[] { ChoicePass, ChoicePassWithIssues, ChoiceInsufficient, ChoicePotentialHarm };
                case TypeSingleSelect:
                    // Primary root cause. Q-TAX-02 was retyped to PassFailInsufficient
                    // under AD-055, so this scale is unambiguous.
                    return RootCauses;
                case TypeMultiSelect:
                    return TaxReasons;
                default:
                    return new int[0];
            }
        }

        /// <summary>AD-020 section ownership, keyed by the review's discipline.</summary>
        public static int OwnerRoleFor(int reviewType)
        {
            switch (reviewType)
            {
                case ReviewTypeTax:
                    return OwnerRoleTaxTeam;
                case ReviewTypeAqs:
                    return OwnerRoleAqsChecker;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(reviewType), reviewType, "Unknown review type.");
            }
        }

        /// <summary>BR-006: these are the outcomes that require remediation, and so a fail reason.</summary>
        public static bool IsNonPass(int choice)
        {
            return choice == ChoiceFail || choice == ChoiceInsufficient || choice == ChoicePotentialHarm;
        }

        /// <summary>
        /// The al_ResponseCodeKey value for one answer. Stable, so a replayed create collides
        /// on the alternate key instead of producing two rival answers to the same question.
        /// Two GUIDs and a separator is 73 characters, inside the AD-026 MaxLength of 100.
        /// </summary>
        public static string BuildResponseCode(Guid reviewId, Guid questionVersionId)
        {
            return reviewId.ToString("D") + "|" + questionVersionId.ToString("D");
        }

        /// <summary>
        /// Validates the shape of an answer. Returns null when valid, otherwise a message
        /// with no failure prefix — the caller adds PRECONDITION:.
        ///
        /// An entirely empty answer is valid: clearing a field is a legitimate draft edit,
        /// and SubmitReview is what refuses to submit an unanswered mandatory question.
        ///
        /// Text alongside a choice or choices is valid and deliberate: al_answertext doubles
        /// as the evidence note held with a structured answer, which is how
        /// app/src/features/reviews/useReviewDetail.ts noteOf() already reads the record.
        /// </summary>
        public static string ValidateAnswer(
            int responseType,
            bool hasText,
            bool hasDate,
            int? choice,
            IReadOnlyCollection<int> choices)
        {
            var selected = choices ?? (IReadOnlyCollection<int>)new int[0];
            var column = ColumnFor(responseType);

            switch (column)
            {
                case AnswerColumn.Text:
                    if (hasDate || choice.HasValue || selected.Count > 0)
                    {
                        return "This question takes a written answer only.";
                    }
                    return null;

                case AnswerColumn.Date:
                    if (hasText || choice.HasValue || selected.Count > 0)
                    {
                        return "This question takes a date only.";
                    }
                    return null;

                case AnswerColumn.Choice:
                    if (hasDate || selected.Count > 0)
                    {
                        return "This question takes a single selection.";
                    }
                    if (choice.HasValue && !PermittedChoices(responseType).Contains(choice.Value))
                    {
                        return "That option is not available for this question.";
                    }
                    return null;

                case AnswerColumn.Choices:
                    if (hasDate || choice.HasValue)
                    {
                        return "This question takes one or more selections from its own list.";
                    }
                    var permitted = PermittedChoices(responseType);
                    if (selected.Any(value => !permitted.Contains(value)))
                    {
                        return "One or more of those options is not available for this question.";
                    }
                    return null;

                default:
                    return "This question cannot be answered.";
            }
        }
    }
}
