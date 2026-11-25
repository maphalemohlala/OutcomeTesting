import { describe, expect, it } from 'vitest';
import { splitRemediationAreas } from './remediationAreas';

const q = (wording: string, over: Partial<{ scheduled: string; retires: string; movedTo: string }> = {}) => ({
  wording,
  scheduled: null,
  retires: null,
  movedTo: null,
  ...over,
});

describe('splitRemediationAreas', () => {
  it('pairs required and comments by area, keeping the other questions as rows in order', () => {
    const reason = q('Tax check reason');
    const calcReq = q('Calculations: remediation required?');
    const calcCom = q('Calculations: remediation comments');
    const otherReq = q('Other: remediation required?');
    const otherCom = q('other: Remediation comments');
    const { rows, areas } = splitRemediationAreas([reason, calcReq, calcCom, otherReq, otherCom]);

    expect(rows).toEqual([reason]);
    expect(areas.map((a) => a.area)).toEqual(['Calculations', 'Other']);
    expect(areas[0].required).toBe(calcReq);
    expect(areas[0].comments).toBe(calcCom);
  });

  it('leaves a half pair as an ordinary row', () => {
    const lonely = q('Omissions: remediation required?');
    const { rows, areas } = splitRemediationAreas([lonely, q('Calculations: remediation comments')]);
    expect(areas).toEqual([]);
    expect(rows).toHaveLength(2);
    expect(rows[0]).toBe(lonely);
  });

  it('does not pair a question with a dated change, so its note stays visible', () => {
    const { rows, areas } = splitRemediationAreas([
      q('Calculations: remediation required?', { scheduled: 'Changes on 20 Nov 2026' }),
      q('Calculations: remediation comments'),
    ]);
    expect(areas).toEqual([]);
    expect(rows).toHaveLength(2);
  });
});
