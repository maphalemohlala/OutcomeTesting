import { describe, expect, it } from 'vitest';
import { choicesIncludingHeld, managedList, toOptionRows } from './listOptions';

const list = managedList('products');
const raw = [
  { al_listoptionid: 'live', al_name: 'ISA', al_list: list.value },
  { al_listoptionid: 'old', al_name: 'Bond', al_list: list.value, al_effectiveto: '2026-01-01' },
  { al_listoptionid: 'older', al_name: 'Annuity', al_list: list.value, al_effectiveto: '2025-01-01' },
];
const rows = toOptionRows(raw as never, list, new Date('2026-11-16'));

describe('a tick list a case holds retired products on', () => {
  it('still offers each retired product the case holds, so it can be unticked', () => {
    const ids = choicesIncludingHeld(rows, ['live', 'old']).map((o) => o.id);
    expect(ids).toEqual(['live', 'old']);
  });

  it('marks them retired, and offers no retired product the case does not hold', () => {
    const labels = choicesIncludingHeld(rows, ['old']).map((o) => o.label);
    expect(labels).toEqual(['ISA', 'Bond (retired)']);
  });

  it('keeps the single-value form working', () => {
    expect(choicesIncludingHeld(rows, 'older').map((o) => o.id)).toEqual(['live', 'older']);
    expect(choicesIncludingHeld(rows, null).map((o) => o.id)).toEqual(['live']);
  });
});
