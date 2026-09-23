import type { Discipline } from './caseCheckers';

/**
 * Who may allocate which check, mirrored from AllocationScope in the plug-in assembly
 * (AD-218; closes OD-029(c)). al_AssignCase is the authority and refuses what this would
 * not offer; the mirror only stops the edit modal proposing a refusal.
 */
export const TAX_TEAM_MANAGER = 'AL Portal - Tax Team Manager';
export const AQS_TEAM_MANAGER = 'AL Portal - AQS Team Manager';
const BOTH = ['AL Portal - Outcome Testing Manager', 'Administrators'];

function normalise(role: string): string {
  return role.trim().toLowerCase();
}

export function allocatableDisciplines(roles: readonly string[]): Discipline[] {
  const held = new Set(roles.map(normalise));
  if (BOTH.some((role) => held.has(normalise(role)))) return ['Tax', 'AQS'];

  const out: Discipline[] = [];
  if (held.has(normalise(TAX_TEAM_MANAGER))) out.push('Tax');
  if (held.has(normalise(AQS_TEAM_MANAGER))) out.push('AQS');
  return out;
}

/** Awaiting Remediation, Remediation In Progress, Awaiting Sign-off, Awaiting Recheck. */
const RELEASE_OPEN = new Set([120910587, 120910588, 120910589, 120910590]);

/**
 * A case in remediation that nobody could be released to (AD-218): the adviser name matched
 * no single contact, so no adviser can see it. Closed is excluded because a Pass case closes
 * without ever being released.
 */
export function adviserUnmatched(statusValue: number, adviserContactId: string | null): boolean {
  return RELEASE_OPEN.has(statusValue) && !adviserContactId;
}
