import { Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';

interface State {
  failed: boolean;
}

/** Errors must not expose stack traces or record content to the user (NFR-OBS-01). */
export class ErrorBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { failed: false };

  static getDerivedStateFromError(): State {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('Unhandled UI error', error.message, info.componentStack);
  }

  render() {
    if (this.state.failed) {
      return (
        <section style={{ padding: 'var(--space-5)' }}>
          <h1>This screen could not be displayed</h1>
          <p>
            No case data has been changed. Reload the page to try again. If it keeps happening,
            report it to the Outcome Testing support contact.
          </p>
        </section>
      );
    }

    return this.props.children;
  }
}
