import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const sent = { count: 0 };

vi.mock('../../generated', () => {
  const service = () => ({ getAll: vi.fn() });
  return {
    Al_listoption_al_outcomecase_productssetService: service(),
    Al_listoptionsService: service(),
    Al_outcomecasesService: service(),
    Al_outcomesService: service(),
    Al_remediationactionsService: service(),
    Al_reviewinstancesService: service(),
    ContactsService: service(),
  };
});
vi.mock('../../services/errors', () => ({ logTechnical: vi.fn() }));
vi.mock('../../services/commands/commandClient', () => ({ commandsSent: () => sent.count }));

import * as generated from '../../generated';
import {
  cachedCaseWorklist,
  loadCaseWorklist,
  resetCaseWorklistForTests,
  useCaseWorklist,
} from './useCaseWorklist';

type Read = ReturnType<typeof vi.fn>;
const services = generated as unknown as Record<string, { getAll: Read }>;
const cases = services.Al_outcomecasesService.getAll;

const caseRow = (id: string) => ({ al_outcomecaseid: id, al_casereference: `REF-${id}`, al_casestatus: 120910000 });

/** Every table answers empty except the cases. */
function answer(caseRows: object[] | 'refused') {
  for (const service of Object.values(services)) {
    service.getAll.mockResolvedValue({ success: true, data: [] });
  }
  cases.mockResolvedValue(
    caseRows === 'refused' ? { success: false, data: [], error: new Error('refused') } : { success: true, data: caseRows },
  );
}

/** What a screen draws on its first render, before any effect has run. */
function firstRender(): string {
  const Probe = () => {
    const state = useCaseWorklist();
    return state.status === 'ready' ? state.cases.map((c) => c.caseReference).join(',') : state.status;
  };
  return renderToStaticMarkup(createElement(Probe));
}

describe('the case list kept between screens', () => {
  beforeEach(() => {
    resetCaseWorklistForTests();
    sent.count = 0;
    for (const service of Object.values(services)) service.getAll.mockReset();
  });

  it('opens loading the first time', () => {
    expect(firstRender()).toBe('loading');
  });

  it('opens the next screen on the rows already read, and still reads again', async () => {
    answer([caseRow('1')]);
    await loadCaseWorklist();
    expect(cases).toHaveBeenCalledTimes(1);

    expect(firstRender()).toBe('REF-1');

    answer([caseRow('1'), caseRow('2')]);
    const fresh = await loadCaseWorklist();

    expect(cases).toHaveBeenCalledTimes(2);
    expect(fresh.status === 'ready' && fresh.cases.map((c) => c.id)).toEqual(['1', '2']);
    expect(firstRender()).toBe('REF-1,REF-2');
  });

  it('does not show rows from before a command was sent', async () => {
    answer([caseRow('1')]);
    await loadCaseWorklist();

    sent.count += 1;

    expect(cachedCaseWorklist()).toEqual({ status: 'loading' });
    expect(firstRender()).toBe('loading');
  });

  it('does not keep a read that a command overtook', async () => {
    answer([caseRow('1')]);
    const read = loadCaseWorklist();
    sent.count += 1;
    await read;

    expect(cachedCaseWorklist()).toEqual({ status: 'loading' });
  });

  it('says the list could not be loaded when the read behind the rows fails, and forgets them', async () => {
    answer([caseRow('1')]);
    await loadCaseWorklist();
    expect(firstRender()).toBe('REF-1');

    answer('refused');
    const failed = await loadCaseWorklist();

    // what the hook now shows in place of the rows
    expect(failed).toMatchObject({
      status: 'unavailable',
      reason: 'The case list could not be loaded right now. Please try again later.',
    });
    // and the next screen does not open on rows nobody could confirm
    expect(cachedCaseWorklist()).toEqual({ status: 'loading' });
    expect(firstRender()).toBe('loading');
  });

  it('does not keep a read begun while a command was in flight', async () => {
    answer([caseRow('1')]);
    sent.count += 1; // the command is sent
    const read = loadCaseWorklist();
    await read;
    expect(firstRender()).toBe('REF-1');

    sent.count += 1; // and settles

    expect(cachedCaseWorklist()).toEqual({ status: 'loading' });
    expect(firstRender()).toBe('loading');
  });

  it('keeps the outcome and remediation rows for the reports page when the cases read throws', async () => {
    answer([]);
    services.Al_outcomesService.getAll.mockResolvedValue({ success: true, data: [{ al_outcomeid: 'o1' }] });
    cases.mockRejectedValue(new Error('offline'));

    const state = await loadCaseWorklist();

    expect(state).toMatchObject({ status: 'unavailable', outcomes: [{ al_outcomeid: 'o1' }], actions: [] });
  });

  it('hands the reports page the outcome and remediation rows it read, or null where refused', async () => {
    answer([caseRow('1')]);
    services.Al_outcomesService.getAll.mockResolvedValue({ success: true, data: [{ al_outcomeid: 'o1' }] });
    services.Al_remediationactionsService.getAll.mockResolvedValue({ success: false, data: [], error: new Error('refused') });

    const state = await loadCaseWorklist();

    expect(state).toMatchObject({ status: 'ready', outcomes: [{ al_outcomeid: 'o1' }], actions: null, remediationKnown: false });
  });
});
