import { describe, expect, it } from 'vitest';
import { intentFor, type QuestionDraft } from './questionModalIntent';

const original: QuestionDraft = {
  wording: 'Is the advice suitable?',
  sectionId: 'sec-1',
  responseType: 120910006,
  mandatory: true,
  displayOrder: 3,
};

describe('intentFor', () => {
  it('does nothing when nothing changed', () => {
    expect(intentFor({ ...original }, original)).toBe('none');
  });

  it('creates a new version when the wording changed', () => {
    expect(intentFor({ ...original, wording: 'Is it suitable?' }, original)).toBe('version');
  });

  it('creates a new version when the response type changed', () => {
    expect(intentFor({ ...original, responseType: 120910008 }, original)).toBe('version');
  });

  it('creates a new version when mandatory changed', () => {
    expect(intentFor({ ...original, mandatory: false }, original)).toBe('version');
  });

  it('creates a new version when the display order changed', () => {
    expect(intentFor({ ...original, displayOrder: 1 }, original)).toBe('version');
  });

  it('moves when the section changed', () => {
    expect(intentFor({ ...original, sectionId: 'sec-2' }, original)).toBe('move');
  });

  it('moves when the section changed and so did everything else', () => {
    // A move carries the wording forward from the retired version, so the move wins and the
    // caller is told the other edits are not applied.
    expect(
      intentFor({ ...original, sectionId: 'sec-2', wording: 'Changed', mandatory: false }, original),
    ).toBe('move');
  });

  it('ignores whitespace-only differences in wording', () => {
    expect(intentFor({ ...original, wording: '  Is the advice suitable?  ' }, original)).toBe(
      'none',
    );
  });

  it('treats an emptied wording as no change rather than a version', () => {
    // The modal refuses to save empty wording; reporting it as a change here would let a
    // blank textarea look like an edit worth a new version.
    expect(intentFor({ ...original, wording: '   ' }, original)).toBe('none');
  });
});
