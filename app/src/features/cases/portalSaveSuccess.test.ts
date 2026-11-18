import { describe, expect, it } from 'vitest';
import layoutRaw from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-layout/OT-Layout.webtemplate.source.html?raw';
import reviewRaw from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';
import remediationRaw from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import reviewListRaw from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-list/OT-Review-List.webtemplate.source.html?raw';

/**
 * Audit 2026-11-17. A lapsed portal session can answer a save with the sign-in page and a
 * 200, which the pages read as success and said "Saved" with nothing written. The portal
 * scripts are run here, not just read, against fake requests.
 */

const layout = layoutRaw.replace(/\r\n/g, '\n');
const review = reviewRaw.replace(/\r\n/g, '\n');

/** The script block holding `marker`, with Liquid comments and output tags taken out. */
function scriptAround(source: string, marker: string): string {
  const at = source.indexOf(marker);
  if (at === -1) return '';
  const start = source.lastIndexOf('<script>', at) + '<script>'.length;
  return source
    .slice(start, source.indexOf('</script>', at))
    .replace(/\{%-?\s*comment\s*-?%\}[\s\S]*?\{%-?\s*endcomment\s*-?%\}/g, '')
    .replace(/\{\{[^}]*\}\}/g, 'me');
}

interface FakeXhr {
  status: number;
  readyState: number;
  responseURL: string;
  responseText: string;
  type: string | null;
  sent: string | null;
  getResponseHeader: (name: string) => string | null;
  onreadystatechange: (() => void) | null;
}

function xhr(status: number, url: string, type: string | null, body = ''): FakeXhr {
  return {
    status,
    readyState: 4,
    responseURL: url,
    responseText: body,
    type,
    sent: null,
    getResponseHeader: (name) => (name === 'Content-Type' ? type : null),
    onreadystatechange: null,
  };
}

const SIGN_IN = xhr(200, 'https://site.example/SignIn?returnUrl=%2f_api%2fcontacts', 'text/html', '<html>Sign in</html>');
const HTML_AT_API = xhr(200, 'https://site.example/_api/contacts(1)', 'text/html', '<html>Sign in</html>');
const JSON_REPLY = xhr(200, 'https://site.example/_api/al_reviewinstances(1)', 'application/json; odata.metadata=minimal', '{}');
const NO_CONTENT = xhr(204, 'https://site.example/_api/contacts(1)', null);

type OtApi = { ok: (x: unknown) => boolean; error: (action: string, x: unknown) => string };

function loadOtApi(win: Record<string, unknown>): OtApi {
  new Function('window', 'location', 'navigator', scriptAround(layout, 'window.otApi = {'))(
    win,
    { href: 'https://site.example/review/' },
    { onLine: true },
  );
  return win.otApi as OtApi;
}

describe("OT Layout's save check", () => {
  const api = loadOtApi({});

  it('does not count a sign-in page as a save, even one answered with 200', () => {
    expect(api.ok(SIGN_IN)).toBe(false);
    expect(api.ok(HTML_AT_API)).toBe(false);
    expect(api.error('Saving', SIGN_IN)).toBe(
      'Saving failed. Your session has ended - reload the page and sign in again.',
    );
  });

  it('counts a Web API reply: JSON, or no content', () => {
    expect(api.ok(JSON_REPLY)).toBe(true);
    expect(api.ok(NO_CONTENT)).toBe(true);
  });

  it('shows a server sentence with quotes in it whole, and does not name the action twice', () => {
    const refusal = xhr(400, 'https://site.example/_api/contacts(1)', 'application/json', JSON.stringify({
      error: { message: 'PRECONDITION: Choose "Adviser" or "Planner" first.' },
    }));
    expect(api.error('Saving', refusal)).toBe('Saving failed. Choose "Adviser" or "Planner" first.');
    const named = xhr(500, 'https://site.example/_api/contacts(1)', 'application/json', JSON.stringify({
      error: { message: 'UNEXPECTED: Recording who carries the fail could not be completed because it took too long.' },
    }));
    expect(api.error('Recording who carries the fail', named)).toBe(
      'Recording who carries the fail could not be completed because it took too long.',
    );
  });
});

describe('every portal write', () => {
  it.each([
    ['OT Review Detail', reviewRaw],
    ['OT Remediation', remediationRaw],
    ['OT Review List', reviewListRaw],
  ])('in %s decides success with the shared check, not a status of its own', (_name, raw) => {
    const code = raw.replace(/\r\n/g, '\n').replace(/\{%-?\s*comment\s*-?%\}[\s\S]*?\{%-?\s*endcomment\s*-?%\}/g, '');
    const writes = [...code.matchAll(/\.open\(\s*(?:'(?:PATCH|POST|PUT|DELETE)'|method)\s*,/g)];
    expect(writes.length).toBeGreaterThan(0);
    for (const write of writes) {
      const handler = code.slice(write.index, code.indexOf('.send(', write.index));
      expect(handler).toContain('window.otApi.ok(');
      expect(handler).not.toMatch(/\.status\s*(>=|<|===)\s*[0-9]/);
    }
  });
});

describe('the submit and who carries the fail', () => {
  /** Runs the accountability script against a fake page with one non-pass answer ticked. */
  function accountability() {
    const sent: FakeXhr[] = [];
    const status = { textContent: '', className: '' };
    const root = {
      hidden: false,
      getAttribute: (name: string) => (name === 'data-review-id' ? 'r1' : null),
      querySelector: (sel: string) => (sel === '[data-ot-acc-status]' ? status : null),
      querySelectorAll: () => [],
    };
    const document = {
      querySelector: (sel: string) => (sel === '[data-ot-accountability]' ? root : null),
      querySelectorAll: (sel: string) =>
        sel === '[data-ot-questionnaire] input:checked' ? [{ value: '120910301' }] : [],
    };
    const win: Record<string, unknown> = {
      shell: { getTokenDeferred: () => ({ done: (cb: (t: string) => void) => { cb('token'); return { fail: () => {} }; } }) },
      sessionStorage: { getItem: () => null, setItem: () => {}, removeItem: () => {} },
      setTimeout: () => 1,
      clearTimeout: () => {},
    };
    loadOtApi(win);
    function FakeRequest(this: FakeXhr) {
      Object.assign(this, { status: 0, readyState: 0, responseURL: '', responseText: '', type: null });
      sent.push(this);
    }
    FakeRequest.prototype.open = function () {};
    FakeRequest.prototype.setRequestHeader = function () {};
    FakeRequest.prototype.getResponseHeader = function (this: FakeXhr, name: string) {
      return name === 'Content-Type' ? this.type : null;
    };
    FakeRequest.prototype.send = function (this: FakeXhr, body: string) { this.sent = body; };

    new Function('window', 'document', 'XMLHttpRequest', scriptAround(review, "var root = document.querySelector('[data-ot-accountability]');"))(
      win, document, FakeRequest,
    );
    const answer = (reply: FakeXhr) => {
      const request = sent[sent.length - 1];
      Object.assign(request, { status: reply.status, readyState: 4, responseURL: reply.responseURL, responseText: reply.responseText, type: reply.type });
      request.onreadystatechange?.();
    };
    return { flush: (win.otAccountability as { flush: (done: (ok: boolean) => void) => void }).flush, answer, sent, status };
  }

  it('reports the save as failed when the sign-in page answers it', () => {
    const page = accountability();
    const results: boolean[] = [];
    page.flush((ok) => results.push(ok));
    expect(page.sent).toHaveLength(1);
    expect(results).toEqual([]);
    page.answer(SIGN_IN);
    expect(results).toEqual([false]);
    expect(page.status.textContent).toContain('Your session has ended');
    expect(page.status.textContent).not.toContain('Saved');
  });

  it('reports it saved only once the Web API has answered', () => {
    const page = accountability();
    const results: boolean[] = [];
    page.flush((ok) => results.push(ok));
    page.answer(NO_CONTENT);
    expect(results).toEqual([true]);
    expect(page.status.textContent).toMatch(/^Saved \d\d:\d\d$/);
  });

  it('holds the submit until that save settles, and stops it with a message when it fails', () => {
    const click = review.slice(review.indexOf("window.otAccountability.flush(function (ok) {"));
    expect(click).toMatch(
      /^window\.otAccountability\.flush\(function \(ok\) \{\s*if \(!ok\) \{\s*show\('Your choice of who carries this fail could not be saved, so the review was'[\s\S]*?setBusy\(false\);\s*return;\s*\}\s*afterAccountability\(\);\s*\}\);\s*return;/,
    );
  });
});
