import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import type { LibraryQuestion } from './useQuestionLibrary';

// The page's services import the Power Apps client, which does not load outside the player.
vi.mock('@microsoft/power-apps/data', () => ({ getClient: () => ({}) }));

const { QuestionRow } = await import('./QuestionLibraryPage');

const question = (over: Partial<LibraryQuestion>): LibraryQuestion => ({
  id: 'q',
  code: 'Q-E1-01',
  order: 1,
  wording: 'Was the advice suitable?',
  responseType: 'Pass/Fail',
  responseTypeValue: 120910005,
  mandatory: true,
  versionNumber: 1,
  retired: false,
  scheduled: null,
  retires: null,
  movedTo: null,
  ...over,
});

const row = (over: Partial<LibraryQuestion>) =>
  renderToStaticMarkup(
    <QuestionRow question={question(over)} canEdit onEdit={() => {}} onRetire={() => {}} onHistory={() => {}} />,
  );

describe('a question with a planned retirement', () => {
  it('keeps Edit and Retire, and says when it retires', () => {
    const html = row({ retires: 'Retires on 01 Dec 2026' });

    expect(html).toContain('Edit question');
    expect(html).toContain('>Retire<');
    expect(html).toContain('Retires on 01 Dec 2026');
  });

  it('still hides Edit and Retire while a next version is scheduled, and shows both notes', () => {
    const html = row({ scheduled: 'Changes on 20 Nov 2026', retires: 'Retires on 01 Dec 2026' });

    expect(html).not.toContain('Edit question');
    expect(html).not.toContain('>Retire<');
    expect(html).toContain('Changes on 20 Nov 2026');
    expect(html).toContain('Retires on 01 Dec 2026');
  });
});

describe('a question with a move pending', () => {
  it('says where it moved and offers neither Edit nor Retire, giving the server refusal', () => {
    const html = row({ movedTo: 'Q-B-01' });

    expect(html).toContain('Moved to Q-B-01');
    expect(html).toContain('already been moved to Q-B-01, so it cannot be edited or retired. Make the change to Q-B-01 instead.');
    expect(html).not.toContain('Edit question');
    expect(html).not.toContain('>Retire<');
    expect(html).toContain('History');
  });
});
