/**
 * When a pop-up closes itself. Kept apart from Modal so the two rules can be tested without a
 * DOM, and so each is written once.
 */

/** Escape closes the dialog, unless a control inside it already used the key. */
export function closesOnEscape(event: { key: string; defaultPrevented: boolean }): boolean {
  return event.key === 'Escape' && !event.defaultPrevented;
}

/**
 * A click on the backdrop closes the dialog only when the press began there too. Selecting
 * text in a field and letting go over the backdrop ends in a click on the backdrop, and that
 * must not throw away what was being typed.
 */
export function closesOnBackdrop(
  pressedOnBackdrop: boolean,
  clickTarget: EventTarget | null,
  backdrop: EventTarget | null,
): boolean {
  return pressedOnBackdrop && clickTarget === backdrop;
}
