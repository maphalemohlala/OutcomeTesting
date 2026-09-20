import { beforeEach, describe, expect, it, vi } from 'vitest';
import { dataSourcesInfo } from '../../../.power/schemas/appschemas/dataSourcesInfo';
import { COMMAND_OPERATIONS, dataSourceForCommand } from './operations';

/**
 * The Power Apps client builds each request body from the parameter list in
 * dataSourcesInfo.ts, so anything the wrapper puts in `body` that the schema does not
 * declare is discarded on the way out - no error, no warning, and the command still
 * succeeds. The parameters below are therefore captured as the wrapper sends them and
 * checked against what the schema declares.
 */
const sent: Array<{ operationName: string; body: Record<string, unknown> }> = [];

vi.mock('@microsoft/power-apps/data', () => ({
  getClient: () => ({
    executeAsync: async (request: {
      dataverseRequest: {
        parameters: { operationName: string; body: Record<string, unknown> };
      };
    }) => {
      sent.push({ ...request.dataverseRequest.parameters });
      return { success: true, data: {} };
    },
  }),
}));

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

/**
 * F14, found in DEV on 2026-09-20. `al_RetireAndSucceedQuestion` gained a `DisplayOrder`
 * parameter server-side, the wrapper was written to send it, and the generated schema was
 * never regenerated. The client dropped it: an administrator could type a new display
 * order, watch a new version appear with no error, and find the order unchanged. Eight
 * versions of one question were created this way before the parameter was found missing.
 *
 * The test above proves the API is registered. It never looked at the parameters, which is
 * exactly the gap the defect went through, so this one sends every field each wrapper
 * accepts and insists the schema declares all of them.
 */
describe('every parameter a command sends is declared in the schema', () => {
  const sources = dataSourcesInfo as Record<
    string,
    { apis: Record<string, { parameters?: Array<{ name: string }> }> }
  >;

  function declaredFor(operationName: string): Set<string> {
    const api = sources[dataSourceForCommand(operationName)]?.apis?.[operationName];
    return new Set((api?.parameters ?? []).map((p) => p.name));
  }

  beforeEach(() => {
    sent.length = 0;
  });

  // Every optional field is populated on purpose: an optional one that is never sent is
  // precisely the one whose absence from the schema nobody notices.
  const calls: Array<[string, () => Promise<unknown>]> = [
    [
      'al_AddQuestion',
      async () => {
        const { addQuestion } = await import('./questions');
        return addQuestion({
          sectionId: 's',
          questionCode: 'Q-1',
          name: 'n',
          wording: 'w',
          responseType: 1,
          mandatory: true,
          displayOrder: 2,
          effectiveFrom: '2026-01-01',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_RetireAndSucceedQuestion',
      async () => {
        const { retireAndSucceedQuestion } = await import('./questions');
        return retireAndSucceedQuestion({
          questionId: 'q',
          newWording: 'w',
          responseType: 1,
          mandatory: false,
          displayOrder: 3,
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_RetireQuestion',
      async () => {
        const { retireQuestion } = await import('./questions');
        return retireQuestion({
          questionId: 'q',
          effectiveTo: '2026-01-01',
          reason: 'r',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_MoveQuestion',
      async () => {
        const { moveQuestion } = await import('./questions');
        return moveQuestion({
          questionId: 'q',
          targetSectionId: 's',
          newQuestionCode: 'Q-2',
          effectiveFrom: '2026-01-01',
          reason: 'r',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_AddSection',
      async () => {
        const { addSection } = await import('./sections');
        return addSection({
          sectionCode: 'S-1',
          name: 'n',
          helpText: 'h',
          ownerRole: 1,
          displayOrder: 1,
          isOptional: true,
          effectiveFrom: '2026-01-01',
          questions: [
            { code: 'Q-1', name: 'n', wording: 'w', responseType: 1, mandatory: true, displayOrder: 1 },
          ],
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_UpdateSection',
      async () => {
        const { updateSection } = await import('./sections');
        return updateSection({
          sectionId: 's',
          name: 'n',
          helpText: 'h',
          ownerRole: 1,
          displayOrder: 1,
          isOptional: false,
          reason: 'r',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_RetireSection',
      async () => {
        const { retireSection } = await import('./sections');
        return retireSection({
          sectionId: 's',
          effectiveTo: '2026-01-01',
          reason: 'r',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_AssignCase',
      async () => {
        const { assignCase } = await import('./assignCase');
        return assignCase({
          caseId: 'c',
          assigneeEmail: 'a@example.invalid',
          reviewInstanceId: 'r',
          team: 't',
          reason: 'r',
          expectedRowVersion: '1',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_CompleteRemediation',
      async () => {
        const { completeRemediation } = await import('./completeRemediation');
        return completeRemediation({
          actionId: 'a',
          expectedRowVersion: '1',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_CreateExportBatch',
      async () => {
        const { createExportBatch } = await import('./exports');
        return createExportBatch({ name: 'n', idempotencyKey: 'k' });
      },
    ],
    [
      'al_GenerateExport',
      async () => {
        const { generateExport } = await import('./exports');
        return generateExport({ batchId: 'b', idempotencyKey: 'k' });
      },
    ],
    [
      'al_ImportCases',
      async () => {
        const { importCases } = await import('./importCases');
        return importCases({ fileName: 'f.csv', csv: 'a,b', idempotencyKey: 'k' });
      },
    ],
    [
      'al_RegradeCase',
      async () => {
        const { regradeCase } = await import('./regradeCase');
        return regradeCase({
          outcomeId: 'o',
          finalOutcome: 1 as never,
          reason: 'r',
          expectedRowVersion: '1',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_ResolveImportException',
      async () => {
        const { resolveImportException } = await import('./resolveImportException');
        return resolveImportException({
          exceptionId: 'e',
          resolution: 'Retry' as never,
          note: 'n',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_SetFailAccountability',
      async () => {
        const { setFailAccountability } = await import('./setFailAccountability');
        return setFailAccountability({
          outcomeId: 'o',
          fqAdviser: true,
          fqParaplanner: true,
          aqAdviser: true,
          aqParaplanner: true,
          fqContactId: 'c1',
          aqContactId: 'c2',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_UpdateCaseDetails',
      async () => {
        const { updateCaseDetails } = await import('./updateCaseDetails');
        return updateCaseDetails({
          caseId: 'c',
          status: 1,
          routeId: 'r',
          priority: 2,
          dueDate: '2026-01-01',
          fields: { al_ioreference: 'IO-1' },
          reason: 'r',
          expectedRowVersion: '1',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_AssignUserRole',
      async () => {
        const { assignUserRole } = await import('./permissions');
        return assignUserRole({
          userEmail: 'a@example.invalid',
          appRole: 'role',
          roleCode: 'code',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_SetPagePermission',
      async () => {
        const { setPagePermission } = await import('./permissions');
        return setPagePermission({
          appRole: 'role',
          roleCode: 'code',
          resourceKey: 'page',
          accessLevel: 'Manage',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_SetPermissionRuleActive',
      async () => {
        const { setPermissionRuleActive } = await import('./permissions');
        return setPermissionRuleActive({ id: 'p', active: true, idempotencyKey: 'k' });
      },
    ],
    [
      'al_SetRoleAssignmentActive',
      async () => {
        const { setRoleAssignmentActive } = await import('./permissions');
        return setRoleAssignmentActive({ id: 'r', active: false, idempotencyKey: 'k' });
      },
    ],
    [
      'al_AdoptRoleAssignment',
      async () => {
        const { adoptRoleAssignment } = await import('./roleReconciliation');
        return adoptRoleAssignment({
          userEmail: 'a@example.invalid',
          roleCode: 'code',
          decision: 'Adopt',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_CreateRole',
      async () => {
        const { createRole } = await import('./roles');
        return createRole({ roleName: 'n', description: 'd', idempotencyKey: 'k' });
      },
    ],
    [
      'al_UpdateRole',
      async () => {
        const { updateRole } = await import('./roles');
        return updateRole({
          roleId: 'r',
          roleName: 'n',
          description: 'd',
          active: true,
          expectedRowVersion: '1',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      'al_CreateUser',
      async () => {
        const { createUser } = await import('./users');
        return createUser({ fullName: 'n', workEmail: 'a@example.invalid', idempotencyKey: 'k' });
      },
    ],
    [
      'al_UpdateUser',
      async () => {
        const { updateUser } = await import('./users');
        return updateUser({
          userId: 'u',
          fullName: 'n',
          expectedRowVersion: '1',
          idempotencyKey: 'k',
        });
      },
    ],
    [
      // No wrapper function: the two callers build the body and call executeCommand directly,
      // so the call is reproduced here in the same shape.
      'al_GetRoleHolders',
      async () => {
        const { executeCommand } = await import('./commandClient');
        return executeCommand('al_GetRoleHolders', { RoleCode: 'code' });
      },
    ],
    [
      'al_SetUserActive',
      async () => {
        const { setUserActive } = await import('./users');
        return setUserActive({ userId: 'u', active: false, idempotencyKey: 'k' });
      },
    ],
  ];

  it.each(calls)('%s sends nothing the schema would drop', async (operationName, call) => {
    await call();

    const call0 = sent.find((c) => c.operationName === operationName);
    expect(call0, `${operationName} was never invoked`).toBeDefined();

    const declared = declaredFor(operationName);
    const dropped = Object.keys(call0!.body).filter((key) => !declared.has(key));

    expect(
      dropped,
      `${operationName} sends ${dropped.join(', ')}, which dataSourcesInfo.ts does not declare, ` +
        `so the client discards it. Run: pa app add dataverse-api --api-name ${operationName}`,
    ).toEqual([]);
  });

  it('covers every command that takes parameters', () => {
    const covered = new Set(calls.map(([name]) => name));
    const uncovered = COMMAND_OPERATIONS.filter(
      (name) => !covered.has(name) && declaredFor(name).size > 0,
    );
    expect(uncovered).toEqual([]);
  });
});
