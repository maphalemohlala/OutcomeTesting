namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The para-planner's copy when a remediation is raised (project owner, 2026-10-05: "When a
    /// remediation is raised include paraplanner in email with pdf of checks and remedial
    /// actions", as a separate email).
    ///
    /// <para>
    /// <b>Not editable.</b> "It should just be a copy of the checks and remedial points": the
    /// letter is its attachments, so the wording is fixed here and kept out of
    /// <see cref="NotificationTemplates"/>, where the template editor would offer it. It is
    /// queued with no template code, so no stored row can reword or redirect it. No portal
    /// button: the para-planner has no access to the case.
    /// </para>
    /// </summary>
    public static class ParaplannerRemediationLetter
    {
        /// <summary>The outbox occurrence that makes it a row of its own beside the adviser's.</summary>
        public const string Occurrence = "PARAPLANNER";

        public static string Subject(string reference)
        {
            return "Checks and remedial points: " + NotificationTemplates.ReferenceOr(reference);
        }

        public static string Body(string reference)
        {
            return string.IsNullOrWhiteSpace(reference)
                ? "The checks and remedial points for this case are attached."
                : "The checks and remedial points for case " + reference.Trim() + " are attached.";
        }
    }
}
