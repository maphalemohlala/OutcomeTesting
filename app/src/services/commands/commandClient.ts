import { getClient } from '@microsoft/power-apps/data';
import { dataSourcesInfo } from '../../../.power/schemas/appschemas/dataSourcesInfo';
import { classify, type CommandResult } from './failures';
import { dataSourceForCommand, type CommandOperation } from './operations';

/**
 * Client-side entry point for the server-side lifecycle commands (AD-003). Every
 * state transition runs as a Dataverse Custom API, never an unrestricted record
 * update, so the guard, concurrency and audit logic live in the plug-in. This
 * module only marshals the request; classifying the failure lives in ./failures,
 * which stays free of the Power Apps client so the rule can be unit-tested.
 */

export { type CommandResult } from './failures';

const client = getClient(dataSourcesInfo);

let sent = 0;

/**
 * A count that moves when a command is sent and again when it settles, whatever became of
 * it. A screen holding rows from an earlier read compares this to know whether a command may
 * have changed them since - and a read begun while a command was still in flight, which may
 * have seen either side of it, is out of date by the same test once the command lands.
 */
export function commandsSent(): number {
  return sent;
}

/** For a write that goes to a table directly rather than through a command. */
export function noteWrite(): void {
  sent += 1;
}

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
  sent += 1;
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
  } finally {
    sent += 1;
  }
}
