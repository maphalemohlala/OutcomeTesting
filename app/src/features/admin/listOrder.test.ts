import { describe, expect, it } from 'vitest';
import { reorderedSortOrders } from './listOrder';

const rows = [
  { id: 'a', sortOrder: 10 },
  { id: 'b', sortOrder: 20 },
  { id: 'c', sortOrder: 30 },
  { id: 'd', sortOrder: null },
];

const ids = (changes: ReturnType<typeof reorderedSortOrders<(typeof rows)[number]>>) =>
  changes.map(({ row, sortOrder }) => [row.id, sortOrder]);

describe('reorderedSortOrders', () => {
  it('swaps two neighbours and gives the unordered option its number', () => {
    expect(ids(reorderedSortOrders(rows, 1, 0))).toEqual([
      ['b', 10],
      ['a', 20],
      ['d', 40],
    ]);
  });

  it('moves the unordered option to the top and renumbers the rest', () => {
    expect(ids(reorderedSortOrders(rows, 3, 0))).toEqual([
      ['d', 10],
      ['a', 20],
      ['b', 30],
      ['c', 40],
    ]);
  });

  it('writes nothing for a move to the same place or out of range', () => {
    expect(reorderedSortOrders(rows, 2, 2)).toEqual([]);
    expect(reorderedSortOrders(rows, 0, 4)).toEqual([]);
    expect(reorderedSortOrders(rows, -1, 0)).toEqual([]);
  });
});
