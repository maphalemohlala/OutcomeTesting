import { describe, expect, it } from 'vitest';
import { dateFilterLabel, scopeFilterLabel } from './filterLabels';

describe('dashboard filter button labels', () => {
  it('names the date range in force', () => {
    expect(dateFilterLabel({ from: '', to: '' })).toBe('All import dates');
    expect(dateFilterLabel({ from: '2026-10-12', to: '2026-11-20' })).toBe('12 Oct 2026 – 20 Nov 2026');
    expect(dateFilterLabel({ from: '2026-10-12', to: '' })).toBe('From 12 Oct 2026');
    expect(dateFilterLabel({ from: '', to: '2026-11-20' })).toBe('Up to 20 Nov 2026');
  });

  it('counts the route, adviser and checker choices set', () => {
    expect(scopeFilterLabel({ route: '', adviser: '', checker: '' })).toBe('Route, adviser, checker');
    expect(scopeFilterLabel({ route: 'AQS only', adviser: '', checker: '' })).toBe('1 filter');
    expect(scopeFilterLabel({ route: 'none', adviser: 'a', checker: 'c' })).toBe('3 filters');
  });
});
