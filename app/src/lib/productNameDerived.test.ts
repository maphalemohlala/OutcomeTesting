import ts from 'typescript';
import { describe, expect, it } from 'vitest';

/**
 * The product name is never written out (owner, 2026-10-01: PROD is called OTIS, DEV and TEST
 * keep "Outcome Testing"; spec docs/superpowers/specs/2026-10-01-otis-product-name-design.md).
 *
 * Every label and every key that carries it is derived from the one per-environment value:
 * ProductName in the plug-ins, app/product/productName.ts in the Code App, and the
 * OT/ProductName site setting in the portal. A literal "Outcome Testing" anywhere a user reads
 * would show the wrong name in PROD; a literal manager role would lock PROD's managers out.
 * The only literals allowed are the default itself, in the three places that define it.
 */

// Imported as text, as the other source-scanning tests do.
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
    '../../../powerpages/outcome-testing---outcometesting/content-snippets/**/*.html',
    '../../../powerpages/outcome-testing---outcometesting/web-files/*.js',
  ],
  { query: '?raw', import: 'default', eager: true },
) as Record<string, string>;

const NAME = /Outcome Testing/;

/** The places that define the default, and the legacy app-role label kept deliberately. */
const ALLOWED_APP = /app\/product\/productName\.ts$|types\/permissions\.ts$|types\/domain\.ts$/;
const ALLOWED_PLUGIN = /ProductName\.cs$|PermissionHelpers\.cs$/;
const PORTAL_DEFAULT = "settings['OT/ProductName'] | default: 'Outcome Testing'";

function where(path: string, line: number, text: string): string {
  return `${path.replace(/^(\.\.\/)+/, '')}:${line}: ${text.replace(/\s+/g, ' ').trim().slice(0, 140)}`;
}

function blank(text: string): string {
  return text.replace(/[^\n]/g, ' ');
}

describe('the product name', () => {
  it('is looked for in every place a user reads', () => {
    expect(Object.keys(APP).length).toBeGreaterThan(100);
    expect(Object.keys(PLUGINS).length).toBeGreaterThan(50);
    expect(Object.keys(PORTAL).length).toBeGreaterThan(10);
  });

  it('is never written out in the Code App', () => {
    const found: string[] = [];
    for (const [path, text] of Object.entries(APP)) {
      if (ALLOWED_APP.test(path)) continue;
      const source = ts.createSourceFile(path, text, ts.ScriptTarget.Latest, true,
        path.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
      const visit = (node: ts.Node) => {
        const literal =
          ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node) || ts.isTemplateHead(node) ||
          ts.isTemplateMiddle(node) || ts.isTemplateTail(node) || ts.isJsxText(node)
            ? node.text
            : null;
        if (literal && NAME.test(literal)) {
          found.push(where(path, source.getLineAndCharacterOfPosition(node.getStart()).line + 1, literal));
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    expect(found).toEqual([]);
  });

  it('is never written out in a plug-in string', () => {
    const found: string[] = [];
    for (const [path, text] of Object.entries(PLUGINS)) {
      if (/\/(bin|obj)\//.test(path) || ALLOWED_PLUGIN.test(path)) continue;
      text.split(/\r?\n/).forEach((line, index) => {
        const code = line.replace(/^\s*\/\/.*$/, '').replace(/\s\/\/\s.*$/, '');
        for (const literal of code.match(/@?"(?:[^"\\]|\\.)*"/g) ?? []) {
          if (NAME.test(literal)) found.push(where(path, index + 1, literal));
        }
      });
    }
    expect(found).toEqual([]);
  });

  it('is never written out on a portal page, except as the setting’s default', () => {
    const found: string[] = [];
    for (const [path, text] of Object.entries(PORTAL)) {
      text
        .replace(/\{%-?\s*comment\s*-?%\}[\s\S]*?\{%-?\s*endcomment\s*-?%\}/g, blank)
        .replace(/<!--[\s\S]*?-->/g, blank)
        .replace(/\/\*[\s\S]*?\*\//g, blank)
        .replace(/(^|[^:"'\\])\/\/[^\n]*/g, (match: string, before: string) => before + blank(match.slice(before.length)))
        .split('\n')
        .forEach((line, index) => {
          if (NAME.test(line.split(PORTAL_DEFAULT).join(''))) found.push(where(path, index + 1, line));
        });
    }
    expect(found).toEqual([]);
  });

  it('decides the manager role on every portal page that checks it', () => {
    for (const [path, text] of Object.entries(PORTAL)) {
      if (!text.includes('ot_manager_role')) continue;
      expect(text, path).toContain("{% assign ot_manager_role = 'AL Portal - ' | append: ot_product | append: ' Manager' %}");
      expect(text, path).toContain(PORTAL_DEFAULT);
    }
    const checking = Object.values(PORTAL).filter((text) => text.includes('user.roles contains ot_manager_role'));
    expect(checking.length).toBe(3);
  });
});
