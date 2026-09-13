import { inForceOn } from '../../lib/effectiveWindow';

/**
 * Whether a row of checklist content is in force, and what to call the team that owns it
 * (AD-123). Mirrors `SectionRules` and `ResponseRules` in the plug-in assembly, which are
 * authoritative; this copy decides what the Question library shows, never what is allowed.
 *
 * Its own module rather than part of `useQuestionLibrary` so it can be tested: that hook
 * imports the generated services, which do not resolve under the test runner.
 */

/** The three teams a section may be owned by. Mirrors SectionRules.OwnerRoleBoth and friends. */
export { OWNER_ROLE_LABEL } from '../../lib/ownerRole';

/**
 * In force from the start of the effective-from day until the start of the effective-to day
 * — the same window a question version uses, applied to sections as well (AD-091, AD-123).
 * A retired row stays Active and keeps its answers; it is simply no longer asked.
 *
 * Delegated rather than restated, exactly as `SectionRules.IsSectionEffective` delegates to
 * `ResponseRules.IsVersionEffective`, so the section window and the version window can
 * never diverge.
 */
export function inForce(
  effectiveFrom: string | Date | null | undefined,
  effectiveTo: string | Date | null | undefined,
  asOf: Date,
): boolean {
  return inForceOn(effectiveFrom, effectiveTo, asOf);
}
