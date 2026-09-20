import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * F19, found in DEV on 2026-09-20. At phone width the whole page scrolled sideways by
 * 268px: the navigation rail collapses to a horizontal strip below 60rem, and the
 * Administration group's four long labels made it 538px wide inside a 375px viewport.
 * The header and the page content scrolled away with it.
 *
 * The rail already declared `overflow-x: auto`, which reads as "this scrolls rather than
 * pushing the page wider" and is why the defect was easy to miss in review. It does not do
 * that on its own: a flex item's automatic minimum size is its content, so the rail was
 * sized to the nav and never had anything to scroll. It needs min-width telling it that it
 * may be narrower than what is inside it.
 *
 * The worklist table was NOT the cause, although it is the widest thing on the page at
 * 960px. It sits in `.worklist__scroll`, which contains it correctly, and that is the
 * pattern the rail now follows.
 *
 * Read from the stylesheet because there is no DOM here to lay out; the live check is the
 * document's scrollWidth against its clientWidth at 390px, which is how this was found.
 */
describe('the navigation rail at phone width', () => {
  const css = readFileSync(join(__dirname, 'AppShell.css'), 'utf8');

  const narrow = css.slice(css.indexOf('@media (max-width: 60rem)'));
  const rail = narrow.slice(narrow.indexOf('.shell__rail'), narrow.indexOf('.shell__group'));

  it('collapses to a horizontal strip below 60rem', () => {
    expect(narrow).toContain('.shell__rail');
    expect(rail).toMatch(/display:\s*flex/);
  });

  it('scrolls itself rather than widening the page', () => {
    expect(rail).toMatch(/overflow-x:\s*auto/);
  });

  it('may be narrower than the navigation inside it', () => {
    // Without this the flex item is sized to its content and overflow-x never engages.
    expect(rail).toMatch(/min-width:\s*0/);
    expect(rail).toMatch(/max-width:\s*100%/);
  });
});
