import { describe, expect, it } from 'vitest';
// Imported as text rather than read with node:fs, because tsconfig.app.json restricts
// ambient types to vite/client on purpose - app code must not reach for Node APIs that do
// not exist in a browser, and one test is not a reason to widen that (see
// staffCodeParameter.test.ts for the same convention).
import contactsSchemaSource from '../../.power/schemas/dataverse/contacts.Schema.json?raw';

/**
 * The Code App's data source only returns the columns its schema declares. The generated
 * schema is regenerated from Dataverse metadata, and that metadata lags a new column by
 * hours, so `al_staffcode` is declared here by hand (Task 7 ruling) rather than waiting for
 * a regeneration that cannot yet see it.
 *
 * This test is the tripwire: if a future regeneration ever drops the declaration, the
 * People page would keep saving the employee code but read it back as blank forever, and
 * nothing else would say so.
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
