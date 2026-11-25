/** What a letter row carries that grouping reads. */
interface GroupableLetter {
  code: string;
  custom: boolean;
  eventValue: number | null;
}

export interface LetterGroup<L> {
  key: string;
  name: string;
  description: string;
  letters: L[];
}

const STAGES = [
  { key: 'allocation', name: 'Allocation', description: 'When a case is given to a checker' },
  { key: 'review', name: 'Review', description: 'When a checker finishes' },
  { key: 'remediation', name: 'Remediation', description: 'When the adviser has actions to complete' },
  { key: 'signoff', name: 'Sign-off', description: 'When the T&C Manager reviews remediation, and what follows' },
  { key: 'other', name: 'Other', description: 'Letters that belong to no stage above' },
] as const;

type StageKey = (typeof STAGES)[number]['key'];

// The trigger events a letter of an administrator's own can carry (notificationRouting TRIGGER_EVENTS).
const EVENT_STAGE: Record<number, StageKey> = {
  120910800: 'allocation',
  120910801: 'review',
  120910805: 'review',
  120910802: 'remediation',
  120910803: 'signoff',
  120910804: 'signoff',
  120910806: 'signoff',
  120910807: 'signoff',
};

function stageOf(letter: GroupableLetter): StageKey {
  if (letter.custom) return (letter.eventValue !== null && EVENT_STAGE[letter.eventValue]) || 'other';
  const code = letter.code.toUpperCase();
  if (code.startsWith('ALLOCATION')) return 'allocation';
  if (code === 'REVIEW-SUBMITTED' || code === 'CASE-PASSED') return 'review';
  if (code.startsWith('REMEDIATION')) return 'remediation';
  if (code.startsWith('SIGNOFF') || code === 'RECHECK-DUE') return 'signoff';
  return 'other';
}

/** The name of the stage a letter belongs to, for the editor's heading. */
export function stageNameOf(letter: GroupableLetter): string {
  const key = stageOf(letter);
  return STAGES.find((stage) => stage.key === key)?.name ?? 'Other';
}

/** Letters by the stage of a case that sends them, stages in case order, empty stages left out. */
export function groupLetters<L extends GroupableLetter>(letters: readonly L[]): LetterGroup<L>[] {
  return STAGES.map((stage) => ({
    ...stage,
    letters: letters.filter((letter) => stageOf(letter) === stage.key),
  })).filter((group) => group.letters.length > 0);
}
