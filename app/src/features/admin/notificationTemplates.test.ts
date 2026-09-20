import { describe, expect, it } from 'vitest';
// Imported as text rather than read with node:fs, for the reason protectedQuestions.test.ts
// gives: tsconfig.app.json restricts ambient types to vite/client on purpose.
import templatesSource from '../../../../plugins/OutcomeTesting.Plugins/NotificationTemplates.cs?raw';
import { TEMPLATE_CODES, templateHint, unknownTokens } from './notificationTemplates';
import { ALWAYS_ALLOWED_TOKENS } from './notificationRouting';
import { can, pageResourceForPath, resolvePermissions } from '../../types/permissions';

/**
 * Mirrors NotificationTemplates in plugins/OutcomeTesting.Plugins, which is authoritative:
 * NotificationTemplateGuardPlugin refuses a bad template server-side whatever this file
 * says, and the table is reachable from the Web API where this file is not involved at all.
 *
 * The drift test is the point of the file. A token list that silently fell behind the C#
 * would tell an administrator their wording was fine and then have the server refuse it, or
 * worse, accept a token that renders as a gap in a letter somebody receives.
 */
describe('notificationTemplates', () => {
  /** Every `public const string X = "CODE";` the C# declares, excluding the token names. */
  function codesInCSharp(): string[] {
    const codes: string[] = [];
    for (const match of templatesSource.matchAll(
      /public const string \w+ = "([A-Z][A-Z0-9-]*)";/g,
    )) {
      codes.push(match[1]);
    }
    return codes;
  }

  it('carries exactly the codes the plug-in assembly declares', () => {
    // The C# also declares the table and column names as constants; those are lower case
    // with underscores, so the pattern above does not pick them up.
    const fromCSharp = codesInCSharp().filter((c) => !c.startsWith('AL_'));

    expect([...TEMPLATE_CODES].sort()).toEqual([...fromCSharp].sort());
  });

  it('names every token the C# defines', () => {
    const tokensInCSharp = [
      ...templatesSource.matchAll(/public const string Token\w+ = "(\w+)";/g),
    ].map((m) => m[1]);

    // The always-allowed ones belong to no single letter by design (AD-171): they say what a
    // letter CARRIES rather than what it says, so naming them in twelve lists would invite the
    // twelve to disagree. They are still offered on every letter, by tokensFor.
    const used = new Set<string>(ALWAYS_ALLOWED_TOKENS);
    for (const code of TEMPLATE_CODES) {
      for (const token of templateHint(code)!.tokens) used.add(token);
    }

    // Every token the C# defines is offered somewhere. A token defined and never offered
    // would be one no administrator could ever discover.
    for (const token of tokensInCSharp) {
      expect(used.has(token)).toBe(true);
    }
  });

  it('knows nothing about a code that is not ours', () => {
    expect(templateHint('NOT-A-LETTER')).toBeNull();
  });

  it('reports a token the letter does not supply', () => {
    expect(unknownTokens('SIGNOFF-DUE', 'Subject', 'Dear {{adviser}}')).toEqual(['adviser']);
  });

  it('accepts the tokens a letter does supply', () => {
    expect(unknownTokens('SIGNOFF-DUE', 'Case {{reference}}', 'Case {{reference}} needs you.')).toEqual(
      [],
    );
  });

  it('reports each offender once however often it appears', () => {
    expect(unknownTokens('SIGNOFF-DUE', '{{nope}}', '{{nope}} {{nope}}')).toEqual(['nope']);
  });

  it('is granted to the people who run the checking process', () => {
    // Not administrators alone: the letters are the checking team's words to advisers.
    expect(can(resolvePermissions(['AL Portal - Outcome Testing Manager']), 'page.admin.templates', 'Manage')).toBe(true);
    expect(can(resolvePermissions(['AL Portal - Adviser Remediation']), 'page.admin.templates', 'View')).toBe(false);
  });

  it('is the resource for its path', () => {
    expect(pageResourceForPath('/admin/templates')).toBe('page.admin.templates');
  });

  it('says nothing about a code it does not know, rather than guessing', () => {
    // An unknown code is the server's refusal to make, and it names the code. Reporting
    // every token as unknown here would bury that.
    expect(unknownTokens('NOT-A-LETTER', '{{anything}}', '')).toEqual([]);
  });
});
