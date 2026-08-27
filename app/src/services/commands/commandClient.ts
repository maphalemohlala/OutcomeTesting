import { getClient } from '@microsoft/power-apps/data';
import { dataSourcesInfo } from '../../../.power/schemas/appschemas/dataSourcesInfo';
import { DEFAULT_FAILURE_MESSAGES, logTechnical } from '../errors';

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

/** A fresh idempotency key for one user intent. Reuse it across retries of that intent. */
export function newCorrelationKey(): string {
  return crypto.randomUUID();
}

function classify(error: unknown): CommandFailure {
  const message = error instanceof Error ? error.message : String(error ?? '');
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
 * Invokes an unbound Dataverse Custom API and normalises the result. `anchorTable`
 * is a registered data source; it only resolves the environment to call. It must be the
 * entity-set name used as a key in dataSourcesInfo (plural, e.g. al_remediationactions),
 * not the singular logical name, or the player cannot resolve the data source.
 */
export async function executeCommand<TResult>(
  operationName: string,
  body: Record<string, unknown>,
  anchorTable = 'al_remediationactions',
): Promise<CommandResult<TResult>> {
  try {
    const result = await client.executeAsync<never, TResult>({
      dataverseRequest: {
        action: 'customapi',
        parameters: {
          operationName,
          tableName: anchorTable,
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
