import { describe, expect, it, vi } from 'vitest';

const getAll = vi.fn();

vi.mock('../../generated', () => ({
  Al_exportbatchesService: { getAll: (o: unknown) => getAll(o) },
  Al_exportrecordsService: { getAll: vi.fn() },
}));

const { loadExportBatch } = await import('./useExports');
const { fileRange, previousBatchGeneratedOn } = await import('./trailLight');

const MON = '00000000-0000-4000-8000-000000000002';

describe('the batch page reads the batches before it', () => {
  it("so Monday's default daily file starts on Friday, not Sunday", async () => {
    getAll.mockReset();
    getAll
      .mockResolvedValueOnce({ success: true, data: [{ al_exportbatchid: MON, al_generatedon: '2026-11-16T09:00:00Z' }] })
      .mockResolvedValueOnce({ success: true, data: [{ al_exportbatchid: 'fri', al_generatedon: '2026-11-13T09:00:00Z' }] });

    const state = await loadExportBatch(MON);

    expect(getAll.mock.calls[1][0]).toMatchObject({ filter: 'al_generatedon lt 2026-11-16T09:00:00.000Z' });
    expect(state.status).toBe('ready');
    if (state.status !== 'ready') return;
    const own = state.batches.find((b) => b.id === MON)!;
    expect(fileRange('daily', '', '', own.generatedOn, previousBatchGeneratedOn(state.batches, MON))).toEqual({
      from: '2026-11-13',
      to: '2026-11-15',
    });
  });

  it('does not pass off a failed read of the earlier batches as a first batch', async () => {
    getAll.mockReset();
    getAll
      .mockResolvedValueOnce({ success: true, data: [{ al_exportbatchid: MON, al_generatedon: '2026-11-16T09:00:00Z' }] })
      .mockResolvedValueOnce({ success: false, data: [] });

    expect((await loadExportBatch(MON)).status).toBe('unavailable');
  });

  it('asks nothing more for a batch not yet generated', async () => {
    getAll.mockReset();
    getAll.mockResolvedValueOnce({ success: true, data: [{ al_exportbatchid: MON, al_generatedon: null }] });

    expect(await loadExportBatch(MON)).toMatchObject({ status: 'ready', batches: [{ id: MON }] });
    expect(getAll).toHaveBeenCalledTimes(1);
  });
});
