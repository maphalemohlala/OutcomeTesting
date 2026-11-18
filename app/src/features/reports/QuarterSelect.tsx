interface Quarter {
  from: string;
  to: string;
  label: string;
}

/**
 * The Quarter filter on an export batch. "Any" clears both dates, so the file holds every
 * case; dates typed by hand that match no quarter show as "Custom dates", which can be seen
 * but not chosen, because choosing it could not mean anything but what is already there.
 */
export function QuarterSelect({
  quarters,
  from,
  to,
  onChoose,
  anyLabel = 'Any (every case)',
}: {
  quarters: readonly Quarter[];
  from: string;
  to: string;
  onChoose: (range: { from: string; to: string }) => void;
  /** What choosing no quarter means for this file. */
  anyLabel?: string;
}) {
  const quarter = quarters.find((q) => q.from === from && q.to === to);
  const custom = !quarter && (from !== '' || to !== '');

  return (
    <select
      id="check-quarter"
      value={quarter ? quarter.from : custom ? 'custom' : ''}
      onChange={(e) => {
        const chosen = quarters.find((q) => q.from === e.target.value);
        onChoose({ from: chosen?.from ?? '', to: chosen?.to ?? '' });
      }}
    >
      <option value="">{anyLabel}</option>
      {custom ? (
        <option value="custom" disabled>
          Custom dates
        </option>
      ) : null}
      {quarters.map((q) => (
        <option key={q.from} value={q.from}>
          {q.label}
        </option>
      ))}
    </select>
  );
}
