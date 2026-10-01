import { EnvironmentvariabledefinitionsService, EnvironmentvariablevaluesService } from '../../generated';
import { logTechnical } from '../../services/errors';
import { odataEscape } from '../../services/odata';
import { DEFAULT_PRODUCT, PRODUCT_VARIABLE, cleanProductName } from './productName';
import { publishProductName, resetProductNameStoreForTests } from './useProductName';

/** The read in flight or done; started once per session. */
let started: Promise<string> | null = null;

/** The value set on this environment, else the shipped default, else DEFAULT_PRODUCT. */
export async function readProductName(): Promise<string> {
  const definitions = await EnvironmentvariabledefinitionsService.getAll({
    select: ['environmentvariabledefinitionid', 'defaultvalue'],
    filter: `schemaname eq '${odataEscape(PRODUCT_VARIABLE)}'`,
  });
  if (!definitions.success) {
    logTechnical('product name: definition', definitions.error);
    return DEFAULT_PRODUCT;
  }
  const definition = definitions.data?.[0];
  if (!definition) return DEFAULT_PRODUCT;

  const values = await EnvironmentvariablevaluesService.getAll({
    select: ['value'],
    filter: `_environmentvariabledefinitionid_value eq ${definition.environmentvariabledefinitionid}`,
  });
  if (!values.success) logTechnical('product name: value', values.error);
  const set = values.success ? values.data?.[0]?.value : undefined;

  return cleanProductName((set ?? '').trim() !== '' ? set : definition.defaultvalue);
}

/** Starts the read once; later calls return the same promise. Never rejects. */
export function loadProductName(): Promise<string> {
  if (!started) {
    started = readProductName()
      .catch((error: unknown) => {
        logTechnical('product name', error);
        return DEFAULT_PRODUCT;
      })
      .then((name) => {
        publishProductName(name);
        return name;
      });
  }
  return started;
}

/** Test seam: forget what was read. */
export function resetProductNameForTests(): void {
  started = null;
  resetProductNameStoreForTests();
}
