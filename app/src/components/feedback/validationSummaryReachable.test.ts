import { describe, expect, it } from 'vitest';

/**
 * That the app's own error summary is the thing a user actually sees.
 *
 * F34, found in DEV on 2026-09-20 working APP-099. Pressing "Close exception" on an empty
 * close-an-import-row form did nothing visible: focus jumped into the note box and no
 * message appeared. The form validates itself and had two things to say - "Choose whether
 * the row was resolved or ignored." and "Say what was done about the row. It is recorded
 * permanently." - and said neither, because the textarea carried the HTML `required`
 * attribute. The browser blocked submit before React's handler ran, so `onSubmit` never
 * executed and `ValidationSummary` never rendered. Typing one character into the note made
 * the summary appear at once, naming the radio group the native bubble cannot mention.
 *
 * The rule is checked over every form rather than that one file, because the trap is
 * structural: a `required` attribute anywhere in a self-validating form silently demotes
 * the summary to a per-field tooltip, and nothing fails to announce it.
 */
const sources = import.meta.glob('../../features/**/*.tsx', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

describe('the forms that render their own error summary', () => {
  const selfValidating = Object.entries(sources).filter(
    ([, source]) => source.includes('<ValidationSummary'),
  );

  it('finds the forms at all', () => {
    // Guards the test itself: a rename of the component would otherwise make every
    // assertion below vacuously true.
    expect(selfValidating.length).toBeGreaterThanOrEqual(3);
    expect(Object.keys(sources).length).toBeGreaterThan(20);
  });

  it('turn the browser validation off, so their own summary is what speaks', () => {
    const missing = selfValidating
      .filter(([, source]) => !source.includes('noValidate'))
      .map(([path]) => path)
      .sort();

    expect(
      missing,
      'A form that renders a ValidationSummary must carry noValidate. Without it a single ' +
        'required attribute lets the browser block submit first, so the handler never runs, ' +
        'the summary never renders, and the user is told about one field at a time - never ' +
        'about a radio group or anything else the native bubble cannot reach (F34).',
    ).toEqual([]);
  });

  it('still says what is wrong for every field, not only the first', () => {
    // The behaviour noValidate preserves. The close-an-import-row form collects both
    // problems before it returns, rather than stopping at the first.
    const form = sources['../../features/imports/ResolveExceptionForm.tsx'];

    expect(form).toBeTypeOf('string');
    expect(form).toContain('Choose whether the row was resolved or ignored.');
    expect(form).toContain('Say what was done about the row. It is recorded permanently.');

    // Both pushed onto one list, then set together - not two early returns.
    expect(form).toMatch(/found\.push[\s\S]*found\.push[\s\S]*setErrors\(found\)/);
  });
});
