import type { IGetAllOptions } from '../generated/models/CommonModels';

/** The most rows Dataverse sends in one page. */
const PAGE_SIZE = 5000;

/** What a generated service's getAll answers, as far as paging needs it. */
interface Page<T> {
  success: boolean;
  data: T[];
  skipToken?: string;
}

/** `orderBy` must end on the primary key: paging over a non-unique order can repeat rows. */
type ReadOptions = Pick<IGetAllOptions, 'select' | 'filter'> & { orderBy: string[] };

/**
 * Every row a query matches, for a screen that counts, lists or exports a whole table.
 *
 * `top` cannot do this. The Power Apps client sends `odata.maxpagesize=500` unless told
 * otherwise, Dataverse ignores `$top` when that header is present, and the client does not
 * fetch the next page itself, so `getAll({ top: 5000 })` stops at 500 rows without saying so.
 *
 * All or nothing: a page that fails fails the read, so a short list never passes as complete.
 */
export async function readAll<T, R extends Page<T>>(
  read: (options: IGetAllOptions) => Promise<R>,
  options: ReadOptions,
): Promise<R> {
  const first = await read({ ...options, maxPageSize: PAGE_SIZE });
  if (!first.success) return first;

  const rows = [...first.data];
  let skipToken = first.skipToken;
  while (skipToken) {
    const next = await read({ ...options, maxPageSize: PAGE_SIZE, skipToken });
    if (!next.success) return next;
    rows.push(...next.data);
    skipToken = next.skipToken;
  }

  return { ...first, data: rows, skipToken: undefined };
}
