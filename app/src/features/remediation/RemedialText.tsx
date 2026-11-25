/**
 * A checker's remedial action. Formatted words (owner, 2026-11-23) are stored as markup that
 * RemedialActionsRequestPlugin cleaned to HtmlSanitiser's allow-list, so they are drawn as
 * markup, as a Rich text answer is on the review page. Words written before the editor are
 * plain text and are drawn as text.
 */
export function RemedialText({ text }: { text: string | null }) {
  if (!text) return <>—</>;
  if (text.trimStart().startsWith('<')) {
    return <span className="value value--rich" dangerouslySetInnerHTML={{ __html: text }} />;
  }
  return <>{text}</>;
}
