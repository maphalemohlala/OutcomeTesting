import { getClient } from '@microsoft/power-apps/data';
import { dataSourcesInfo } from '../../../.power/schemas/appschemas/dataSourcesInfo';
import { classify, type CommandResult } from './failures';
import { dataSourceForCommand, type CommandOperation } from './operations';

export { newCorrelationKey } from './intentKey';

/**
 * Client-side entry point for the server-side lifecycle commands (AD-003). Every
 * state transition runs as a Dataverse Custom API, never an unrestricted record
 * update, so the guard, concurrency and audit logic live in the plug-in. This
 * module only marshals the request; classifying the failure lives in ./failures,
 * which stays free of the Power Apps client so the rule can be unit-tested.
 */

export {
  extractErrorMessage,
  sentenceAfterPrefix,
  type CommandFailure,
  type CommandFailureKind,
  type CommandResult,
  type CommandSuccess,
} from './failures';

const client = getClient(dataSourcesInfo);

/**
 * Invokes an unbound Dataverse Custom API and normalises the result. The Power Apps client
 * resolves the call as `dataSourcesInfo[tableName].apis[operationName]`, and the generator
 * registers each custom API as its own data source, so `tableName` is the API's own key --
 * not the table the command happens to write to.
 */
export async function executeCommand<TResult>(
  operationName: CommandOperation,
  body: Record<string, unknown>,
): Promise<CommandResult<TResult>> {
  try {
    const result = await client.executeAsync<never, TResult>({
      dataverseRequest: {
        action: 'customapi',
        parameters: {
          operationName,
          tableName: dataSourceForCommand(operationName),
          body,
        },
      },
    });

    if (!result.success) {
      return classify(result.error);
    }

    return { ok: true, data: result.data };
  } catch (error) {
    return classify(error);
  }
}
