import { describe, expect, it } from 'vitest';
import { Al_auditeventsal_command } from '../../generated/models/Al_auditeventsModel';

// Every plug-in that writes an audit event, as raw source. A command's code is declared as
// a constant on the plug-in that raises it, so the constants are the definitive list of what
// can reach the History table.
const sources = import.meta.glob('../../../../plugins/OutcomeTesting.Plugins/*.cs', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/**
 * F22, found in DEV on 2026-09-20. The case History table read "Unknown command" against
 * real entries — both fail-accountability events, and any Add/Retire/Move on a question or
 * section. The label map is generated from the al_command option set and had not been
 * regenerated since eight commands were added to it, so an audit trail that recorded the
 * action perfectly well displayed it as unknown.
 *
 * The same shape as F14 and F15: a generated artifact drifting behind Dataverse, with
 * nothing failing to announce it. This checks the map against the plug-in constants rather
 * than against a list written here, so a command added tomorrow is covered without anyone
 * remembering to add it.
 */
describe('the audit command labels the History table reads', () => {
  const declared = new Map<number, string>();

  for (const source of Object.values(sources)) {
    const pattern = /const int Command([A-Za-z]+)\s*=\s*(\d+)/g;
    let match: RegExpExecArray | null;
    while ((match = pattern.exec(source)) !== null) {
      declared.set(Number(match[2]), match[1]);
    }
  }

  const labels = Al_auditeventsal_command as Record<number, string>;

  it('finds the plug-in command constants at all', () => {
    // Guards the test itself: a rename that broke the pattern would otherwise make every
    // assertion below vacuously true.
    expect(declared.size).toBeGreaterThan(20);
    expect([...declared.values()]).toContain('SetFailAccountability');
  });

  it('has a label for every command a plug-in can write', () => {
    const missing = [...declared.entries()]
      .filter(([code]) => labels[code] === undefined)
      .map(([code, name]) => `${name} (${code})`)
      .sort();

    expect(
      missing,
      'These commands would render as "Unknown command" in the case History. ' +
        'Run: pa app refresh data-source --name al_auditevents',
    ).toEqual([]);
  });

  it('names each command the same way the plug-in does', () => {
    // RetireAndSucceed is the one deliberate difference: the constant is shortened, the
    // option set spells the command out. Everything else should agree.
    const renamed = new Set(['RetireAndSucceed']);
    const disagreements = [...declared.entries()]
      .filter(([code, name]) => !renamed.has(name) && labels[code] !== undefined && labels[code] !== name)
      .map(([code, name]) => `${code}: plug-in ${name}, option set ${labels[code]}`);

    expect(disagreements).toEqual([]);
  });
});
