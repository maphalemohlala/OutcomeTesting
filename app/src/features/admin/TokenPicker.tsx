import { useId } from 'react';
import { Icon } from '../../components/icons/Icon';
import { TOKEN_COMPLETED_CHECK, tokenHelp } from './notificationRouting';

interface Props {
  tokens: readonly string[];
  /** Called with the token's name; the caller decides where it lands. */
  onInsert: (token: string) => void;
  /** Where a click will put it, so the button is not a guess. */
  targetLabel: string;
  /** Tokens the letter already uses, marked "In use". */
  used?: ReadonlySet<string>;
  /** Given to the list, so a control elsewhere can move focus into it. */
  id?: string;
  disabled?: boolean;
}

/**
 * The tokens this letter can use, each with a line saying what it fills in and a button that
 * puts it where the caret is.
 *
 * <p>
 * This replaced a read-only row of <code>{{reference}}</code> chips. That row told an
 * administrator what they were <em>allowed</em> to type and nothing about what any of it would
 * do, so the only way to use one was to know it already, and to spell it exactly, since a
 * token the letter does not supply is refused on save.
 * </p>
 * <p>
 * <b>Insertion goes to whichever field was last focused</b>, which the caller tracks, and the
 * heading says which one that is. Silently putting a token in the body when the person had
 * just clicked into the subject is the kind of thing that gets noticed only after the letter
 * has gone out.
 * </p>
 */
export function TokenPicker({ tokens, onInsert, targetLabel, used, id, disabled = false }: Props) {
  const headingId = useId();
  if (tokens.length === 0) {
    return null;
  }

  const target = targetLabel.toLowerCase();
  return (
    <section className="tokens" aria-labelledby={headingId}>
      <div className="tokens__head">
        <h3 id={headingId} className="tokens__heading">
          Fields you can insert
        </h3>
        <span className="tokens__where">Adds at the cursor in the {target}</span>
      </div>
      <ul className="tokens__list" id={id}>
        {tokens.map((token) => (
          <li key={token} className="tokens__item">
            <div className="tokens__text">
              <span className="tokens__top">
                <code className="tokens__chip">
                  {token === TOKEN_COMPLETED_CHECK ? <Icon name="paperclip" /> : null}
                  {`{{${token}}}`}
                </code>
                {used?.has(token) ? <span className="tokens__used">In use</span> : null}
              </span>
              <span className="tokens__help">{tokenHelp(token)}</span>
            </div>
            <button
              type="button"
              className="tokens__add"
              aria-label={`Insert {{${token}}} in the ${target}`}
              title={`Insert in the ${target}`}
              disabled={disabled}
              // Keeps the caret where it is: a click that moves focus to this button first
              // loses the position the token is meant to go in.
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => onInsert(token)}
            >
              <Icon name="plus" />
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}
