import { describe, expect, it } from 'vitest';
// Imported as text rather than read with node:fs, because tsconfig.app.json restricts
// ambient types to vite/client on purpose - app code must not reach for Node APIs that do
// not exist in a browser, and one test is not a reason to widen that (see
// staffCodeParameter.test.ts for the same convention).
import contactsSchemaSource from '../../.power/schemas/dataverse/contacts.Schema.json?raw';

/**
 * INFERRED, not established: that the Code App's data source returns only the columns its
 * schema declares. It follows from how the generated client is built - the schema is what
 * describes the shape a `getAll`/`get` hands back - but nothing in `app/src` imports
 * `contacts.Schema.json` at runtime, so no code in this repository demonstrates the link,
 * and it has never been checked against a live environment (2026-09-22 review; this
 * docstring previously stated it as fact, and a test file is what future readers trust).
 *
 * What would DISPROVE it: an employee code saved through the People page and read back
 * correctly with the declaration removed from `contacts.Schema.json`. If that works, the
 * declaration is not load-bearing and this tripwire guards nothing.
 *
 * What will SETTLE it: the deployment verification step in
 * `docs/superpowers/plans/2026-09-22-staff-codes-registry.md`, Task 11 - set an employee
 * code on a contact in DEV, save, reload, and confirm it persisted. That is the step that
 * exercises the read end to end. Until it has been run, treat the premise as reasoning.
 *
 * The assertion below stands either way. The generated schema is regenerated from Dataverse
 * metadata, and that metadata lags a new column by hours, so `al_staffcode` is declared by
 * hand (Task 7 ruling) rather than waiting for a regeneration that cannot yet see it - and
 * if a future regeneration dropped the declaration, the cost of the premise being TRUE is
 * that the People page saves the employee code and reads it back blank forever, with
 * nothing else to say so. Cheap tripwire, expensive silence.
 */
describe('Contacts data source schema', () => {
  it('declares al_staffcode, or the employee code silently never reads back', () => {
    const schema = JSON.parse(contactsSchemaSource);
    const property = schema.schema.items.properties.al_staffcode;

    expect(property).toBeDefined();
    expect(property['x-ms-dataverse-attribute']).toBe('al_staffcode');
    expect(property['x-ms-schema-name']).toBe('al_StaffCode');
  });
});
