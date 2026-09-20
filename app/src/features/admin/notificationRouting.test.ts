import { describe, expect, it } from 'vitest';

// The C# is authoritative. These are read as text rather than imported, because the point is
// to notice when somebody changes one side and not the other (the AD-041 pattern, as
// notificationTemplates.test.ts already does for the letter catalogue).
import outboxSource from '../../../../plugins/OutcomeTesting.Plugins/NotificationOutbox.cs?raw';
import recipientsSource from '../../../../plugins/OutcomeTesting.Plugins/NotificationRecipients.cs?raw';
import rowsSource from '../../../../plugins/OutcomeTesting.Plugins/NotificationTemplateRows.cs?raw';
import { TEMPLATE_CODES, templateHint } from './notificationTemplates';
import {
  ALWAYS_ALLOWED_TOKENS,
  TOKEN_COMPLETED_CHECK,
  tokensFor,
  CUSTOM_TOKENS,
  TOKEN_HELP,
  tokenHelp,
  KIND_CONTACT,
  RECIPIENT_KINDS,
  TRIGGER_EVENTS,
  eventLabel,
  normaliseCode,
  recipientLabel,
  unknownCustomTokens,
} from './notificationRouting';

describe('the events a letter can be attached to', () => {
  it('lists exactly the events the plug-in raises', () => {
    // KnownEvents() is what the guard validates against, so a value here that is not there
    // would offer an administrator an event nothing ever fires.
    const block = outboxSource.slice(
      outboxSource.indexOf('public static int[] KnownEvents()'),
      outboxSource.indexOf('/// Writes one outbox row'),
    );

    const named = [...block.matchAll(/Event([A-Za-z]+),/g)].map((m) => m[1]);
    expect(named).toHaveLength(TRIGGER_EVENTS.length);
  });

  it('gives every event the value the plug-in gives it', () => {
    for (const event of TRIGGER_EVENTS) {
      expect(outboxSource).toContain(`= ${event.value};`);
    }
  });

  it('gives every event the wording the plug-in gives it', () => {
    // The label is what an administrator picks from; a different word here and in the
    // notification's own name would read as two different events.
    for (const event of TRIGGER_EVENTS) {
      expect(outboxSource).toContain(`return "${event.label}";`);
    }
  });
});

describe('who a letter can go to', () => {
  it('gives every kind the value the plug-in gives it', () => {
    for (const kind of RECIPIENT_KINDS) {
      expect(recipientsSource).toContain(`= ${kind.value};`);
    }
  });

  it('gives every kind the wording the plug-in gives it', () => {
    for (const kind of RECIPIENT_KINDS) {
      expect(recipientsSource).toContain(`return "${kind.label}";`);
    }
  });

  it('agrees which kind needs a contact named with it', () => {
    expect(RECIPIENT_KINDS.some((k) => k.value === KIND_CONTACT)).toBe(true);
    expect(recipientsSource).toContain(`public const int KindContact = ${KIND_CONTACT};`);
  });
});

describe('the tokens a letter of your own may use', () => {
  it('matches the plug-in list exactly', () => {
    // Narrower than a built-in letter's set, and deliberately so. Offering {{grading}} here
    // would produce a letter that reads correctly on the screen and arrives with a gap.
    const block = rowsSource.slice(
      rowsSource.indexOf('public static readonly string[] CustomTokens'),
      rowsSource.indexOf('};', rowsSource.indexOf('public static readonly string[] CustomTokens')),
    );

    const named = [...block.matchAll(/NotificationTemplates\.Token([A-Za-z]+),/g)].map((m) =>
      m[1].charAt(0).toLowerCase() + m[1].slice(1),
    );

    expect(named).toEqual([...CUSTOM_TOKENS]);
  });
});

describe('what the editor can catch before a round trip', () => {
  it('reports a token a letter of your own cannot supply', () => {
    expect(unknownCustomTokens('Case {{reference}}', '<p>{{grading}}</p>')).toEqual(['grading']);
  });

  it('accepts every token a case can always answer', () => {
    expect(
      unknownCustomTokens(
        'Case {{reference}}',
        '<p>{{client}} {{adviser}} {{caseLink}} {{caseButton}}</p>',
      ),
    ).toEqual([]);
  });

  it('names a token once however often it appears', () => {
    expect(unknownCustomTokens('{{grading}}', '{{grading}} {{grading}}')).toEqual(['grading']);
  });

  it('reads a label back for a value, and nothing for one it does not know', () => {
    expect(eventLabel(120910805)).toBe('Case passed');
    expect(eventLabel(999)).toBeNull();
    expect(recipientLabel(120910811)).toBe('T&C Manager');
    expect(recipientLabel(999)).toBeNull();
  });
});

describe('normalising a code somebody typed', () => {
  it('makes one code out of the ways a person would type it', () => {
    // The code is the table's alternate key and is matched exactly, so these must not become
    // two rows that look like one in a list.
    expect(normaliseCode('tell the manager')).toBe('TELL-THE-MANAGER');
    expect(normaliseCode('  Tell  The Manager  ')).toBe('TELL-THE-MANAGER');
    expect(normaliseCode('tell_the_manager')).toBe('TELL-THE-MANAGER');
  });

  it('does not leave a hyphen dangling at either end', () => {
    expect(normaliseCode('-tell-')).toBe('TELL');
    expect(normaliseCode('tell!!')).toBe('TELL');
  });

  it('gives an empty string for something with no code in it at all', () => {
    // The caller treats this as "no code given" rather than saving a row keyed on nothing.
    expect(normaliseCode('  !!  ')).toBe('');
  });
});

describe('what each token does', () => {
  it('explains every token any letter can use', () => {
    // The picker exists because the old list named the tokens and said nothing about what any
    // of them did. A token gaining a definition in the C# without gaining a line here would
    // put it back in that state for one entry, silently.
    const used = new Set<string>([...CUSTOM_TOKENS, ...ALWAYS_ALLOWED_TOKENS]);
    for (const code of TEMPLATE_CODES) {
      for (const token of templateHint(code)!.tokens) {
        used.add(token);
      }
    }

    const missing = [...used].filter((t) => !(t in TOKEN_HELP));
    expect(missing).toEqual([]);
  });

  it('describes no token that no letter uses', () => {
    // The other direction: a description left behind for a token that has been removed would
    // offer an administrator something the server now refuses.
    const used = new Set<string>([...CUSTOM_TOKENS, ...ALWAYS_ALLOWED_TOKENS]);
    for (const code of TEMPLATE_CODES) {
      for (const token of templateHint(code)!.tokens) {
        used.add(token);
      }
    }

    expect(Object.keys(TOKEN_HELP).filter((t) => !used.has(t))).toEqual([]);
  });

  it('tells the two link tokens apart, which is the pair that matters', () => {
    // caseLink and caseButton are a web address and a button. Reading only their names, the
    // difference is a guess.
    expect(tokenHelp('caseLink')).not.toBe(tokenHelp('caseButton'));
    expect(tokenHelp('caseLink').toLowerCase()).toContain('address');
    expect(tokenHelp('caseButton').toLowerCase()).toContain('button');
  });

  it('says when a token can legitimately come out as nothing', () => {
    // dueText and notes are written to be dropped into a sentence and are often empty. An
    // administrator who does not know that reads a gap as a fault.
    expect(tokenHelp('dueText').toLowerCase()).toContain('nothing');
    expect(tokenHelp('notes').toLowerCase()).toContain('nothing');
  });

  it('falls back rather than showing an empty line for an unknown token', () => {
    expect(tokenHelp('nonesuch')).not.toBe('');
  });
});

describe('the attachment marker', () => {
  it('is offered on every letter, not owned by any one of them', () => {
    // It says what the letter CARRIES rather than what it says.
    for (const code of TEMPLATE_CODES) {
      expect(tokensFor(templateHint(code)!.tokens)).toContain(TOKEN_COMPLETED_CHECK);
      expect(templateHint(code)!.tokens).not.toContain(TOKEN_COMPLETED_CHECK);
    }
  });

  it('is offered on a letter of your own too', () => {
    expect(tokensFor(CUSTOM_TOKENS)).toContain(TOKEN_COMPLETED_CHECK);
  });

  it('is not reported as a token the letter cannot supply', () => {
    // The editor must not refuse what the server allows.
    expect(unknownCustomTokens('Subject', `{{${TOKEN_COMPLETED_CHECK}}}`)).toEqual([]);
  });

  it('is never offered twice', () => {
    expect(tokensFor([TOKEN_COMPLETED_CHECK, 'reference'])).toEqual([
      TOKEN_COMPLETED_CHECK,
      'reference',
    ]);
  });

  it('says it puts nothing in the text, which is the surprising part', () => {
    expect(tokenHelp(TOKEN_COMPLETED_CHECK).toLowerCase()).toContain('nothing in the text');
  });
});
