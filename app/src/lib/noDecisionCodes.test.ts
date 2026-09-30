import ts from 'typescript';
import { describe, expect, it } from 'vitest';

/**
 * No decision or requirement code in anything a user sees (project owner, 2026-09-30:
 * "Most of the app messages show the decision and requirements code like AD_05 etc.
 * Remove all that").
 *
 * The codes belong to the decision log and the requirements, which the advisers, checkers
 * and managers using the app have never seen. They stay in comments, docs and commit
 * messages; this scans only what reaches a screen, an email or a refusal:
 *   - the Code App's string literals, template literals and JSX text;
 *   - the plug-ins' string literals (refusals, audit history, notification text);
 *   - the portal's templates, snippets and scripts, with Liquid, HTML and JS comments
 *     taken out.
 */

// Imported as text, as the other source-scanning tests do: the app is type-checked without
// Node types, so node:fs is not available to it.
const APP = import.meta.glob(['../**/*.{ts,tsx}', '!../**/*.{test,spec}.{ts,tsx}', '!../generated/**'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const PLUGINS = import.meta.glob('../../../plugins/OutcomeTesting.Plugins/**/*.cs', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const PORTAL = import.meta.glob(
  [
    '../../../powerpages/outcome-testing---outcometesting/web-templates/**/*.html',
    '../../../powerpages/outcome-testing---outcometesting/content-snippets/**/*.{html,yml}',
    '../../../powerpages/outcome-testing---outcometesting/web-files/*.js',
  ],
  { query: '?raw', import: 'default', eager: true },
) as Record<string, string>;

const CODE = /\b(AD|OD|BR|FR|NFR|PP|UR|SR|DR|TR|CR)[-_ ]?\d{1,3}\b|\bNFR-[A-Z]+-\d+/;

function where(path: string, line: number, text: string): string {
  return `${path.replace(/^(\.\.\/)+/, '')}:${line}: ${text.replace(/\s+/g, ' ').trim().slice(0, 140)}`;
}

/** Whitespace in place of a comment, so line numbers still point at the right line. */
function blank(text: string): string {
  return text.replace(/[^\n]/g, ' ');
}

describe('decision and requirement codes', () => {
  it('are looked for in every place a user reads', () => {
    // Without this a mistyped glob would scan nothing and every test below would pass.
    expect(Object.keys(APP).length).toBeGreaterThan(100);
    expect(Object.keys(PLUGINS).length).toBeGreaterThan(50);
    expect(Object.keys(PORTAL).length).toBeGreaterThan(10);
  });

  it('never appear in the Code App’s on-screen text', () => {
    const found: string[] = [];
    for (const [path, text] of Object.entries(APP)) {
      const source = ts.createSourceFile(
        path,
        text,
        ts.ScriptTarget.Latest,
        true,
        path.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
      );
      const visit = (node: ts.Node) => {
        const literal =
          ts.isStringLiteral(node) ||
          ts.isNoSubstitutionTemplateLiteral(node) ||
          ts.isTemplateHead(node) ||
          ts.isTemplateMiddle(node) ||
          ts.isTemplateTail(node) ||
          ts.isJsxText(node)
            ? node.text
            : null;
        if (literal && CODE.test(literal)) {
          found.push(where(path, source.getLineAndCharacterOfPosition(node.getStart()).line + 1, literal));
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    expect(found).toEqual([]);
  });

  it('never appear in a plug-in’s message, audit text or notification', () => {
    const found: string[] = [];
    for (const [path, text] of Object.entries(PLUGINS)) {
      if (/\/(bin|obj)\//.test(path)) continue;
      text.split(/\r?\n/).forEach((line, index) => {
        const code = line.replace(/^\s*\/\/.*$/, '').replace(/\s\/\/\s.*$/, '');
        for (const literal of code.match(/@?"(?:[^"\\]|\\.)*"/g) ?? []) {
          if (CODE.test(literal)) found.push(where(path, index + 1, literal));
        }
      });
    }
    expect(found).toEqual([]);
  });

  it('never appear on a portal page', () => {
    const found: string[] = [];
    for (const [path, text] of Object.entries(PORTAL)) {
      text
        .replace(/\{%-?\s*comment\s*-?%\}[\s\S]*?\{%-?\s*endcomment\s*-?%\}/g, blank)
        .replace(/<!--[\s\S]*?-->/g, blank)
        .replace(/\/\*[\s\S]*?\*\//g, blank)
        .replace(/(^|[^:"'\\])\/\/[^\n]*/g, (match: string, before: string) => before + blank(match.slice(before.length)))
        .split('\n')
        .forEach((line, index) => {
          if (CODE.test(line)) found.push(where(path, index + 1, line));
        });
    }
    expect(found).toEqual([]);
  });
});
