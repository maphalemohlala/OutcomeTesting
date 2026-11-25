import type { RoleMappingRow } from './useSecurityConfig';
import { sameRoleCode } from './roleDetail';

export type CellState = 'held' | 'withdrawn' | 'none';

export interface MatrixColumn {
  code: string;
  /** What the header shows: the portal prefix is the same on every role, so it is dropped. */
  label: string;
  retired: boolean;
  /** Administrators can do everything, so its column is tinted as the one to watch. */
  privileged: boolean;
}

export interface MatrixRow {
  email: string;
  cells: { column: MatrixColumn; state: CellState; mapping: RoleMappingRow | null }[];
  active: number;
  withdrawn: number;
}

export function roleDisplayName(code: string): string {
  return code.replace(/^AL Portal - /i, '');
}

/**
 * The live roles, then any role a stored assignment names that is no longer live, so no
 * assignment is left without a column. Administrators goes last, as the widest grant.
 */
export function matrixColumns(
  roles: readonly { code: string; active: boolean }[],
  mappings: readonly RoleMappingRow[],
): MatrixColumn[] {
  const codes: string[] = roles.filter((r) => r.active).map((r) => r.code);
  for (const m of mappings) if (!codes.some((c) => sameRoleCode(c, m.role))) codes.push(m.role);
  const isAdmin = (code: string) => sameRoleCode(code, 'Administrators');
  return codes
    .sort((a, b) => Number(isAdmin(a)) - Number(isAdmin(b)))
    .map((code) => ({
      code,
      label: roleDisplayName(code),
      retired: !roles.some((r) => r.active && sameRoleCode(r.code, code)),
      privileged: isAdmin(code),
    }));
}

/** One row per work email (case ignored). An active assignment wins over a withdrawn one. */
export function matrixRows(mappings: readonly RoleMappingRow[], columns: readonly MatrixColumn[]): MatrixRow[] {
  const byEmail = new Map<string, RoleMappingRow[]>();
  for (const m of mappings) {
    const key = m.email.trim().toLowerCase();
    byEmail.set(key, [...(byEmail.get(key) ?? []), m]);
  }
  return [...byEmail.values()].map((own) => {
    const cells = columns.map((column) => {
      const forRole = own.filter((m) => sameRoleCode(m.role, column.code));
      const mapping = forRole.find((m) => m.active) ?? forRole[0] ?? null;
      const state: CellState = !mapping ? 'none' : mapping.active ? 'held' : 'withdrawn';
      return { column, state, mapping };
    });
    return {
      email: own[0].email,
      cells,
      active: cells.filter((c) => c.state === 'held').length,
      withdrawn: cells.filter((c) => c.state === 'withdrawn').length,
    };
  });
}

export function holdingSummary(row: Pick<MatrixRow, 'active' | 'withdrawn'>): string {
  const parts = [`${row.active} active`];
  if (row.withdrawn) parts.push(`${row.withdrawn} withdrawn`);
  return parts.join(', ');
}
