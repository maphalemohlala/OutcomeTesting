/**
 * {{token}} chips inside the rich-text editor.
 *
 * Each token is wrapped in a real span while it is on screen, so it can be padded and rounded
 * like the field chips beside it. The span never leaves the editor: `cleanHtml` unwraps every
 * chip before the value is handed on, so what is saved is exactly the text the person typed.
 *
 * The chips are rebuilt only when the text and the chips disagree (a token typed, inserted,
 * pasted or broken), never on ordinary typing, so the browser's own undo keeps working for
 * the text around them. The caret is put back by its character offset after a rebuild.
 */
export const CHIP_CLASS = 'rte__chip';
const TOKEN = /\{\{[^{}]*\}\}/g;
const WHOLE_TOKEN = /^\{\{[^{}]*\}\}$/;

function chipsIn(root: ParentNode): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>(`span.${CHIP_CLASS}`));
}

function unwrap(chip: HTMLElement) {
  chip.replaceWith(...Array.from(chip.childNodes));
}

function textNodes(root: Node): Text[] {
  const nodes: Text[] = [];
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) nodes.push(node as Text);
  return nodes;
}

const chipOf = (node: Node) => node.parentElement?.closest<HTMLElement>(`span.${CHIP_CLASS}`) ?? null;

/**
 * The editor's HTML as it is saved: every chip unwrapped to its text.
 *
 * A space typed against a chip's edge is written by the browser as a non-breaking one, which
 * it would not have done without the chip there. Once the chip is gone, one that sits between
 * two words goes back to a plain space; runs of spaces (where &nbsp; keeps them visible) are
 * left alone.
 */
export function cleanHtml(area: HTMLElement): string {
  const copy = area.cloneNode(true) as HTMLElement;
  chipsIn(copy).forEach(unwrap);
  copy.normalize();
  for (const node of textNodes(copy)) {
    const plain = node.data.replace(/(\S)\u00a0(?=\S)/g, '$1 ');
    if (plain !== node.data) node.data = plain;
  }
  return copy.innerHTML;
}

/** True when every chip holds exactly one whole token and no token sits outside a chip. */
function chipsMatchText(area: HTMLElement): boolean {
  for (const chip of chipsIn(area)) {
    const only = chip.childNodes.length === 1 && chip.firstChild?.nodeType === Node.TEXT_NODE;
    if (!only || !WHOLE_TOKEN.test(chip.textContent ?? '')) return false;
  }
  return textNodes(area).every((node) => chipOf(node) !== null || !new RegExp(TOKEN.source).test(node.data));
}

function caretOffset(area: HTMLElement): number | null {
  const selection = document.getSelection();
  if (!selection || selection.rangeCount === 0) return null;
  const range = selection.getRangeAt(0);
  if (!area.contains(range.endContainer)) return null;
  const before = document.createRange();
  before.selectNodeContents(area);
  before.setEnd(range.endContainer, range.endOffset);
  return before.toString().length;
}

function placeCaret(area: HTMLElement, offset: number) {
  const range = document.createRange();
  let remaining = offset;
  for (const node of textNodes(area)) {
    const chip = chipOf(node);
    // A caret at a chip's end goes after the chip, so the next keystroke is not typed into it.
    if (remaining < node.length || (remaining === node.length && !chip)) {
      range.setStart(node, remaining);
      break;
    }
    remaining -= node.length;
    if (remaining === 0 && chip) {
      range.setStartAfter(chip);
      break;
    }
  }
  if (range.startContainer === document) {
    range.selectNodeContents(area);
    range.collapse(false);
  }
  range.collapse(true);
  const selection = document.getSelection();
  selection?.removeAllRanges();
  selection?.addRange(range);
}

/** Rebuilds the chips if they no longer match the text. Returns whether anything changed. */
export function decorateTokens(area: HTMLElement): boolean {
  if (chipsMatchText(area)) return false;
  const offset = area.contains(document.activeElement) || area === document.activeElement ? caretOffset(area) : null;

  chipsIn(area).forEach(unwrap);
  area.normalize();
  for (const node of textNodes(area)) {
    const matches = Array.from(node.data.matchAll(TOKEN));
    if (matches.length === 0) continue;
    const fragment = document.createDocumentFragment();
    let last = 0;
    for (const match of matches) {
      const at = match.index ?? 0;
      if (at > last) fragment.append(node.data.slice(last, at));
      const chip = document.createElement('span');
      chip.className = CHIP_CLASS;
      chip.spellcheck = false;
      chip.textContent = match[0];
      fragment.append(chip);
      last = at + match[0].length;
    }
    if (last < node.data.length) fragment.append(node.data.slice(last));
    node.replaceWith(fragment);
  }

  if (offset !== null) placeCaret(area, offset);
  return true;
}
