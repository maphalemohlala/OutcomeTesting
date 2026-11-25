import { useEffect, useId, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { closesOnBackdrop, closesOnEscape } from './modalDismiss';
import { Icon } from '../icons/Icon';
import './Modal.css';

interface Props {
  title: string;
  onClose: () => void;
  children: ReactNode;
  /** 'wide' for a form laid out in columns, such as Edit case; 'editor' fills the screen and lets its columns scroll. */
  size?: 'wide' | 'editor';
  /** A line under the title, inside the header. */
  intro?: ReactNode;
  /** Shown beside the title, on its line: a status chip, a category. */
  meta?: ReactNode;
  /** Pinned under the body, for the dialog's own buttons. */
  footer?: ReactNode;
}

/** Accessible pop-up dialog: backdrop click and Escape close it, focus moves in. */
export function Modal({ title, onClose, children, size, intro, meta, footer }: Props) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  // Where the last press began, so a drag that ends on the backdrop is not taken for a click on it.
  const pressedOnBackdrop = useRef(false);

  // Focus the dialog once on open only. Keeping this out of the keydown effect stops
  // focus being stolen from inputs whenever the parent re-renders (e.g. on each keystroke).
  useEffect(() => {
    dialogRef.current?.focus();
  }, []);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (closesOnEscape(event)) onClose();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  return createPortal(
    <div
      className="modal__overlay"
      role="presentation"
      onPointerDown={(event) => {
        pressedOnBackdrop.current = event.target === event.currentTarget;
      }}
      onClick={(event) => {
        if (closesOnBackdrop(pressedOnBackdrop.current, event.target, event.currentTarget)) onClose();
        pressedOnBackdrop.current = false;
      }}
    >
      <div
        ref={dialogRef}
        className={size ? `modal__dialog modal__dialog--${size}` : 'modal__dialog'}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="modal__header">
          <div className="modal__heading">
            <div className="modal__title-line">
              <h2 id={titleId} className="modal__title">
                {title}
              </h2>
              {meta}
            </div>
            {intro ? <p className="modal__intro">{intro}</p> : null}
          </div>
          <button type="button" className="modal__close" aria-label="Close" onClick={onClose}>
            <Icon name="close" />
          </button>
        </div>
        <div className="modal__body">{children}</div>
        {footer ? <div className="modal__footer">{footer}</div> : null}
      </div>
    </div>,
    document.body,
  );
}
