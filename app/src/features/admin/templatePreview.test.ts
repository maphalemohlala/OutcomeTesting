import { describe, expect, it } from 'vitest';
import { previewBody, previewSubject, splitTokens, tokensInUse } from './templatePreview';

describe('letter preview', () => {
  it('fills the subject with sample values and leaves unknown tokens as typed', () => {
    expect(previewSubject('Case {{reference}}: {{check}} allocated to you - {{client}} {{nope}}')).toBe(
      'Case OT-2026-0417: AQS check allocated to you - Test Client Alpha {{nope}}',
    );
  });

  it('fills the body, keeping the allowed formatting and turning caseLink into a link', () => {
    expect(previewBody('<p>The <strong>{{check}}</strong> on {{reference}}: {{caseLink}}</p>')).toBe(
      '<p>The <strong>AQS check</strong> on OT-2026-0417: <a>link to the case in the portal</a></p>',
    );
  });

  it('drops scripts, attributes and tags outside the allow-list', () => {
    const html = previewBody(
      '<p onclick="x()">Hi<script>alert(1)</script><img src=x onerror=alert(1)><a href="javascript:alert(1)">go</a></p><!-- c --><div>d</div>',
    );
    expect(html).toBe('<p>Hi<a>go</a></p>d');
  });

  it('escapes text that only looks like markup', () => {
    expect(previewBody('1 &lt; 2 and 3 > 2 "q"')).toBe('1 &lt; 2 and 3 &gt; 2 &quot;q&quot;');
  });

  it('lists the tokens in use and splits a subject around them', () => {
    expect([...tokensInUse('{{a}} x', '<p>{{ b }}</p>{{a}}')]).toEqual(['a', 'b']);
    expect(splitTokens('Case {{reference}}!')).toEqual([
      { text: 'Case ', token: false },
      { text: '{{reference}}', token: true },
      { text: '!', token: false },
    ]);
  });
});
