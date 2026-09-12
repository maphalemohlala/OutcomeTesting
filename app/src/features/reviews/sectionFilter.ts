/**
 * The sections a review must render (AD-123). Mirrors the section half of the portal's
 * review FetchXML and `SectionRules` in `plugins/OutcomeTesting.Plugins`, which is the
 * authority — this is the client half of the same rule, and the server is what enforces it.
 *
 * Held in its own module rather than inside `useReviewDetail` so it can be tested: that hook
 * imports the generated Power Apps services, which do not resolve under the test runner.
 * `versionEffective` and `reviewAnswer` are separated for the same reason.
 */

/**
 * A section owed by the Tax review and the AQS review alike. Each answers its own copy,
 * because responses hang off the review instance and not off the section. Mirrors
 * `SectionRules.OwnerRoleBoth`.
 */
export const OWNER_ROLE_BOTH = 120910105;

/**
 * Owned by this discipline or by Both, on the checklist version issued to the review, and in
 * force on the reference day — the review's submission day once submitted, otherwise today
 * (AD-091). A submitted review therefore keeps rendering the sections it was answered
 * against, which is the same as-of rule the question versions already use, applied one level
 * up.
 *
 * `asOf` is a date-only string, `YYYY-MM-DD`, because both columns are date-only and
 * comparing them against a timestamp is what AD-091 was raised to stop.
 */
export function buildSectionFilter(
  ownerRole: number,
  checklistVersionId: string | null,
  asOf: string,
): string {
  return [
    // Parenthesised deliberately. Without the grouping this reads as
    //   (own role) or (Both and version and dates)
    // which returns every section this discipline owns, retired ones included.
    `(al_ownerrole eq ${ownerRole} or al_ownerrole eq ${OWNER_ROLE_BOTH})`,
    `(al_effectivefrom eq null or al_effectivefrom le ${asOf})`,
    `(al_effectiveto eq null or al_effectiveto gt ${asOf})`,
    checklistVersionId ? `_al_checklistversionid_value eq ${checklistVersionId}` : null,
  ]
    .filter(Boolean)
    .join(' and ');
}

/** The reference day for a review, as the date-only string the filter needs. */
export function referenceDay(submittedOn: string | null | undefined): string {
  const day = submittedOn ? new Date(submittedOn) : new Date();
  return Number.isNaN(day.getTime())
    ? new Date().toISOString().slice(0, 10)
    : day.toISOString().slice(0, 10);
}
