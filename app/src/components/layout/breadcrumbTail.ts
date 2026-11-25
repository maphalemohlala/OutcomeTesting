import { useEffect, useSyncExternalStore } from 'react';

// The last breadcrumb a page below the menu names itself with, such as a case reference.
let tail: string | null = null;
const listeners = new Set<() => void>();
const emit = () => listeners.forEach((listener) => listener());

/** Called by a page to add itself after its menu item; cleared when the page leaves. */
export function useBreadcrumbTail(label: string | null | undefined) {
  useEffect(() => {
    const own = label || null;
    tail = own;
    emit();
    return () => {
      if (tail === own) {
        tail = null;
        emit();
      }
    };
  }, [label]);
}

export function useCurrentBreadcrumbTail(): string | null {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    () => tail,
  );
}
