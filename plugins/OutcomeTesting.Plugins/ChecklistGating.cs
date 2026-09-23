using System;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Which answers the rest of the form leaves available (project owner, 2026-09-22).
    ///
    /// <para>
    /// Four rules, all of the same shape: an answer recorded somewhere on the checklist takes
    /// an option off somewhere else, because the two could not both be true of one file.
    /// </para>
    /// <list type="bullet">
    /// <item>Insufficient evidence anywhere takes Pass and Pass with issues off the grade.</item>
    /// <item>A No or a Fail on any test point takes Pass off the grade AND off the file
    /// quality outcome.</item>
    /// <item>The File Quality fail points lock on a Tax check once the TAX CHECK outcome is a
    /// Pass, and on an AQS check once every AML and CRA point reads Yes.</item>
    /// <item>The file quality outcome decides Remedial action required? outright - it is the
    /// answer the question opens on AND the only one it accepts.</item>
    /// </list>
    /// <para>
    /// Deliberately free of Dataverse types, like <see cref="ResponseRules"/>,
    /// <see cref="GradingRules"/> and <see cref="CaseHeaderRules"/>, so each rule can be
    /// tested without a fake organisation service. The facts the rules run on are gathered by
    /// <see cref="ChecklistQueries"/>; deciding is done here.
    /// </para>
    /// <para>
    /// Mirrored in the OT Review Detail web template, which is where a checker answers. The
    /// server is the authority in both places: the page disables an option so a checker does
    /// not record something that is about to be taken back off them (AD-041), and that is an
    /// affordance, not the boundary (NFR-SEC-01).
    /// </para>
    /// <para>
    /// No TypeScript mirror. The Code App's review page is a read-only document (OD-007) -
    /// answering is a portal write path, and that page renders no control for a client-side
    /// copy of these rules to restrict. The portal carries the whole rendering half, in
    /// OT Review Detail and OT Answer Options. When a rule changes, change it there and here.
    /// </para>
    /// <para>
    /// This widens GradingRules.SuitabilityGradeRefusal, which applied the first rule to the
    /// Suitability core checks alone (item 10, 2026-09-19). The instruction on 2026-09-22 was
    /// "if insufficient evidence is selected at any section not just the File quality AML and
    /// CRA section", so the section test is gone rather than extended - and with it
    /// GradingRules.IsSuitabilitySection, which had no other caller. What replaces it is a
    /// QUESTION test, <see cref="IsOutcomeQuestion"/>: the thing to leave out of the sum is
    /// not a family of sections but the answers that ARE the sum.
    /// </para>
    /// </summary>
    public static class ChecklistGating
    {
        /// <summary>The AQS "Remedial action required?" answer, which raises remediation.</summary>
        public const string RemedialActionQuestionCode = "Q-FQ-03";

        /// <summary>The Tax "Remedial action required?" answer.</summary>
        public const string TaxRemedialActionQuestionCode = "Q-FQTAX-03";

        /// <summary>
        /// The Tax check outcome, on the Tax check section, which is what locks the Tax
        /// fail points (project owner, 2026-09-23).
        ///
        /// <para>
        /// Not to be confused with <see cref="FileQuality.TaxQuestionCode"/>, the Tax team's
        /// FILE QUALITY outcome on S-FQTAX. The rule was written against that one on
        /// 2026-09-22 and corrected here: "if Tax check outcome field is pass then the File
        /// Quality - Fail points section needs to be disabled, instead it is done on the File
        /// quality Outcome". Two questions, two sections, two different judgements about the
        /// same file - and the checklist warns that they have been conflated before.
        /// </para>
        /// </summary>
        public const string TaxCheckOutcomeQuestionCode = "Q-TAX-02";

        /// <summary>
        /// The AML and CRA checking points, which are what unlock the File Quality fail
        /// points on an AQS check. Identified by its stable section code rather than its
        /// display name, because the code survives a version being reissued where the name is
        /// editable through al_UpdateSection.
        /// </summary>
        public const string AmlCraSectionCode = "S-AMLCRA";

        /// <summary>
        /// Whether a question records an OUTCOME or a DECISION rather than a test point.
        ///
        /// <para>
        /// These five are what the rest of the form adds up TO, so they must not be counted as
        /// part of the sum. Two of them would otherwise make the rules contradict each other
        /// outright (project owner, 2026-09-22, settling exactly this): "Remedial action
        /// required?" answers No to mean nothing needs fixing, and a Pass on the file quality
        /// outcome is what sets it to No - so a scan that counted every No would have the
        /// checker's own Pass grey out Pass a moment after they chose it. The grade and the
        /// two file quality outcomes are excluded because they are the values being gated:
        /// a rule that read its own answer back would only ever confirm itself.
        /// </para>
        /// <para>
        /// Everything else counts, including the ones that are easy to mistake for outcomes.
        /// Q-TAX-02, the Tax check outcome, is a finding about the file. So is Q-E2-LENS -
        /// "would a reasonable third party conclude the client was not exposed to foreseeable
        /// harm?" - where a No is as much a finding as any Fail on the grid above it.
        /// </para>
        /// <para>
        /// Matched on the question's business code, never its text or its response type: a
        /// code survives the question being retired and succeeded (BR-013, AD-004), and all
        /// five are protected from retirement by <see cref="ChecklistGuards"/> (AD-122), so
        /// there is no version in which one of them quietly stops being found.
        /// </para>
        /// </summary>
        public static bool IsOutcomeQuestion(string questionCode)
        {
            if (string.IsNullOrWhiteSpace(questionCode))
            {
                // A question whose code could not be read is treated as a test point. That is
                // the safe direction: it may cost a checker the Pass option they could have
                // had, and they can say so - where the other way round records a grade the
                // form contradicts and nobody notices.
                return false;
            }

            var code = questionCode.Trim();

            return string.Equals(code, FileQuality.QuestionCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, FileQuality.TaxQuestionCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, RemedialActionQuestionCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, TaxRemedialActionQuestionCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, GradingRules.GradeQuestionCode, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether an answer on a test point is a finding of the kind that takes Pass away:
        /// a Fail, or a No.
        ///
        /// Insufficient evidence is deliberately NOT here. It takes away more than a No does -
        /// Pass with issues as well as Pass - so it is asked separately, by
        /// <see cref="IsInsufficient"/>, and the two are combined by the rules below rather
        /// than flattened into one "something is wrong" test that could only express the
        /// weaker of them.
        /// </summary>
        public static bool IsNoOrFail(int choice)
        {
            return choice == ResponseRules.ChoiceFail || choice == ResponseRules.ChoiceNo;
        }

        /// <summary>Whether an answer is Insufficient evidence.</summary>
        public static bool IsInsufficient(int choice)
        {
            return choice == ResponseRules.ChoiceInsufficient;
        }

        /// <summary>
        /// Whether an answer on the AML and CRA section is a clean one - the only thing that
        /// counts towards locking the File Quality fail points (project owner, 2026-09-23:
        /// "if all the responses are yes, then lock it. Otherwise open it").
        ///
        /// <para>
        /// Written as "is it a Yes" rather than as a list of the values that are not. The
        /// section has already moved once - seeded Yes / No / N/A and retyped to Yes / No /
        /// Insufficient evidence - and AD-123 can retype it again. A rule naming the values
        /// that unlock would have to be found and extended every time; this one cannot fall
        /// behind the scale, and an option nobody anticipated leaves the fail points OPEN,
        /// which is the safe direction: an open block costs the checker nothing but a glance,
        /// where a wrongly locked one hides the place to record what they found.
        /// </para>
        /// </summary>
        public static bool IsAmlCraClean(int? choice)
        {
            return choice.HasValue && choice.Value == ResponseRules.ChoiceYes;
        }

        /// <summary>
        /// Why this advice quality grade cannot be recorded against the rest of the form, or
        /// null when it can. Returns a message with no failure prefix - the caller adds
        /// PRECONDITION:.
        ///
        /// <para>
        /// An absent grade passes. Clearing the grade is what this rule does to one already
        /// saved that the form has since contradicted, so refusing the cleared state would
        /// leave the review with no way back.
        /// </para>
        /// <para>
        /// Insufficient evidence is tested first because it refuses strictly more: it leaves
        /// only Insufficient evidence and Potential harm, where a No or a Fail leaves Pass
        /// with issues available too. Reporting the weaker refusal against a form that
        /// breaches both would send the checker to change the grade to something still
        /// refused.
        /// </para>
        /// </summary>
        public static string GradeRefusal(int? gradeAnswer, bool insufficientAnywhere, bool noOrFailAnywhere)
        {
            if (!gradeAnswer.HasValue)
            {
                return null;
            }

            if (insufficientAnywhere && !GradingRules.GradeAllowedWithInsufficientEvidence(gradeAnswer.Value))
            {
                return "A check on this review has been answered Insufficient evidence, so the "
                    + "advice quality grade can only be Insufficient evidence or Potential harm.";
            }

            if (noOrFailAnywhere && gradeAnswer.Value == ResponseRules.ChoicePass)
            {
                return "A check on this review has been answered No or Fail, so the advice "
                    + "quality grade cannot be Pass.";
            }

            return null;
        }

        /// <summary>
        /// Whether a grade already recorded must be let go because the form now contradicts
        /// it.
        ///
        /// The batch's instruction throughout is to clear the now-invalid outcome and prompt,
        /// rather than to refuse the tick that invalidated it: the checker is recording what
        /// they found on the file, and the finding is not the thing to argue with.
        /// </summary>
        public static bool GradeCleared(int? gradeAnswer, bool insufficientAnywhere, bool noOrFailAnywhere)
        {
            return GradeRefusal(gradeAnswer, insufficientAnywhere, noOrFailAnywhere) != null;
        }

        /// <summary>
        /// Why this file quality outcome cannot be recorded, or null when it can (project
        /// owner, 2026-09-22: the Pass that is greyed out by a No or a Fail is the grade's
        /// AND the file quality outcome's).
        ///
        /// Only Pass is ever refused. The outcome's scale is Pass / Fail, so there is nothing
        /// else to take away - and Insufficient evidence has no bearing here for the same
        /// reason: it is not a value this question can carry.
        /// </summary>
        public static string FileQualityRefusal(int? outcomeAnswer, bool noOrFailAnywhere)
        {
            if (!outcomeAnswer.HasValue || !noOrFailAnywhere)
            {
                return null;
            }

            if (outcomeAnswer.Value != ResponseRules.ChoicePass)
            {
                return null;
            }

            return "A check on this review has been answered No or Fail, so the file quality "
                + "outcome cannot be Pass.";
        }

        /// <summary>
        /// Whether the File Quality fail points may be ticked at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two rules, one per discipline, because the two checks reach the fail points from
        /// opposite directions.
        /// </para>
        /// <para>
        /// <b>Tax.</b> Locked once the TAX CHECK outcome - Q-TAX-02, on the Tax check section
        /// - is a Pass (project owner, 2026-09-23). A file the Tax checker passed has no fail
        /// to give reasons for. Only a clean Pass locks it: the Tax scale carries Pass with
        /// issues as well (AD-055), and a file passed with issues is exactly one whose issues
        /// want recording. Unanswered is NOT locked either - the reasons are often what the
        /// checker works out first, and locking a form nobody has answered yet would read as
        /// the page being broken.
        /// </para>
        /// <para>
        /// This corrects the rule of 2026-09-22, which read the same Pass off the tax FILE
        /// QUALITY outcome (Q-FQTAX-01, on S-FQTAX). That is a different question on a
        /// different section, answered later and by a different judgement.
        /// </para>
        /// <para>
        /// <b>AQS.</b> Locked once every AML and CRA point reads Yes (project owner,
        /// 2026-09-23: "if all the responses are yes, then lock it. Otherwise open it"). A
        /// section with nothing wrong in it has no failing for the fail points to explain.
        /// </para>
        /// <para>
        /// This too is a reversal. Until 2026-09-23 the AQS block was locked UNTIL a finding
        /// arrived, so a checker who had not yet reached that section met it shut - the very
        /// thing the Tax rule above refuses to do on an unanswered outcome. "Otherwise open
        /// it" settles the two the same way round: open is the resting state, and only a
        /// section positively answered clean closes it. Whether every point is answered is
        /// <see cref="ChecklistQueries.ReadGatingFacts"/>'s question, not this one's; a
        /// half-answered section is not all Yes and arrives here as false.
        /// </para>
        /// <para>
        /// A Tax check has no AML and CRA section at all - it is AQS-owned (al_ownerrole
        /// 120910101) and AD-020 filters it out of the Tax form - which is exactly why the
        /// AQS rule cannot simply be applied to both: a section that review never had would
        /// read as never clean, and the Tax fail points would never lock at all.
        /// </para>
        /// </remarks>
        public static bool FailPointsLocked(bool isTaxReview, int? taxCheckOutcome, bool amlCraAllYes)
        {
            if (isTaxReview)
            {
                return taxCheckOutcome.HasValue
                    && taxCheckOutcome.Value == ResponseRules.ChoicePass;
            }

            return amlCraAllYes;
        }

        /// <summary>
        /// What "Remedial action required?" opens on, given the file quality outcome, or null
        /// where the outcome does not say (project owner, 2026-09-22, given twice: "if the
        /// file quality outcome is pass, the remedial action required should default to no,
        /// and yes if it is a fail").
        ///
        /// <para>
        /// No longer only a default. It was one until 2026-09-23 - the page filled it in and
        /// the checker could move it - and the instruction that day was that the other answer
        /// "needs to be disabled". So this value is now what the question opens on AND the
        /// only value it accepts; <see cref="RemedialActionRefusal"/> is the half that makes
        /// it stick, and the two must be read together.
        /// </para>
        /// <para>
        /// Null on an unanswered or unrecognised outcome, which the callers read as "leave it
        /// alone". Defaulting from an outcome nobody has chosen would put an answer on the
        /// form that no one gave.
        /// </para>
        /// </summary>
        public static int? RemedialActionDefault(int? fileQualityOutcome)
        {
            if (!fileQualityOutcome.HasValue)
            {
                return null;
            }

            switch (fileQualityOutcome.Value)
            {
                case ResponseRules.ChoicePass:
                    return ResponseRules.ChoiceNo;
                case ResponseRules.ChoiceFail:
                    return ResponseRules.ChoiceYes;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Why this "Remedial action required?" answer cannot be recorded against the file
        /// quality outcome, or null when it can. Returns a message with no failure prefix -
        /// the caller adds PRECONDITION:.
        ///
        /// <para>
        /// The other half of <see cref="RemedialActionDefault"/>, and the change of
        /// 2026-09-23: what the outcome implies had been a default the checker could overrule,
        /// and is now the only answer the question takes. A Pass leaves No, a Fail leaves Yes.
        /// </para>
        /// <para>
        /// <b>What this closes.</b> A passed file can no longer carry a remedial action. That
        /// combination was reachable and meant something - AD-184 fixed a Tax check that
        /// passed and still flagged one, and the deferral it repaired is live - so this is a
        /// deliberate narrowing, given twice and confirmed. The Tax side of AD-184 survives
        /// it: that path turns on Q-TAX-02, the tax CHECK outcome, which this rule does not
        /// read. It is the FILE QUALITY Pass that now forecloses a Yes.
        /// </para>
        /// <para>
        /// Silent on an unanswered outcome, and on any value off the Pass / Fail scale. A
        /// checker working bottom-up answers this question before the outcome above it on
        /// plenty of files, and refusing them there would leave nothing recordable at all -
        /// where the answer they give is reconciled the moment the outcome lands, by
        /// ResponseProgressPlugin.
        /// </para>
        /// </summary>
        public static string RemedialActionRefusal(int? fileQualityOutcome, int answer)
        {
            var allowed = RemedialActionDefault(fileQualityOutcome);
            if (!allowed.HasValue || allowed.Value == answer)
            {
                return null;
            }

            return fileQualityOutcome.Value == ResponseRules.ChoicePass
                ? "The file quality outcome on this review is a Pass, so a remedial action "
                    + "cannot be required. Record the outcome as Fail if the file needs one."
                : "The file quality outcome on this review is a Fail, so a remedial action is "
                    + "required. Record the outcome as Pass if the file needs none.";
        }
    }
}
