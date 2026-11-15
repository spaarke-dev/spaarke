/**
 * LookupField - stale answers, failed searches and short queries (spaarkeai-mcp-bff-api-r1 task 032 fix round).
 *
 *  - A search answered late must not replace the results of a newer one (a slow request, or a host that resolves a superseded search with
 *    nothing): only the newest search's answer is applied, and an older answer does not turn the spinner off under the newer search.
 *  - `searchErrorText` (opt-in): a rejected search says so, never "No results found". Without it nothing changes.
 *  - `shortQueryHint` (opt-in) with `minSearchLength`: a short query says why nothing happens instead of "No results found".
 *
 * Per ADR-038 this is a KEEP-category suite for the public props surface of a shared component.
 */

import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { LookupField } from '../LookupField';
import type { ILookupItem } from '../../../types/LookupTypes';

const LABEL = 'Budget';
const NEW: ILookupItem = { id: 'n', name: 'FY26 litigation budget' };
const OLD: ILookupItem = { id: 'o', name: 'FY25 closed budget' };

/** A search whose every call is answered by the test, so the order of the answers is the test's. */
function controlledSearch() {
  const pending: Array<{ query: string; resolve: (items: ILookupItem[]) => void; reject: (e: Error) => void }> = [];
  const onSearch = jest.fn(
    (query: string) =>
      new Promise<ILookupItem[]>((resolve, reject) => {
        pending.push({ query, resolve, reject });
      })
  );
  return { onSearch, pending };
}

function renderField(overrides: Partial<React.ComponentProps<typeof LookupField>> = {}) {
  const onChange = jest.fn();
  const search = controlledSearch();
  const utils = renderWithProviders(
    <LookupField label={LABEL} value={null} onChange={onChange} onSearch={search.onSearch} {...overrides} />
  );
  return { onChange, ...search, ...utils };
}

const type = (text: string) => fireEvent.change(screen.getByRole('textbox'), { target: { value: text } });

describe('a search answered late does not replace a newer one', () => {
  it('a late EMPTY answer of an older search does not wipe the newer results', async () => {
    const { pending } = renderField();

    type('fy');
    await waitFor(() => expect(pending).toHaveLength(1), { timeout: 3000 });
    type('fy26');
    await waitFor(() => expect(pending).toHaveLength(2), { timeout: 3000 });

    await act(async () => pending[1].resolve([NEW])); // the newer search answers first
    expect(await screen.findByText(NEW.name)).toBeInTheDocument();
    await act(async () => pending[0].resolve([])); // the older one, late, with nothing

    expect(screen.getByText(NEW.name)).toBeInTheDocument();
    expect(screen.queryByText('No results found')).toBeNull();
  });

  it('a late answer with OTHER results does not replace them either', async () => {
    const { pending } = renderField();

    type('fy');
    await waitFor(() => expect(pending).toHaveLength(1), { timeout: 3000 });
    type('fy26');
    await waitFor(() => expect(pending).toHaveLength(2), { timeout: 3000 });
    await act(async () => pending[1].resolve([NEW]));
    await act(async () => pending[0].resolve([OLD]));

    expect(screen.getByText(NEW.name)).toBeInTheDocument();
    expect(screen.queryByText(OLD.name)).toBeNull();
  });

  it('an older answer does not turn the spinner off while the newer search is still out', async () => {
    const { pending } = renderField();

    type('fy');
    await waitFor(() => expect(pending).toHaveLength(1), { timeout: 3000 });
    type('fy26');
    await waitFor(() => expect(pending).toHaveLength(2), { timeout: 3000 });
    await act(async () => pending[0].resolve([OLD]));

    expect(screen.getByText('Searching...')).toBeInTheDocument();
    expect(screen.queryByText(OLD.name)).toBeNull();
    await act(async () => pending[1].resolve([NEW]));
    expect(await screen.findByText(NEW.name)).toBeInTheDocument();
    expect(screen.queryByText('Searching...')).toBeNull();
  });

  it('text deleted below the minimum drops the answer still on its way', async () => {
    const { pending } = renderField({ minSearchLength: 2 });

    type('fy');
    await waitFor(() => expect(pending).toHaveLength(1), { timeout: 3000 });
    type('f');
    await act(async () => pending[0].resolve([OLD]));

    expect(screen.queryByText(OLD.name)).toBeNull();
    expect(screen.queryByText('Searching...')).toBeNull();
  });
});

describe('a failed search (searchErrorText)', () => {
  const failed = async (overrides: Partial<React.ComponentProps<typeof LookupField>>) => {
    const field = renderField(overrides);
    const quiet = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    type('fy26');
    await waitFor(() => expect(field.pending).toHaveLength(1), { timeout: 3000 });
    await act(async () => field.pending[0].reject(new Error('429')));
    return { ...field, quiet };
  };

  it('says the search could not be completed, and is not "No results found"', async () => {
    const { quiet } = await failed({ searchErrorText: 'Search could not be completed. Try again.' });
    expect(await screen.findByRole('alert')).toHaveTextContent('Search could not be completed. Try again.');
    expect(screen.queryByText('No results found')).toBeNull();
    quiet.mockRestore();
  });

  it('the next search clears it', async () => {
    const { pending, quiet } = await failed({ searchErrorText: 'Search could not be completed. Try again.' });
    type('fy2');
    await waitFor(() => expect(pending).toHaveLength(2), { timeout: 3000 });
    expect(screen.queryByRole('alert')).toBeNull();
    await act(async () => pending[1].resolve([NEW]));
    expect(await screen.findByText(NEW.name)).toBeInTheDocument();
    quiet.mockRestore();
  });

  it('without the seam a failed search still reads "No results found", as it always did', async () => {
    const { quiet } = await failed({});
    expect(await screen.findByText('No results found')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).toBeNull();
    quiet.mockRestore();
  });
});

describe('a short query (minSearchLength with shortQueryHint)', () => {
  it('one letter asks nothing, says how many are needed, and is not "No results found"', async () => {
    const { onSearch } = renderField({ minSearchLength: 2, shortQueryHint: 'Type at least 2 letters' });

    type('f');

    expect(await screen.findByText('Type at least 2 letters')).toBeInTheDocument();
    expect(screen.queryByText('No results found')).toBeNull();
    await new Promise(resolve => setTimeout(resolve, 450));
    expect(onSearch).not.toHaveBeenCalled();
  });

  it('two letters search, and the hint goes away', async () => {
    const { pending } = renderField({ minSearchLength: 2, shortQueryHint: 'Type at least 2 letters' });
    type('f');
    type('fy');
    await waitFor(() => expect(pending).toHaveLength(1), { timeout: 3000 });
    expect(screen.queryByText('Type at least 2 letters')).toBeNull();
  });

  it('an empty box shows no short-query hint, and the field keeps its own hint', () => {
    renderField({ minSearchLength: 2, shortQueryHint: 'Type at least 2 letters', hint: 'Pick the budget' });
    expect(screen.queryByText('Type at least 2 letters')).toBeNull();
    expect(screen.getByText('Pick the budget')).toBeInTheDocument();
  });
});

describe('a browse at a term below the minimum survives the parent re-rendering', () => {
  const browse = () => fireEvent.click(screen.getByRole('button', { name: `Browse ${LABEL}` }));

  it('a re-render with a new onSearch identity mid-flight does not drop the answer', async () => {
    const { pending, onChange, rerender } = renderField({ minSearchLength: 2 });

    browse();
    await waitFor(() => expect(pending).toHaveLength(1));
    // The parent re-renders with an inline onSearch (a new identity every render), so the effect runs again while the browse is out.
    const next = controlledSearch();
    rerender(
      <LookupField label={LABEL} value={null} onChange={onChange} onSearch={next.onSearch} minSearchLength={2} />
    );
    await act(async () => pending[0].resolve([NEW]));

    expect(await screen.findByText(NEW.name)).toBeInTheDocument();
    expect(screen.queryByText('Searching...')).toBeNull();
  });

  it('a re-render does not close a browse list that is already showing', async () => {
    const { pending, onChange, rerender } = renderField({ minSearchLength: 2 });

    browse();
    await waitFor(() => expect(pending).toHaveLength(1));
    await act(async () => pending[0].resolve([NEW]));
    expect(await screen.findByText(NEW.name)).toBeInTheDocument();
    const next = controlledSearch();
    rerender(
      <LookupField label={LABEL} value={null} onChange={onChange} onSearch={next.onSearch} minSearchLength={2} />
    );

    expect(screen.getByText(NEW.name)).toBeInTheDocument();
  });

  it('still drops an answer for a longer term once the text is cut below the minimum, and on a change within the short range', async () => {
    const { pending } = renderField({ minSearchLength: 3 });
    type('fy2');
    await waitFor(() => expect(pending).toHaveLength(1), { timeout: 3000 });
    type('fy');
    await act(async () => pending[0].resolve([OLD]));
    expect(screen.queryByText(OLD.name)).toBeNull();

    // A browse at 'fy', then the text changes within the short range: the browse answer is for a term that is gone.
    browse();
    await waitFor(() => expect(pending).toHaveLength(2));
    type('f');
    await act(async () => pending[1].resolve([OLD]));
    expect(screen.queryByText(OLD.name)).toBeNull();
  });
});
