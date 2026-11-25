import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { RemedialText } from './RemedialText';

describe('a checker\'s remedial action in the app', () => {
  it('draws formatted words as their markup', () => {
    expect(renderToStaticMarkup(<RemedialText text="<p><strong>Bold</strong> and <u>under</u></p><ul><li>One</li></ul>" />)).toBe(
      '<span class="value value--rich"><p><strong>Bold</strong> and <u>under</u></p><ul><li>One</li></ul></span>',
    );
  });

  it('escapes words written before the editor', () => {
    expect(renderToStaticMarkup(<RemedialText text="Fix <this> & that" />)).toBe('Fix &lt;this&gt; &amp; that');
  });

  it('shows a dash when there is nothing', () => {
    expect(renderToStaticMarkup(<RemedialText text={null} />)).toBe('—');
  });
});
