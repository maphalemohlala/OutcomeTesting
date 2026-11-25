import { useState } from 'react';

export interface Page<T> {
  rows: T[];
  /** 1-based index of the first row shown; 0 when there are none. */
  from: number;
  to: number;
  total: number;
  page: number;
  pages: number;
}

/** Cuts a list into pages. A page past the end (the list shrank) falls back to the last one. */
export function pageOf<T>(items: readonly T[], page: number, size: number): Page<T> {
  const pages = Math.max(1, Math.ceil(items.length / size));
  const current = Math.min(Math.max(1, page), pages);
  const start = (current - 1) * size;
  const rows = items.slice(start, start + size);
  return {
    rows,
    from: rows.length ? start + 1 : 0,
    to: start + rows.length,
    total: items.length,
    page: current,
    pages,
  };
}

/** Rows per page on every paged list. */
export const PAGE_SIZE = 15;

/**
 * Page state for one list. Pass the filtered list. When `resetKey` (e.g. the filter values
 * joined into a string) changes, the list goes back to page 1 without an effect: the page
 * number is held against the key it was chosen under.
 */
export function usePaged<T>(items: readonly T[], size = PAGE_SIZE, resetKey: string | number = '') {
  const [state, setState] = useState({ key: resetKey, page: 1 });
  const page = state.key === resetKey ? state.page : 1;
  return { ...pageOf(items, page, size), setPage: (next: number) => setState({ key: resetKey, page: next }) };
}
