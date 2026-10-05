/**
 * RelatedToPicker compact layout (spaarkeai-word-add-in-r1 task 095, owner UAT round 4, items 2-4):
 * no "Related to" label; the lookup placeholder names the selected type; the search runs from an icon button
 * named "Search" (Enter too); "+ New" shares the pill row with the type pills.
 *
 * Task 099 (owner UAT round 5, items 1, 2, 4): the placeholder carries no icon; selecting a record collapses the
 * picker to just that record and its x restores the search (query + results, no new request); "+ New" shows
 * only the create form and reports `onCreatingChange`; focus follows each of those actions.
 */
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RelatedToPicker } from '../RelatedToPicker';
import type { EntitySearchResult, EntityType } from '../../hooks/useEntitySearch';

function renderPicker(onSearch = jest.fn<Promise<EntitySearchResult[]>, [string, EntityType]>().mockResolvedValue([])) {
  render(
    <FluentProvider theme={webLightTheme}>
      <RelatedToPicker
        value={null}
        onChange={jest.fn()}
        candidates={[]}
        onSearch={onSearch}
        onCreateRecord={jest.fn()}
        allowedTypes={['Matter', 'Project', 'Invoice']}
        defaultType="Matter"
      />
    </FluentProvider>
  );
  return { onSearch };
}

describe('RelatedToPicker — compact layout (task 095)', () => {
  it('has no "Related to" label, and the lookup placeholder follows the selected pill', () => {
    renderPicker();

    expect(screen.queryByText('Related to')).toBeNull();
    expect(screen.getByPlaceholderText('Look up related Matter...')).toBeTruthy();

    fireEvent.click(screen.getByRole('radio', { name: 'Project' }));
    expect(screen.getByPlaceholderText('Look up related Project...')).toBeTruthy();
  });

  it('runs the search from an icon button named "Search"; there is no "Search" text pill', async () => {
    const { onSearch } = renderPicker();

    fireEvent.change(screen.getByPlaceholderText('Look up related Matter...'), { target: { value: 'acme' } });
    const searchButton = screen.getByRole('button', { name: 'Search' });
    expect(searchButton.textContent).toBe('');
    fireEvent.click(searchButton);

    await waitFor(() => expect(onSearch).toHaveBeenCalledWith('acme', 'Matter'));
  });

  it('Enter in the lookup box also runs the search', async () => {
    const { onSearch } = renderPicker();

    const box = screen.getByPlaceholderText('Look up related Matter...');
    fireEvent.change(box, { target: { value: 'beta' } });
    fireEvent.keyDown(box, { key: 'Enter' });

    await waitFor(() => expect(onSearch).toHaveBeenCalledWith('beta', 'Matter'));
  });

  it('puts "+ New" on the pill row (same container as the pills), not on the lookup row', () => {
    renderPicker();

    const pillRow = screen.getByRole('radiogroup').parentElement as HTMLElement;
    const newButton = screen.getByRole('button', { name: 'New' });
    expect(pillRow.contains(newButton)).toBe(true);
    expect(pillRow.contains(screen.getByPlaceholderText('Look up related Matter...'))).toBe(false);
  });

  it('"New" turns the lookup row into the create form (name box, Create, Cancel) and hides the pill-row New', () => {
    renderPicker();

    fireEvent.click(screen.getByRole('button', { name: 'New' }));

    expect(screen.getByLabelText('New Matter name')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Create' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'New' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Search' })).toBeNull();
  });
});

const ACME: EntitySearchResult = {
  id: 'aaaa1111-0000-4000-8000-000000000001',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Acme v. Beta',
  displayInfo: 'MAT-1',
};

function StatefulPicker(props: {
  onSearch: (q: string, t: EntityType) => Promise<EntitySearchResult[]>;
  onCreatingChange?: (creating: boolean) => void;
  onCreateRecord?: () => Promise<{ record: EntitySearchResult } | null>;
}): React.ReactElement {
  const [value, setValue] = React.useState<EntitySearchResult | null>(null);
  return (
    <FluentProvider theme={webLightTheme}>
      <RelatedToPicker
        value={value}
        onChange={setValue}
        candidates={[]}
        onSearch={props.onSearch}
        onCreateRecord={props.onCreateRecord ?? jest.fn()}
        {...(props.onCreatingChange ? { onCreatingChange: props.onCreatingChange } : {})}
        allowedTypes={['Matter', 'Project', 'Invoice']}
        defaultType="Matter"
      />
    </FluentProvider>
  );
}

async function searchAndGetResults(onSearch: jest.Mock): Promise<void> {
  fireEvent.change(screen.getByPlaceholderText('Look up related Matter...'), { target: { value: 'acme' } });
  fireEvent.click(screen.getByRole('button', { name: 'Search' }));
  await waitFor(() => expect(onSearch).toHaveBeenCalled());
  await screen.findByText('MAT-1 : Acme v. Beta');
}

describe('RelatedToPicker — round 5 (task 099)', () => {
  it('item 1: the lookup placeholder has no icon (text only); the Search icon button stays', () => {
    renderPicker();

    const box = screen.getByPlaceholderText('Look up related Matter...');
    expect(box.parentElement?.querySelector('svg')).toBeNull();
    expect(screen.getByRole('button', { name: 'Search' })).toBeTruthy();
  });

  it('item 4: selecting a result collapses the picker to the selected record; its x restores the search, query and results', async () => {
    const onSearch = jest.fn().mockResolvedValue([ACME]);
    render(<StatefulPicker onSearch={onSearch} />);
    await searchAndGetResults(onSearch);

    fireEvent.click(screen.getByRole('button', { name: 'Select this record' }));

    // Only the selected record (with its x): no pills, lookup box, results list or "+ New".
    expect(screen.getAllByText('MAT-1 : Acme v. Beta')).toHaveLength(1);
    expect(screen.queryByRole('radiogroup')).toBeNull();
    expect(screen.queryByPlaceholderText('Look up related Matter...')).toBeNull();
    expect(screen.queryByRole('button', { name: 'New' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Search' })).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Clear selection' }));

    // Back as it was: the previous query and the previous results, with no second request.
    expect(screen.getByRole('radiogroup')).toBeTruthy();
    expect((screen.getByPlaceholderText('Look up related Matter...') as HTMLInputElement).value).toBe('acme');
    expect(screen.getByText('MAT-1 : Acme v. Beta')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Select this record' })).toBeTruthy();
    expect(onSearch).toHaveBeenCalledTimes(1);
  });

  it("item 4 focus: selecting moves focus to the selected record's control; clearing returns it to the lookup box", async () => {
    const onSearch = jest.fn().mockResolvedValue([ACME]);
    render(<StatefulPicker onSearch={onSearch} />);
    await searchAndGetResults(onSearch);

    fireEvent.click(screen.getByRole('button', { name: 'Select this record' }));
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Selected — click to clear' }));

    fireEvent.click(screen.getByRole('button', { name: 'Clear selection' }));
    expect(document.activeElement).toBe(screen.getByPlaceholderText('Look up related Matter...'));
  });

  it('item 2: "+ New" shows only the create form (no results / suggestions) and reports onCreatingChange; Cancel restores the view and focuses the lookup box', async () => {
    const onSearch = jest.fn().mockResolvedValue([ACME]);
    const onCreatingChange = jest.fn();
    render(<StatefulPicker onSearch={onSearch} onCreatingChange={onCreatingChange} />);
    await searchAndGetResults(onSearch);
    expect(onCreatingChange).toHaveBeenLastCalledWith(false);

    fireEvent.click(screen.getByRole('button', { name: 'New' }));

    expect(onCreatingChange).toHaveBeenLastCalledWith(true);
    expect(screen.getByLabelText('New Matter name')).toBeTruthy();
    expect(document.activeElement).toBe(screen.getByLabelText('New Matter name'));
    expect(screen.queryByText('MAT-1 : Acme v. Beta')).toBeNull();
    expect(screen.queryByText(/No suggested Matter matches/)).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onCreatingChange).toHaveBeenLastCalledWith(false);
    expect(screen.getByText('MAT-1 : Acme v. Beta')).toBeTruthy();
    expect(document.activeElement).toBe(screen.getByPlaceholderText('Look up related Matter...'));
  });

  it('item 2: a successful create selects the new record (collapsed), ends the creating state and focuses it', async () => {
    const created: EntitySearchResult = {
      id: 'bbbb2222-0000-4000-8000-000000000002',
      entityType: 'Project',
      logicalName: 'sprk_project',
      name: 'Brand New',
      displayInfo: 'PRJ-2',
    };
    const onCreatingChange = jest.fn();
    const onCreateRecord = jest.fn().mockResolvedValue({ record: created });
    render(
      <StatefulPicker
        onSearch={jest.fn().mockResolvedValue([])}
        onCreatingChange={onCreatingChange}
        onCreateRecord={onCreateRecord}
      />
    );

    // Project (a Matter would also need a Matter Type).
    fireEvent.click(screen.getByRole('radio', { name: 'Project' }));
    fireEvent.click(screen.getByRole('button', { name: 'New' }));
    fireEvent.change(screen.getByLabelText('New Project name'), { target: { value: 'Brand New' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));

    await screen.findByText('PRJ-2 : Brand New');
    expect(onCreatingChange).toHaveBeenLastCalledWith(false);
    expect(screen.queryByRole('radiogroup')).toBeNull();
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Selected — click to clear' }))
    );
  });
});
