import './ValidationSummary.css';

/**
 * Groups the validation messages for a form so the user sees every blocking issue at
 * once (item 1 recommendation: validation summaries), rather than one error at a time.
 */
export function ValidationSummary({
  title = 'Please correct the following before continuing',
  errors,
}: {
  title?: string;
  errors: string[];
}) {
  if (errors.length === 0) {
    return null;
  }
  return (
    <div className="validation-summary" role="alert" aria-live="assertive">
      <p className="validation-summary__title">{title}</p>
      <ul className="validation-summary__list">
        {errors.map((error) => (
          <li key={error}>{error}</li>
        ))}
      </ul>
    </div>
  );
}
