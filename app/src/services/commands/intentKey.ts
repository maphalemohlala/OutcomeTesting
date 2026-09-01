/**
 * Idempotency keys for the server-side commands (NFR-REL-01). A key identifies one user
 * INTENT, not one attempt: the plug-ins look the key up on al_auditevent and replay the
 * original result, so a retry after a timeout that actually succeeded cannot write twice.
 * Minting a fresh key per attempt silently defeats that, so callers hold a key for the
 * life of the intent -- see useIntentKey.
 *
 * Kept free of the Power Apps SDK so it stays importable outside the data client.
 */
export function newCorrelationKey(): string {
  return crypto.randomUUID();
}
