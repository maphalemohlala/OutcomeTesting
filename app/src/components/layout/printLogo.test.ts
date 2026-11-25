import { describe, expect, it } from 'vitest';
import shell from './AppShell.tsx?raw';
import baseCssRaw from '../../styles/base.css?raw';
import layout from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-layout/OT-Layout.webtemplate.source.html?raw';
import portalCss from '../../../../powerpages/outcome-testing---outcometesting/web-files/outcome-testing.css?raw';

const baseCss = baseCssRaw.replace(/\r\n/g, '\n');

/** Ascot Lloyd branding on printed and saved pages (owner, 2026-11-23): top-left, print only. */
describe('the printed page carries the Ascot Lloyd logo', () => {
  const printBlock = (css: string) => css.slice(css.indexOf("@media print {"));

  it('in the app: a copy of the shell logo without ids, shown only in print', () => {
    expect(shell).toContain('<span className="shell__print-logo" aria-hidden="true" dangerouslySetInnerHTML={{ __html: printLogo }} />');
    expect(shell).toContain(String.raw`const printLogo = logo.replace(/\s(id|data-name)="[^"]*"/g, '');`);
    expect(baseCss).toMatch(/\.shell__print-logo \{\n {2}display: none;\n\}/);
    expect(printBlock(baseCss)).toMatch(/\.shell__print-logo \{\n {4}display: block;/);
  });

  it('on the portal: the same artwork at the top of every page, shown only in print', () => {
    const logo = /<div class="ot-print-logo" aria-hidden="true">(<svg[\s\S]*?<\/svg>)<\/div>/.exec(layout);
    expect(logo).not.toBeNull();
    expect(logo![1]).toContain('fill="#2b5257"');
    expect(logo![1]).not.toMatch(/\sid="/);
    expect(layout.indexOf('ot-print-logo')).toBeLessThan(layout.indexOf('<h1 class="ot-page__title">'));
    expect(portalCss).toContain('.ot-print-logo { display: none; }');
    expect(printBlock(portalCss)).toContain('.ot-print-logo { display: block; margin: 0 0 12px; }');
  });
});
