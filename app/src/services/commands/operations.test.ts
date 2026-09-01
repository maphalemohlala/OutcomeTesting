import { describe, expect, it } from 'vitest';
import { dataSourcesInfo } from '../../../.power/schemas/appschemas/dataSourcesInfo';
import { COMMAND_OPERATIONS, dataSourceForCommand } from './operations';

/**
 * Guards the defect that made every save fail: the Power Apps client resolves a
 * custom API as `dataSourcesInfo[tableName].apis[operationName]`, so a command whose
 * API was never added to the code app is unresolvable and never reaches Dataverse.
 */

describe('dataSourceForCommand', () => {
  it('maps an operation to the lower-cased key the generator emits', () => {
    expect(dataSourceForCommand('al_RetireAndSucceedQuestion')).toBe(
      'al_retireandsucceedquestion',
    );
  });
});

describe('custom API registration', () => {
  const sources = dataSourcesInfo as Record<string, { apis: Record<string, unknown> }>;

  it.each(COMMAND_OPERATIONS)('%s is registered with the code app', (operationName) => {
    const key = dataSourceForCommand(operationName);
    const source = sources[key];
    expect(
      source,
      `No data source "${key}". Run: pa app add dataverse-api --api-name ${operationName}`,
    ).toBeDefined();
    expect(Object.keys(source.apis)).toContain(operationName);
  });
});
