import { describe, expect, it } from 'vitest';
import { closesOnBackdrop, closesOnEscape } from './modalDismiss';

describe('closing a pop-up', () => {
  it('ignores an Escape a control inside it already handled', () => {
    expect(closesOnEscape({ key: 'Escape', defaultPrevented: true })).toBe(false);
    expect(closesOnEscape({ key: 'Escape', defaultPrevented: false })).toBe(true);
    expect(closesOnEscape({ key: 'Enter', defaultPrevented: false })).toBe(false);
  });

  it('does not close on a text-selection drag released over the backdrop', () => {
    const backdrop = {} as EventTarget;
    // Pressed inside the dialog, released on the backdrop: the click lands on the backdrop.
    expect(closesOnBackdrop(false, backdrop, backdrop)).toBe(false);
  });

  it('closes when both the press and the click were on the backdrop', () => {
    const backdrop = {} as EventTarget;
    expect(closesOnBackdrop(true, backdrop, backdrop)).toBe(true);
    expect(closesOnBackdrop(true, {} as EventTarget, backdrop)).toBe(false);
  });
});
