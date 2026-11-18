import {
  choicesIncludingHeld,
  toOptionRows,
  type ManagedList,
  type RawListOption,
} from '../admin/listOptions';

/**
 * A single-choice managed dropdown on the edit case modal.
 *
 * Only the options offered TODAY, plus a retired one the case already holds, marked as such.
 * al_UpdateCaseDetails refuses a retired option a case does not hold, so offering one would
 * produce a choice the save rejects.
 *
 * Where the catalogue could not be read there is nothing to draw the choices from, and a
 * select with only "Not set" would read as a field that is empty. Said instead, and left
 * alone: an untouched field is not sent, so the value the case holds survives.
 */
export function ListOptionSelect({
  id,
  value,
  list,
  listRows,
  onChange,
}: {
  id: string;
  value: string;
  list: ManagedList;
  /** The whole catalogue, unshaped; empty where the read failed. */
  listRows: readonly RawListOption[];
  onChange: (value: string) => void;
}) {
  if (listRows.length === 0) {
    return (
      <p id={id} className="case-edit__help" role="status">
        {value
          ? 'The options for this list could not be read, so it cannot be changed right now. The case keeps the value it holds.'
          : 'The options for this list could not be read, so it cannot be set right now.'}
      </p>
    );
  }

  return (
    <select id={id} value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">Not set</option>
      {choicesIncludingHeld(toOptionRows(listRows, list, new Date()), value || null).map((option) => (
        <option key={option.id} value={option.id}>
          {option.label}
        </option>
      ))}
    </select>
  );
}
