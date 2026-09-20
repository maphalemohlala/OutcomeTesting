/**
 * The day a checklist change takes effect, as the commands take it (AD-122).
 *
 * Deliberately the **UTC** day and not the browser's. Every command compares the date it is
 * given against `DateTime.UtcNow.Date` and refuses anything earlier, so a local-day default
 * would be refused as "in the past" for anybody east of UTC between midnight and their
 * offset - they would open the editor, touch nothing, save, and be told the date they never
 * chose is invalid.
 *
 * Shared rather than written out twice so the field that sets a start date and the field
 * that sets an end date cannot drift into disagreeing about what today is.
 */
export function today(): string {
  return new Date().toISOString().slice(0, 10);
}
