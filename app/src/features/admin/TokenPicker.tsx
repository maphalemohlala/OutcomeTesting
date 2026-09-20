import { tokenHelp } from './notificationRouting';

interface Props {
  tokens: readonly string[];
  /** Called with the token's name; the caller decides where it lands. */
  onInsert: (token: string) => void;
  /** Where a click will put it, so the button is not a guess. */
  targetLabel: string;
  disabled?: boolean;
}

/**
 * The tokens this letter can use, as buttons that put one where the caret is.
 *
 * <p>
 * This replaced a read-only row of <code>{{reference}}</code> chips. That row told an
 * administrator what they were <em>allowed</em> to type and nothing about what any of it would
 * do, so the only way to use one was to know it already — and to spell it exactly, since a
 * token the letter does not supply is refused on save.
 * </p>
 * <p>
 * Each button carries the token's name and a line saying what it fills in, because "caseLink"
 * and "caseButton" are not self-explanatory and the difference between them matters: one is an
 * address in the text, the other is a button.
 * </p>
 * <p>
 * <b>Insertion goes to whichever field was last focused</b>, which the caller tracks, and the
 * heading says which one that is. Silently putting a token in the body when the person had
 * just clicked into the subject is the kind of thing that gets noticed only after the letter
 * has gone out.
 * </p>
 */
export function TokenPicker({ tokens, onInsert, targetLabel, disabled = false }: Props) {
  if (tokens.length === 0) {
    return null;
  }

  return (
    <div className="tokens">
      <p className="tokens__intro">
        Insert into <strong>{targetLabel}</strong>:
      </p>
      <ul className="tokens__list">
        {tokens.map((token) => (
          <li key={token}>
            <button
              type="button"
              className="tokens__btn"
              disabled={disabled}
              // Keeps the caret where it is: a click that moves focus to this button first
              // loses the position the token is meant to go in.
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => onInsert(token)}
            >
              <code className="tokens__name">{`{{${token}}}`}</code>
              <span className="tokens__help">{tokenHelp(token)}</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
