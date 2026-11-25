import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { CaseReferenceLink } from './CaseReferenceLink';

/** The Case cell on the worklist, a person's cases and the completed-case report. */
function draw(serviceCaseRef: string | null): string {
  return renderToStaticMarkup(
    <MemoryRouter>
      <CaseReferenceLink id="case-1" caseReference="254471517" serviceCaseRef={serviceCaseRef} />
    </MemoryRouter>,
  );
}

describe('CaseReferenceLink', () => {
  it('links the TaskID and names the service case reference beneath it (client, 2026-11-24)', () => {
    expect(draw('IOA07066846')).toBe(
      '<a href="/cases/case-1" data-discover="true">254471517</a>' +
        '<span class="case-ref__service"><span class="visually-hidden">Service case reference </span>IOA07066846</span>',
    );
  });

  it('draws the TaskID alone when the upload carried no service case reference', () => {
    expect(draw(null)).toBe('<a href="/cases/case-1" data-discover="true">254471517</a>');
  });
});
