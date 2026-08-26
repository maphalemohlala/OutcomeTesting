import { NAV_GROUPS } from './navigation';

const STORAGE_KEY = 'ot.nav.collapsed';

/** Only the groups a user has deliberately closed are stored, so new groups appear open. */
export function readCollapsedGroups(): string[] {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    const known = new Set(NAV_GROUPS.map((group) => group.heading));
    return parsed.filter((value): value is string => typeof value === 'string' && known.has(value));
  } catch {
    return [];
  }
}

export function writeCollapsedGroups(headings: string[]): void {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(headings));
  } catch {
    // Storage can be unavailable; navigation must still work.
  }
}
