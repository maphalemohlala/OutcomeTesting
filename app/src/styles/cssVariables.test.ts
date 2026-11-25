import { describe, expect, it } from 'vitest';

/*
 * A var() with no definition and no fallback draws nothing. Found 2026-11-24: the recheck
 * submit button used --colour-brand, --colour-on-brand and --radius-sm, none of which exist,
 * so it rendered as bare text; its fields used --colour-background, defined only for print.
 */
const sheets = import.meta.glob('../**/*.css', { query: '?raw', import: 'default', eager: true }) as Record<string, string>;

// Set from component code as an inline style, not in a stylesheet.
const SET_INLINE = new Set(['--share']);

/** The sheet without its @media print blocks: a print-only definition draws nothing on screen. */
function screenOnly(css: string): string {
  let out = '';
  let i = 0;
  for (;;) {
    const start = css.indexOf('@media print', i);
    if (start < 0) return out + css.slice(i);
    out += css.slice(i, start);
    let depth = 0;
    let j = css.indexOf('{', start);
    for (; j < css.length; j++) {
      if (css[j] === '{') depth++;
      else if (css[j] === '}' && --depth === 0) break;
    }
    i = j + 1;
  }
}

describe('CSS custom properties', () => {
  it('reads the stylesheets', () => {
    expect(Object.keys(sheets).length).toBeGreaterThan(25);
  });

  it('drops print-only blocks before looking for definitions', () => {
    expect(screenOnly('a{b:c}@media print{:root{--x:1}.y{b:c}}.z{--w:2}')).toBe('a{b:c}.z{--w:2}');
  });

  it('are all defined for the screen before they are used without a fallback', () => {
    const screen = Object.fromEntries(Object.entries(sheets).map(([p, css]) => [p, screenOnly(css)]));
    const defined = new Set(
      [...Object.values(screen).join('\n').matchAll(/(--[a-z0-9-]+)\s*:/gi)].map((m) => m[1]),
    );
    const missing = new Set<string>();
    for (const [path, css] of Object.entries(screen)) {
      for (const m of css.matchAll(/var\((--[a-z0-9-]+)\s*\)/gi)) {
        if (!defined.has(m[1]) && !SET_INLINE.has(m[1])) missing.add(`${m[1]} in ${path}`);
      }
    }
    expect([...missing]).toEqual([]);
  });
});
