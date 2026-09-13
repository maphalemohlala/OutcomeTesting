import type { HeaderField } from './checklistForm';

/**
 * The case header as the Checker Checklist draws it: a ruled table, two label/value pairs
 * to a row, in the document's own order (project owner, 2026-09-13).
 *
 * One component for the case page and the review page. Both draw the same eighteen fields
 * from `caseHeaderFields`, and each used to build the pairs itself; the audit of
 * 2026-09-13 found the two copies already disagreeing on how an empty value reads. The
 * pairing is the rule worth sharing - which cell an odd final field leaves empty, and the
 * reading order - so it lives here once, and each page keeps only its own ruling.
 *
 * Pairs are built here rather than by CSS columns so the reading order is the document's
 * (left to right, then down) for a screen reader as well as for the eye; a two-column grid
 * would read down one column and then down the other. An odd final field leaves its second
 * cell empty rather than stretching across, which is what the form does too.
 */
export function CaseHeaderTable({
  fields,
  variant,
}: {
  fields: HeaderField[];
  /**
   * `case` is the case page: header cells for the labels, its own stylesheet, and an
   * unrecorded value said in words. `review` is the checklist document: plain `.lbl` /
   * `.val` cells the shared document stylesheet rules, and an unrecorded value left blank
   * as the paper form leaves it.
   */
  variant: 'case' | 'review';
}) {
  const pairs: HeaderField[][] = [];
  for (let i = 0; i < fields.length; i += 2) pairs.push(fields.slice(i, i + 2));

  const isCase = variant === 'case';

  return (
    <table className={isCase ? 'case-detail__header' : 'meta'}>
      {isCase ? <caption className="visually-hidden">Case header</caption> : null}
      <tbody>
        {pairs.map((pair) => (
          <tr key={pair[0].label}>
            {pair.map((field) => [
              isCase ? (
                <th key={`${field.label}-l`} scope="row">
                  {field.label}
                </th>
              ) : (
                <td key={`${field.label}-l`} className="lbl">
                  {field.label}
                </td>
              ),
              <td
                key={`${field.label}-v`}
                className={isCase ? undefined : 'val'}
                data-empty={field.value === null ? 'true' : undefined}
              >
                {field.value ?? (isCase ? 'Not recorded' : '')}
              </td>,
            ])}
            {pair.length === 1 ? (
              isCase ? (
                <>
                  <th aria-hidden="true" />
                  <td aria-hidden="true" />
                </>
              ) : (
                <>
                  <td className="lbl" aria-hidden="true" />
                  <td className="val" aria-hidden="true" />
                </>
              )
            ) : null}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
