/**
 * What a letter looks like once it is filled in, for the editor's preview.
 *
 * Sample values, not a real case: the preview shows shape and wording, and a real case would
 * need picking and reading for nothing. Tokens with no sample stay as typed, so a misspelt one
 * is visible in the preview as well as in the warning under the body.
 */
const SAMPLE_TEXT: Record<string, string> = {
  reference: 'OT-2026-0417',
  check: 'AQS check',
  client: 'Test Client Alpha',
  adviser: 'Jordan Adviser',
  recipient: 'Alex Manager',
  caseLink: 'https://portal.example/cases/OT-2026-0417',
  caseButton: 'Open the case',
  dueText: ' It is due by 3 March 2026.',
  grading: 'Pass with issues',
  finalOutcome: 'Pass',
  notes: ' Notes: evidence filed on the case.',
  notesPanel: 'Notes: evidence filed on the case.',
  completedCheck: '',
};

// Our own markup, never the letter's: safe to put in as HTML.
const SAMPLE_HTML: Record<string, string> = {
  caseLink: '<a>link to the case in the portal</a>',
  caseButton: '<span class="preview__button">Open the case</span>',
  notesPanel: '<span class="preview__panel"><strong>Notes</strong> Evidence filed on the case.</span>',
};

const TOKEN = /\{\{\s*([^}]*?)\s*\}\}/g;
/** The same pattern for split(), whose capture group puts each token's name at the odd indexes. */
const TOKEN_SPLIT = /\{\{\s*([^}]*?)\s*\}\}/;
const TAG = /<(\/?)([a-zA-Z][a-zA-Z0-9]*)\b[^>]*>/g;

/** Tags the server's HtmlSanitiser keeps; any other tag is dropped and its text kept. */
const ALLOWED = new Set(['p', 'br', 'strong', 'b', 'em', 'i', 'u', 'ul', 'ol', 'li', 'a']);

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/** The subject as plain text, tokens filled in. React escapes it when it renders. */
export function previewSubject(subject: string): string {
  return subject.replace(TOKEN, (whole, name: string) => SAMPLE_TEXT[name] ?? whole);
}

// Text between tags is already entity-encoded (it came from innerHTML), so & is left alone;
// anything that could open a tag is not.
function fillText(text: string): string {
  return text
    .split(TOKEN_SPLIT)
    .map((piece, i) => {
      if (i % 2 === 0) return piece.replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
      return SAMPLE_HTML[piece] ?? escapeHtml(SAMPLE_TEXT[piece] ?? `{{${piece}}}`);
    })
    .join('');
}

/**
 * The body as HTML, rebuilt from the allow-list with every attribute dropped, tokens filled.
 * The body is the administrator's own or the server's sanitised copy, but it is rebuilt here
 * anyway: this is the one place the app puts letter markup into its own page. Only tags this
 * function writes itself reach the output; everything else is escaped as text.
 */
export function previewBody(body: string): string {
  const html = body.replace(/<!--[\s\S]*?-->/g, '').replace(/<(script|style)\b[\s\S]*?<\/\1\s*>/gi, '');
  let out = '';
  let last = 0;
  for (const match of html.matchAll(TAG)) {
    out += fillText(html.slice(last, match.index));
    last = (match.index ?? 0) + match[0].length;
    const tag = match[2].toLowerCase();
    if (!ALLOWED.has(tag)) continue;
    const closing = match[1] === '/';
    if (tag === 'br') out += closing ? '' : '<br>';
    // Links keep their text but lose their address: the preview is not the place to follow one.
    else out += closing ? `</${tag}>` : `<${tag}>`;
  }
  return out + fillText(html.slice(last));
}

/** The tokens a subject and body use, for the picker's "In use" marks. */
export function tokensInUse(subject: string, body: string): Set<string> {
  return new Set(Array.from(`${subject} ${body}`.matchAll(TOKEN), (m) => m[1]));
}

/** Pieces of a plain string with the tokens marked, for the subject's highlighted copy. */
export function splitTokens(text: string): { text: string; token: boolean }[] {
  const parts: { text: string; token: boolean }[] = [];
  let last = 0;
  for (const match of text.matchAll(TOKEN)) {
    const at = match.index ?? 0;
    if (at > last) parts.push({ text: text.slice(last, at), token: false });
    parts.push({ text: match[0], token: true });
    last = at + match[0].length;
  }
  if (last < text.length) parts.push({ text: text.slice(last), token: false });
  return parts;
}
