import { describe, expect, it } from 'vitest';
import { avatarTone } from './avatarTone';

describe('avatarTone', () => {
  it('gives one name the same tint every time, whatever its case or spacing', () => {
    expect(avatarTone('Clare Hook')).toBe(avatarTone(' clare hook '));
  });

  it('stays within the five tints', () => {
    for (const name of ['Adam Strumidlo', 'Zoe Ramwell', 'Service Account', 'x', '']) {
      expect(avatarTone(name)).toBeGreaterThanOrEqual(0);
      expect(avatarTone(name)).toBeLessThan(5);
    }
  });
});
