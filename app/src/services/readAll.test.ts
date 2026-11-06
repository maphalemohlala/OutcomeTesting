import { describe, expect, it } from 'vitest';
import { readAll } from './readAll';
import type { IGetAllOptions } from '../generated/models/CommonModels';

function pages(...answers: { success: boolean; data: number[]; skipToken?: string }[]) {
  const calls: IGetAllOptions[] = [];
  const read = async (options: IGetAllOptions) => {
    calls.push(options);
    return answers[calls.length - 1];
  };
  return { read, calls };
}

describe('readAll', () => {
  it('follows every page and keeps the query the same on each', async () => {
    const { read, calls } = pages(
      { success: true, data: [1, 2], skipToken: 'a' },
      { success: true, data: [3], skipToken: 'b' },
      { success: true, data: [4] },
    );

    const result = await readAll<number, Awaited<ReturnType<typeof read>>>(read, {
      filter: 'x eq 1',
      orderBy: ['id asc'],
    });

    expect(result.data).toEqual([1, 2, 3, 4]);
    expect(calls.map((call) => call.skipToken)).toEqual([undefined, 'a', 'b']);
    expect(calls.every((call) => call.filter === 'x eq 1' && call.orderBy?.[0] === 'id asc')).toBe(true);
  });

  it('asks for the largest page, because the client default of 500 also disables top', async () => {
    const { read, calls } = pages({ success: true, data: [] });

    await readAll<number, Awaited<ReturnType<typeof read>>>(read, { orderBy: ['id asc'] });

    expect(calls[0].maxPageSize).toBe(5000);
    expect(calls[0].top).toBeUndefined();
  });

  it('never hands back part of a table as if it were all of it', async () => {
    const { read } = pages(
      { success: true, data: [1], skipToken: 'a' },
      { success: false, data: [] },
    );

    const result = await readAll<number, Awaited<ReturnType<typeof read>>>(read, { orderBy: ['id asc'] });

    expect(result.success).toBe(false);
  });
});
