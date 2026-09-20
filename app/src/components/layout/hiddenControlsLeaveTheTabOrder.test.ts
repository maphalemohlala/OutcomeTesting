import { describe, expect, it } from 'vitest';

/**
 * That nothing a keyboard can reach is invisible when it gets there.
 *
 * F35, found in DEV on 2026-09-20 working APP-164. Tabbing off "Upload cases" on the Case
 * intake page put focus on a 1x1 clipped `<input type="file">` with no accessible name and
 * no visible focus ring - the real control is the button beside it, which opens the picker
 * by calling `click()` on this input. A keyboard user saw the focus ring vanish for one
 * press and a screen reader announced an unlabelled file field.
 *
 * `.visually-hidden` clips rather than removing, which is exactly right for a heading or a
 * table caption a screen reader should still read, and exactly wrong for a control: the
 * element stays focusable and stays in the accessibility tree. So the class is safe on
 * everything except the handful of tags a browser focuses by default.
 *
 * The skip link is the deliberate opposite - hidden until focused, then visible - and it
 * carries its own `.skip-link` class rather than this one, which is why the rule can be
 * this blunt.
 */
const sources = import.meta.glob('../../**/*.tsx', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/** The tags a browser puts in the tab order without being asked. */
const FOCUSABLE = /<(input|button|select|textarea|a)\s[^>]*?>/g;

describe('controls that are hidden but still there', () => {
  it('reads the whole app, not one folder', () => {
    // Guards the test itself. F30 was a guard that only looked where its own bug was.
    expect(Object.keys(sources).length).toBeGreaterThan(20);
    expect(Object.keys(sources).some((p) => p.includes('/features/imports/'))).toBe(true);
  });

  it('are taken out of the tab order, so focus never lands on nothing', () => {
    const offenders: string[] = [];

    for (const [path, source] of Object.entries(sources)) {
      for (const match of source.matchAll(FOCUSABLE)) {
        const tag = match[0];
        if (!tag.includes('visually-hidden')) continue;
        if (tag.includes('tabIndex={-1}')) continue;

        offenders.push(`${path}: ${tag.replace(/\s+/g, ' ').slice(0, 80)}`);
      }
    }

    expect(
      offenders.sort(),
      'A focusable element with .visually-hidden is clipped, not removed: it keeps its place ' +
        'in the tab order and in the accessibility tree. Tabbing onto it moves focus to ' +
        'something nobody can see. Give it tabIndex={-1} and drive it from the visible ' +
        'control beside it (F35).',
    ).toEqual([]);
  });

  it('leaves the skip link alone, because it is meant to be reachable', () => {
    // The one control that is hidden until focused and must stay in the tab order. It uses
    // .skip-link, not .visually-hidden, which is what keeps the rule above from catching it.
    const shell = Object.entries(sources).find(([path]) =>
      path.endsWith('/AppShell.tsx'),
    )?.[1];

    expect(shell).toBeTypeOf('string');
    expect(shell).toContain('className="skip-link"');
    expect(shell).not.toContain('className="visually-hidden" href');
  });
});
