import type { ReportFilters } from './reportFilters';

function shortDate(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  if (!y || !m || !d) return iso;
  return new Date(Date.UTC(y, m - 1, d)).toLocaleDateString('en-GB', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  });
}

/** What the import-date button says: the range in force, or that every date counts. */
export function dateFilterLabel(filters: Pick<ReportFilters, 'from' | 'to'>): string {
  if (filters.from && filters.to) return `${shortDate(filters.from)} – ${shortDate(filters.to)}`;
  if (filters.from) return `From ${shortDate(filters.from)}`;
  if (filters.to) return `Up to ${shortDate(filters.to)}`;
  return 'All import dates';
}

/** What the second button says: the three choices it holds, or how many of them are set. */
export function scopeFilterLabel(filters: Pick<ReportFilters, 'route' | 'adviser' | 'checker'>): string {
  const set = [filters.route, filters.adviser, filters.checker].filter(Boolean).length;
  if (set === 0) return 'Route, adviser, checker';
  return set === 1 ? '1 filter' : `${set} filters`;
}
