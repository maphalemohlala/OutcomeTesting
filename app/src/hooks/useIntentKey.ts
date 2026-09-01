import { useCallback, useRef } from 'react';
import { newCorrelationKey } from '../services/commands/intentKey';

export interface IntentKeys {
  /**
   * The idempotency key for one intent, identified by `token` (a record id, or a constant
   * for a create form). Returns the same key until the intent is released, so retrying a
   * failed submit replays server-side instead of writing twice.
   */
  keyFor: (token: string) => string;
  /** Ends the intent. Call after a confirmed success, and when a form is reopened. */
  release: (token: string) => void;
}

/**
 * Holds one idempotency key per in-flight user intent (NFR-REL-01). The commands look their
 * key up on al_auditevent and replay the original result, so a key must survive retries of
 * the same intent -- minting one per attempt silently disables that protection.
 *
 * Keys live in a ref rather than state: `keyFor` is called inside event handlers and must
 * return the settled key synchronously, not one render behind.
 */
export function useIntentKeys(): IntentKeys {
  const keys = useRef<Map<string, string>>(new Map());

  const keyFor = useCallback((token: string): string => {
    const existing = keys.current.get(token);
    if (existing) return existing;
    const fresh = newCorrelationKey();
    keys.current.set(token, fresh);
    return fresh;
  }, []);

  const release = useCallback((token: string): void => {
    keys.current.delete(token);
  }, []);

  return { keyFor, release };
}
