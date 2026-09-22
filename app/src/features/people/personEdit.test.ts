import { describe, expect, it } from 'vitest';
import { staffCodeToSend } from './personEdit';

/**
 * The client half of al_UpdateUser's absent-vs-empty contract.
 *
 * The server tells "leave the code alone" from "clear the code" by whether the StaffCode
 * parameter arrived at all, and `UpdateUserPluginTests.An_absent_staff_code_leaves_the_
 * stored_code_unchanged` pins that branch. The modal used to send the code on EVERY save,
 * so the only caller there is never took it - a pinned branch nothing reached (2026-09-22
 * review). These make it reachable and keep it so.
 */
describe('staffCodeToSend', () => {
  it('sends nothing when the field was not edited', () => {
    expect(staffCodeToSend('EMP1', 'EMP1')).toBeUndefined();
  });

  it('sends nothing when the person had no code and none was typed', () => {
    // The commonest save of all: somebody's NAME, on a person with no code. Sending an
    // empty string here would be read by the server as "clear it".
    expect(staffCodeToSend('', null)).toBeUndefined();
    expect(staffCodeToSend('', undefined)).toBeUndefined();
  });

  it('sends the new value when the code changed', () => {
    expect(staffCodeToSend('EMP2', 'EMP1')).toBe('EMP2');
  });

  it('sends the new value when a code is set for the first time', () => {
    expect(staffCodeToSend('EMP1', null)).toBe('EMP1');
  });

  it('sends an explicit empty string when a held code is cleared', () => {
    // The one case that MUST still send: an empty string is how the server is told to
    // clear the column, and it is distinguishable from absence only by being sent.
    expect(staffCodeToSend('', 'EMP1')).toBe('');
  });

  it('treats stray whitespace as no edit, and trims what it does send', () => {
    expect(staffCodeToSend('  EMP1  ', 'EMP1')).toBeUndefined();
    expect(staffCodeToSend('  EMP2  ', 'EMP1')).toBe('EMP2');
  });
});
