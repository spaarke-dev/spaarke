/**
 * The type-ahead as the shared field uses it (task 032 fix round). The field applies the answers it gets as they arrive, so what a
 * superseded search resolves with decides what the user sees. These cases go through the real `LookupField`:
 *  - a late answer to an older request never wipes or replaces the candidates of the newer one
 *  - a failed search (throttled: 429) says "Search could not be completed. Try again.", never "No results found"
 *  - one letter asks the tool nothing and says "Type at least 2 letters", never "No results found"
 */
import * as React from 'react';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { LookupField } from '@spaarke/ui-components/components/LookupField/LookupField';
import type { ILookupItem } from '@spaarke/ui-components/types/LookupTypes';
import { createLookupTypeAhead, LOOKUP_DEBOUNCE_MS } from '../wizard/lookupTypeAhead';

const SIGNAL = '50000001-0000-4000-8000-000000000000';
const FIELD_DEBOUNCE_MS = 300; // the shared field's own

const response = (body: unknown, ok = true) => ({ ok, json: async () => body }) as unknown as Response;
const candidate = (id: string, name: string) => response({ candidates: [{ id, name }] });

function Harness({ lookup }: { lookup: ReturnType<typeof createLookupTypeAhead> }) {
  const [value, setValue] = React.useState<ILookupItem | null>(null);
  const onSearch = React.useCallback(
    (query: string) =>
      lookup({
        kind: 'Lookup',
        entity: 'sprk_budget',
        parameterCode: 'budget',
        query,
        core: null,
        signalId: SIGNAL,
      }),
    [lookup]
  );
  return (
    <FluentProvider theme={webLightTheme}>
      <LookupField
        label="Budget"
        value={value}
        onChange={setValue}
        onSearch={onSearch}
        minSearchLength={lookup.minChars}
        shortQueryHint={`Type at least ${lookup.minChars} letters`}
        searchErrorText={lookup.failureText}
      />
    </FluentProvider>
  );
}

const typeText = (text: string) => fireEvent.change(screen.getByRole('textbox'), { target: { value: text } });
const advance = (ms: number) =>
  act(async () => {
    await jest.advanceTimersByTimeAsync(ms);
  });

beforeEach(() => jest.useFakeTimers());
afterEach(() => jest.useRealTimers());

describe('the type-ahead through the shared field', () => {
  it('a late answer to an older request does not wipe the newer candidates', async () => {
    const finishers: Array<(r: Response) => void> = [];
    const search = jest.fn(() => new Promise<Response>(resolve => finishers.push(resolve)));
    render(<Harness lookup={createLookupTypeAhead(search)} />);

    typeText('fy');
    await advance(FIELD_DEBOUNCE_MS + LOOKUP_DEBOUNCE_MS); // the older request is asked
    typeText('fy26');
    await advance(FIELD_DEBOUNCE_MS + LOOKUP_DEBOUNCE_MS); // the newer one is asked
    expect(search).toHaveBeenCalledTimes(2);

    await act(async () => {
      finishers[1](candidate('b2', 'FY26 budget')); // the newer answer first
      await jest.advanceTimersByTimeAsync(0);
    });
    expect(screen.getByText('FY26 budget')).toBeInTheDocument();

    await act(async () => {
      finishers[0](candidate('b1', 'FY25 budget')); // the older one, late
      await jest.advanceTimersByTimeAsync(0);
    });
    expect(screen.getByText('FY26 budget')).toBeInTheDocument();
    expect(screen.queryByText('FY25 budget')).toBeNull();
    expect(screen.queryByText('No results found')).toBeNull();
  });

  it('a throttled search (429) says it could not be completed, not "No results found"', async () => {
    const quiet = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const search = jest.fn(async () => response({ detail: 'Too many searches. Wait a moment.' }, false));
    render(<Harness lookup={createLookupTypeAhead(search)} />);

    typeText('fy26');
    await advance(FIELD_DEBOUNCE_MS + LOOKUP_DEBOUNCE_MS);

    expect(search).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('alert')).toHaveTextContent('Search could not be completed. Try again.');
    expect(screen.queryByText('No results found')).toBeNull();
    quiet.mockRestore();
  });

  it('one letter asks the tool nothing and says how many letters are needed, not "No results found"', async () => {
    const search = jest.fn(async () => candidate('b1', 'FY25 budget'));
    render(<Harness lookup={createLookupTypeAhead(search)} />);

    typeText('f');
    await advance(3 * (FIELD_DEBOUNCE_MS + LOOKUP_DEBOUNCE_MS));

    expect(search).not.toHaveBeenCalled();
    expect(screen.getByText('Type at least 2 letters')).toBeInTheDocument();
    expect(screen.queryByText('No results found')).toBeNull();
  });
});
