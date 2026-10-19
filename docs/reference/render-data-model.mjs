// Renders the mermaid diagram in data-model.md to data-model.png, using the app's Playwright.
// Mermaid itself is loaded from jsDelivr, so this needs network access.
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const here = (name) => fileURLToPath(new URL(name, import.meta.url));
const { chromium } = createRequire(here('../../app/package.json'))('playwright');

const markdown = readFileSync(here('data-model.md'), 'utf8');
const source = markdown.match(/```mermaid\r?\n([\s\S]*?)```/)?.[1];
if (!source) throw new Error('No mermaid block in data-model.md');

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1600, height: 1200 }, deviceScaleFactor: 2 });
// The ELK layout routes a 29-table diagram far more compactly than the default dagre.
await page.setContent(`<body style="margin:0;background:#fff">
  <div id="out" style="padding:24px;display:inline-block;background:#fff"></div>
  <script type="module">
    import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@11.6.0/dist/mermaid.esm.min.mjs';
    import elk from 'https://cdn.jsdelivr.net/npm/@mermaid-js/layout-elk@0.1.7/dist/mermaid-layout-elk.esm.min.mjs';
    mermaid.registerLayoutLoaders(elk);
    window.mermaid = mermaid;
  </script>
</body>`);
await page.waitForFunction(() => window.mermaid, null, { timeout: 60000 });
await page.evaluate(async (text) => {
  window.mermaid.initialize({
    startOnLoad: false,
    layout: 'elk',
    elk: { mergeEdges: false, nodePlacementStrategy: 'BRANDES_KOEPF' },
    theme: 'base',
    er: { useMaxWidth: false },
    themeVariables: {
      fontFamily: 'Segoe UI, Arial, sans-serif',
      primaryColor: '#eef3fb',
      primaryBorderColor: '#3b5b8c',
      lineColor: '#1d4ed8',
      tertiaryColor: '#ffffff',
    },
    // Relationships in strong blue so every lookup stands out from the table borders.
    themeCSS: '.relationshipLine { stroke: #1d4ed8 !important; stroke-width: 1.6px !important; }'
      + ' .relationshipLabel { font-weight: 600; fill: #1e3a8a; }',
  });
  const { svg } = await window.mermaid.render('erd', text);
  document.getElementById('out').innerHTML = svg;
}, source);

await page.locator('#out').screenshot({ path: here('data-model.png') });
await browser.close();
console.log('Wrote', here('data-model.png'));
