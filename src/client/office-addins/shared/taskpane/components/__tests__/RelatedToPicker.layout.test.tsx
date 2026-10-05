/**
 * RelatedToPicker compact layout (spaarkeai-word-add-in-r1 task 095, owner UAT round 4, items 2-4):
 * no "Related to" label; the lookup placeholder names the selected type; the search runs from an icon button
 * named "Search" (Enter too); "+ New" shares the pill row with the type pills.
 */
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
