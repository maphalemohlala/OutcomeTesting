import { useEffect, useId, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import './Modal.css';

interface Props {
  title: string;
  onClose: () => void;
  children: ReactNode;
}

/** Accessible pop-up dialog: backdrop click and Escape close it, focus moves in. */
export function Modal({ title, onClose, children }: Props) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const titleId = useId();

  // Focus the dialog once on open only. Keeping this out of the keydown effect stops
  // focus being stolen from inputs whenever the parent re-renders (e.g. on each keystroke).
  useEffect(() => {
    dialogRef.current?.focus();
  }, []);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  return createPortal(
    <div className="modal__overlay" role="presentation" onClick={onClose}>
      <div
        ref={dialogRef}
        className="modal__dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="modal__header">
          <h2 id={titleId} className="modal__title">
            {title}
          </h2>
          <button type="button" className="modal__close" aria-label="Close" onClick={onClose}>
            &times;
          </button>
        </div>
        <div className="modal__body">{children}</div>
      </div>
    </div>,
    document.body,
  );
}
