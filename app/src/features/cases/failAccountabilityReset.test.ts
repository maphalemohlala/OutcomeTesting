import { describe, expect, it } from 'vitest';

/**
 * F29, found in DEV on 2026-09-20 while working APP-065.
 *
 * Recording who carries a fail wrote correctly every time — Dataverse and the audit line
 * were exactly right — and the panel then showed the opposite. Right after a save it read
 * "Derived by default" with the ticks cleared and both named people blank, for an
 * attribution that had just been stored. It stayed wrong until the page was left and
 * returned to.
 *
 * The editor was keyed `outcome.id + '-' + reloadKey`, and onSaved bumps reloadKey the
 * instant the write returns. So React tore the editor down and built it again while the
 * re-read was still in flight, and the new instance seeded its draft from the outcome the
 * page still held — the one from before the save. The same remount took the "Accountability
 * recorded." message with it, so nothing on screen said the save had worked either.
 *
 * This matters more than the stale Checks table of F27. That one failed to show a change;
 * this one showed the change being undone. Somebody recording a regulatory attribution saw
 * the screen say it had not been recorded, and the natural response to that is to do it
 * again.
 *
 * Checked at the source. The behaviour is interactive and this app has no DOM test runner —
 * `checklistRender.test.tsx` renders to static markup, which cannot see a remount. What
 * actually went wrong is the key, and the key is a fact about the source.
 */
describe('the fail accountability editor', () => {
  const sources = import.meta.glob('./FailAccountabilityPanel.tsx', {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>;

  const panel = Object.values(sources)[0] ?? '';

  it('is reading the panel at all', () => {
    expect(panel).toContain('OutcomeAccountability');
    expect(panel).toContain('reloadKey');
  });

  it('is not rebuilt on the key that fires when the request is sent', () => {
    // A key carrying reloadKey remounts the editor at the moment of the write rather than
    // at the moment the fresh row arrives, which is what discarded the save on screen.
    const at = panel.indexOf('<OutcomeAccountability');
    const element = panel.slice(at, at + 1400);
    const keyLine = element.split('\n').find((line) => line.trim().startsWith('key='));

    expect(keyLine).toBeDefined();
    expect(
      keyLine,
      'Keying the editor on reloadKey remounts it while the re-read is still in flight, so ' +
        'it seeds itself from the row as it was before the save.',
    ).not.toContain('reloadKey');
  });

  it('takes the record’s values when the record changes', () => {
    // The replacement for the remount: the draft follows the row version, which moves when
    // the row does. Without this the editor would keep the stale draft forever instead of
    // only until the next visit.
    expect(panel).toContain('outcome.rowVersion !== seenVersion');
    for (const reset of ['setDraft(effective)', 'setFqPerson(', 'setAqPerson(']) {
      expect(panel.slice(panel.indexOf('outcome.rowVersion !== seenVersion'))).toContain(reset);
    }
  });

  it('keeps a row version to compare against', () => {
    // Guards the test above: the comparison is worthless if nothing supplies the version.
    expect(panel).toContain('useState(outcome.rowVersion)');
  });
});
