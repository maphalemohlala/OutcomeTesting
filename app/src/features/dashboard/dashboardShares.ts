/**
 * The share of a whole that one count represents, as a percentage for a bar width.
 *
 * Bars on the dashboard sit on a shared scale within their panel (all cases, or all open
 * cases), so a count reads as a proportion without arithmetic. A zero or missing whole
 * yields 0 rather than NaN, because an empty environment must still render.
 */
export function proportion(count: number, total: number): number {
  if (!(total > 0) || !(count > 0)) return 0;
  return Math.min(100, Math.round((count / total) * 1000) / 10);
}
