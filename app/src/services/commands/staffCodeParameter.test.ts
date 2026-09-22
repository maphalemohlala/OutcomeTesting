import { describe, expect, it } from 'vitest';
// Imported as text rather than read with node:fs, because tsconfig.app.json restricts
// ambient types to vite/client on purpose - app code must not reach for Node APIs that do
// not exist in a browser, and one test is not a reason to widen that (see ownerRole.test.ts,
// caseUpload.test.ts, protectedQuestions.test.ts for the same convention).
import dataSourcesInfoSource from '../../../.power/schemas/appschemas/dataSourcesInfo.ts?raw';

/**
 * The Code App discards a command parameter `dataSourcesInfo.ts` does not declare, in
 * silence, and the command still reports success. The generated file is regenerated from
 * Dataverse metadata, and that metadata lags a new Custom API parameter by hours, so this
 * declaration is written by hand.
 *
 * This test is the tripwire: if a regeneration ever drops StaffCode, the employee code
 * stops saving and nothing else would say so.
 */
describe('al_UpdateUser parameter declaration', () => {
  it('declares StaffCode, or the employee code silently never saves', () => {
    const command = dataSourcesInfoSource.slice(dataSourcesInfoSource.indexOf('"al_UpdateUser"'));
    const body = command.slice(0, command.indexOf('"responseInfo"'));

    expect(body).toContain('"name": "StaffCode"');
  });
});
