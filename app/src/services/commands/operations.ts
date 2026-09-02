/**
 * The custom API operations this app invokes (AD-003). Adding one here is not enough:
 * the API must also be registered with the code app via
 * `pa app add dataverse-api --api-name <name>`, or the Power Apps client cannot resolve
 * it and the command fails before it reaches Dataverse. operations.test.ts enforces this.
 */
export const COMMAND_OPERATIONS = [
  'al_AssignCase',
  'al_AssignUserRole',
  'al_CompleteRemediation',
  'al_CreateExportBatch',
  'al_CreateRole',
  'al_CreateUser',
  'al_GenerateExport',
  'al_RetireAndSucceedQuestion',
  'al_SetPagePermission',
  'al_SetPermissionRuleActive',
  'al_SetRoleAssignmentActive',
  'al_SetUserActive',
  'al_UpdateCaseDetails',
  'al_UpdateRole',
  'al_UpdateUser',
] as const;

export type CommandOperation = (typeof COMMAND_OPERATIONS)[number];

/** The generator registers each custom API as its own data source, keyed on the lower-cased name. */
export function dataSourceForCommand(operationName: string): string {
  return operationName.toLowerCase();
}
