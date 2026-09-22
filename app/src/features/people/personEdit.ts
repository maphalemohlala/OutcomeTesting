/**
 * What an edit of somebody's person record should actually send.
 *
 * Pure and separate from the modal so the one decision that matters here can be pinned by a
 * unit test: this repository has no way to drive a React form's submit handler, and the rule
 * below is not one to leave unproven.
 */

/**
 * The `staffCode` to hand `updateUser`, or `undefined` to leave the stored code alone.
 *
 * `al_UpdateUser` tells "leave it" from "clear it" by whether the StaffCode parameter
 * arrived at all: absent leaves the stored value, an explicit empty string clears it. The
 * modal used to send `staffCode.trim()` on every save, which meant the only caller there is
 * never took the absent branch - so the server-side distinction was untested in practice
 * and the client leaned entirely on the READ working.
 *
 * That read is this design's one unproven link (see `src/hooks/staffCodeSchema.test.ts`). If
 * `al_staffcode` ever came back blank from the data source, an unconditional send would turn
 * every save of somebody's NAME into a silent wipe of their employee code - precisely the
 * failure absent-vs-empty exists to prevent. Sending it only when it moved makes the
 * unedited case harmless whatever the read does.
 *
 * Both sides are compared trimmed, because the stored value is already trimmed on the way
 * out of the directory and a user's stray space is not an edit.
 */
export function staffCodeToSend(edited: string, stored: string | null | undefined): string | undefined {
  const next = edited.trim();
  return next === (stored ?? '').trim() ? undefined : next;
}
