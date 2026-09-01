import { getClient } from '@microsoft/power-apps/data';
import { dataSourcesInfo } from '../../../.power/schemas/appschemas/dataSourcesInfo';
import { DEFAULT_FAILURE_MESSAGES, logTechnical } from '../errors';
import { dataSourceForCommand, type CommandOperation } from './operations';

export { newCorrelationKey } from './intentKey';

/**
 * Client-side entry point for the server-side lifecycle commands (AD-003). Every
 * state transition runs as a Dataverse Custom API, never an unrestricted record
 * update, so the guard, concurrency and audit logic live in the plug-in. This
 * module only marshals the request and classifies the standard failure codes.
 */

const client = getClient(dataSourcesInfo);

// The plug-in prefixes InvalidPluginExecutionException messages so the client can branch.
const FAILURE_PREFIXES: Record<string, CommandFailureKind> = {
  'VALIDATION:': 'validation',
  'UNAUTHORIZED:': 'unauthorized',
  'NOTFOUND:': 'notFound',
  'CONFLICT:': 'conflict',
  'PRECONDITION:': 'precondition',
};

export type CommandFailureKind =
  | 'validation'
  | 'unauthorized'
  | 'notFound'
  | 'conflict'
  | 'precondition'
  | 'unavailable';

export interface CommandSuccess<T> {
  ok: true;
  data: T;
}

export interface CommandFailure {
  ok: false;
  kind: CommandFailureKind;
  message: string;
}

export type CommandResult<T> = CommandSuccess<T> | CommandFailure;

/**
 * Pulls a human-readable message out of whatever the platform throws. The Power Apps
 * data client rejects with a plain object (not an Error), whose message can sit at
 * `.message`, `.error.message` or deeper, so a naive `String(error)` yields
 * "[object Object]" and both the classification and the support log lose the cause.
 */
export function extractErrorMessage(error: unknown, depth = 0): string {
  if (error == null) return '';
  if (typeof error === 'string') return error;
  if (error instanceof Error) return error.message;
  if (typeof error === 'object') {
    const obj = error as Record<string, unknown>;
    for (const key of ['message', 'Message', 'description', 'ExceptionMessage']) {
      const value = obj[key];
      if (typeof value === 'string' && value.trim()) return value;
    }
    if (depth < 4) {
      for (const key of ['error', 'innererror', 'InnerError', 'details', 'cause']) {
        if (obj[key] != null && typeof obj[key] === 'object') {
          const nested = extractErrorMessage(obj[key], depth + 1);
          if (nested) return nested;
        }
      }
    }
    try {
      const json = JSON.stringify(error);
      if (json && json !== '{}') return json;
    } catch {
      // Non-serialisable (e.g. circular): fall through to the default string form.
    }
  }
  return String(error);
}

function classify(error: unknown): CommandFailure {
  const message = extractErrorMessage(error);
  for (const prefix of Object.keys(FAILURE_PREFIXES)) {
    if (message.includes(prefix)) {
      return {
        ok: false,
        kind: FAILURE_PREFIXES[prefix],
        message: message.slice(message.indexOf(prefix) + prefix.length).trim() || message,
      };
    }
  }
  // Unclassified failures are system-level: log the raw cause for support, show a safe message.
  logTechnical('command failed', error);
  return {
    ok: false,
    kind: 'unavailable',
    message: DEFAULT_FAILURE_MESSAGES.unavailable,
  };
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
