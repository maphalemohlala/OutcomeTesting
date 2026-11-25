import { describe, expect, it } from 'vitest';
import { pageOf } from './paging';

const items = Array.from({ length: 23 }, (_, i) => i + 1);

describe('pageOf', () => {
  it('cuts the first page', () => {
    const p = pageOf(items, 1, 10);
    expect([p.from, p.to, p.total, p.pages]).toEqual([1, 10, 23, 3]);
    expect(p.rows[0]).toBe(1);
  });

  it('holds the short last page', () => {
    const p = pageOf(items, 3, 10);
    expect([p.from, p.to]).toEqual([21, 23]);
  });

  it('falls back to the last page when the list shrank under it', () => {
    expect(pageOf(items.slice(0, 5), 3, 10).page).toBe(1);
  });

  it('reports nothing for an empty list', () => {
    const p = pageOf([], 1, 10);
    expect([p.from, p.to, p.total, p.pages]).toEqual([0, 0, 0, 1]);
  });
});
