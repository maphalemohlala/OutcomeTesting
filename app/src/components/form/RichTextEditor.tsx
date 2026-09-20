import { useEffect, useRef } from 'react';
import './RichTextEditor.css';

interface Props {
  id: string;
  value: string;
  onChange: (html: string) => void;
  /** Read out to a screen reader in place of a visible label. */
  label: string;
  disabled?: boolean;
}

/**
 * A small rich-text editor: contenteditable with a toolbar, producing exactly the markup the
 * server's allow-list keeps.
 *
 * <p>
 * <b>The same six commands as the portal's editor</b> (`OT Review Detail`, item 7), and
 * deliberately so — the two write into the same shaped column and an administrator moving
 * between them should not find one can do things the other cannot. Bold, italic, underline,
 * the two lists, and clear formatting. No link button: the portal has none either, and the
 * one link these letters need is built by the assembly as `{{caseButton}}`.
 * </p>
 * <p>
 * <b>Every command here lands inside `HtmlSanitiser`'s allow-list</b> — p, br, strong, em, u,
 * ul, ol, li, a. That is not a coincidence to be relied on quietly: the toolbar was chosen to
 * fit the list, so anything this produces survives a clean unchanged, and anything pasted in
 * that would not is the server's business rather than this component's.
 * </p>
 * <p>
 * `execCommand` is deprecated and has no replacement with this reach. It is what the portal
 * already uses, it works in every browser this app supports, and the alternative is a
 * dependency for six buttons.
 * </p>
 */
export function RichTextEditor({ id, value, onChange, label, disabled = false }: Props) {
  const areaRef = useRef<HTMLDivElement>(null);

  // Written only when the incoming value is not already what the element holds. Assigning
  // innerHTML on every render puts the caret back to the start on each keystroke, which is
  // the classic way a contenteditable becomes unusable for anything longer than a word.
  useEffect(() => {
    const area = areaRef.current;
    if (area && area.innerHTML !== value) {
      area.innerHTML = value;
    }
  }, [value]);

  const run = (command: string) => {
    const area = areaRef.current;
    if (!area || disabled) return;

    // The selection has to be inside the editable area for the command to apply to it, and a
    // toolbar click moves focus to the button first.
    area.focus();
    document.execCommand(command, false);
    onChange(area.innerHTML);
  };

  return (
    <div className="rte" data-disabled={disabled ? 'true' : undefined}>
      <div className="rte__bar" role="toolbar" aria-label="Formatting">
        <button
          type="button"
          className="rte__btn"
          title="Bold"
          aria-label="Bold"
          disabled={disabled}
          // The toolbar must not take focus away from the text, or the selection the command
          // is meant to act on is gone by the time it runs.
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => run('bold')}
        >
          <strong>B</strong>
        </button>
        <button
          type="button"
          className="rte__btn"
          title="Italic"
          aria-label="Italic"
          disabled={disabled}
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => run('italic')}
        >
          <em>I</em>
        </button>
        <button
          type="button"
          className="rte__btn"
          title="Underline"
          aria-label="Underline"
          disabled={disabled}
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => run('underline')}
        >
          <u>U</u>
        </button>

        <span className="rte__sep" aria-hidden="true" />

        <button
          type="button"
          className="rte__btn"
          title="Bulleted list"
          aria-label="Bulleted list"
          disabled={disabled}
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => run('insertUnorderedList')}
        >
          • List
        </button>
        <button
          type="button"
          className="rte__btn"
          title="Numbered list"
          aria-label="Numbered list"
          disabled={disabled}
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => run('insertOrderedList')}
        >
          1. List
        </button>

        <span className="rte__sep" aria-hidden="true" />

        <button
          type="button"
          className="rte__btn"
          title="Clear formatting"
          aria-label="Clear formatting"
          disabled={disabled}
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => run('removeFormat')}
        >
          Clear
        </button>
      </div>

      <div
        id={id}
        ref={areaRef}
        className="rte__area"
        contentEditable={!disabled}
        suppressContentEditableWarning
        role="textbox"
        aria-multiline="true"
        aria-label={label}
        onInput={(e) => onChange((e.target as HTMLDivElement).innerHTML)}
        // Pasted content comes in as whatever the source was — a whole styled document, in the
        // worst case. Taking the plain text keeps what was meant and drops the rest, and the
        // person can then format it with the toolbar.
        onPaste={(e) => {
          e.preventDefault();
          const text = e.clipboardData.getData('text/plain');
          document.execCommand('insertText', false, text);
          const area = areaRef.current;
          if (area) onChange(area.innerHTML);
        }}
      />
    </div>
  );
}
