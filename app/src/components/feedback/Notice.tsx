import type { ReactNode } from 'react';
import './Notice.css';

export type NoticeTone = 'success' | 'error' | 'info';

/** Inline feedback banner. Errors are announced assertively; other tones politely. */
export function Notice({ tone, children }: { tone: NoticeTone; children: ReactNode }) {
  return (
    <p className="notice" data-tone={tone} role={tone === 'error' ? 'alert' : 'status'}>
      {children}
    </p>
  );
}
