import { useSyncExternalStore } from 'react';
import { DEFAULT_PRODUCT } from './productName';

/**
 * This environment's product name, shared app-wide. Filled once by loadProductName (which
 * the permission provider awaits at sign-in); until then, and wherever it cannot be read,
 * it is DEFAULT_PRODUCT - what DEV and TEST are called anyway.
 *
 * Kept apart from the loader so a screen can show the name without importing the Dataverse
 * client.
 */
let current = DEFAULT_PRODUCT;
const listeners = new Set<() => void>();

export function publishProductName(name: string): void {
  current = name;
  if (typeof document !== 'undefined') document.title = name;
  listeners.forEach((listener) => listener());
}

/** The current name, for code outside React. */
export function productName(): string {
  return current;
}

/** The current name, re-rendering when it arrives. */
export function useProductName(): string {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    () => current,
    () => current,
  );
}

/** Test seam. */
export function resetProductNameStoreForTests(): void {
  current = DEFAULT_PRODUCT;
  listeners.clear();
}
