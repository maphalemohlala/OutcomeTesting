/**
 * The sort orders to write after moving one offered option from `from` to `to`.
 *
 * Every offered option is renumbered 10, 20, 30… in its new place, and only the ones whose
 * number changes are returned, so a move usually writes two or three rows rather than all of
 * them. Options with no order yet get one, which is the point: they stop "showing last".
 */
export function reorderedSortOrders<R extends { sortOrder: number | null }>(
  rows: readonly R[],
  from: number,
  to: number,
): { row: R; sortOrder: number }[] {
  if (from === to || from < 0 || to < 0 || from >= rows.length || to >= rows.length) return [];
  const next = [...rows];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next
    .map((row, index) => ({ row, sortOrder: (index + 1) * 10 }))
    .filter(({ row, sortOrder }) => row.sortOrder !== sortOrder);
}
