import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { managedList } from '../admin/listOptions';
import { ListOptionSelect } from './ListOptionSelect';
import { checkerHelp } from './caseEditForm';

const list = managedList('case-type');

describe('a single-choice managed dropdown', () => {
  it('says the options could not be read instead of showing a held value as Not set', () => {
    const html = renderToStaticMarkup(
      <ListOptionSelect id="f" value="held-guid" list={list} listRows={[]} onChange={() => {}} />,
    );
    expect(html).not.toContain('<select');
    expect(html).toContain('could not be read');
    expect(html).toContain('keeps the value it holds');
  });

  it('draws the choices, the held one selected, once the catalogue is read', () => {
    const rows = [{ al_listoptionid: 'g1', al_name: 'Full advice', al_list: list.value }];
    const html = renderToStaticMarkup(
      <ListOptionSelect id="f" value="g1" list={list} listRows={rows as never} onChange={() => {}} />,
    );
    expect(html).toContain('<option value="g1" selected="">Full advice</option>');
  });
});

describe('the line under a checker field', () => {
  it('says the role holders are loading rather than implying nobody holds the role', () => {
    expect(checkerHelp('loading', 'AL Portal - AQS Reviewer')).toBe('Loading who holds the AL Portal - AQS Reviewer role…');
    expect(checkerHelp('ready', 'AL Portal - AQS Reviewer')).toContain('Only people holding');
    expect(checkerHelp('unavailable', 'x')).toContain('could not be read');
  });
});
