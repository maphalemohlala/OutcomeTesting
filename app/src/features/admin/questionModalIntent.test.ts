import { describe, expect, it } from 'vitest';
import { intentFor, refusalFor, type QuestionDraft } from './questionModalIntent';

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

  it('treats an unpicked response type as no change rather than a version', () => {
    // Same reasoning as the emptied wording: the modal refuses to save it, so reporting it
    // as a change would let an empty picker look like an edit worth a new version.
    expect(intentFor({ ...original, responseType: null }, original)).toBe('none');
  });
});

describe('refusalFor', () => {
  const add = {
    mode: 'add' as const,
    intent: 'version' as const,
    draft: { ...original, responseType: null },
    questionCode: 'Q-E1-06',
    reason: '',
  };

  it('refuses a new question with no response type picked', () => {
    // There is no default: al_AddQuestion accepts any integer it parses, so a question
    // created without a choice here is a free-text question nobody asked for.
    expect(refusalFor(add)).toContain('how the question is answered');
  });

  it('accepts a new question once a response type is picked', () => {
    expect(refusalFor({ ...add, draft: { ...original, responseType: 120910006 } })).toBeNull();
  });

  it('names the wording before the response type', () => {
    expect(refusalFor({ ...add, draft: { ...original, wording: '  ', responseType: null } })).toBe(
      'Enter the question wording.',
    );
  });

  it('refuses a new question with no code', () => {
    expect(
      refusalFor({ ...add, draft: { ...original, responseType: 120910006 }, questionCode: ' ' }),
    ).toContain('question code');
  });

  it('does not ask a move for a response type, which it carries forward', () => {
    // The control is disabled on a move and the value is taken from the retired version, so
    // there is nothing for the administrator to have picked.
    expect(
      refusalFor({
        mode: 'edit',
        intent: 'move',
        draft: { ...original, sectionId: 'sec-2', responseType: null },
        questionCode: 'Q-E2-04',
        reason: 'Belongs under outcome 2.',
      }),
    ).toBeNull();
  });

  it('refuses a move with no reason', () => {
    expect(
      refusalFor({
        mode: 'edit',
        intent: 'move',
        draft: { ...original, sectionId: 'sec-2' },
        questionCode: 'Q-E2-04',
        reason: '   ',
      }),
    ).toContain('why the question is moving');
  });

  it('does not ask for wording on a move, which carries the wording forward', () => {
    expect(
      refusalFor({
        mode: 'edit',
        intent: 'move',
        draft: { ...original, sectionId: 'sec-2', wording: '' },
        questionCode: 'Q-E2-04',
        reason: 'Belongs under outcome 2.',
      }),
    ).toBeNull();
  });

  it('refuses a move with no new code', () => {
    expect(
      refusalFor({
        mode: 'edit',
        intent: 'move',
        draft: { ...original, sectionId: 'sec-2' },
        questionCode: '',
        reason: 'Belongs under outcome 2.',
      }),
    ).toContain('new code');
  });
});
