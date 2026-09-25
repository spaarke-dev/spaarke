/**
 * Task 053 (spaarkeai-word-add-in-r1): `SaveFlow.tsx` used to discard every non-OK response from the two
 * fetches behind `RelatedToPicker`'s "New <type>" create form and its "Look up another record" search —
 * `if (!res.ok) return null` / `return []` — so a refused quick-create (e.g. task 031's load-bearing
 * Project/Matter owner 403) rendered as a generic "Couldn't create the {type}." and a failed search
 * rendered identically to zero matches. `SaveFlow.tsx` now THROWS a descriptive Error on failure instead
 * (see `SaveFlow.quickCreateErrorSurfacing.test.tsx` for that fetch→message plumbing, end to end); these
 * tests pin `RelatedToPicker`'s side of the contract directly — the component that actually owns both
 * forms — by mocking `onCreateRecord`/`onSearch` to reject, exactly as the sibling
 * `RelatedToPicker.matterType.test.tsx` mocks them to resolve.
 *
 * NEW FILE — ADR-038 (new tests in new files); `RelatedToPicker.matterType.test.tsx` is the harness
 * precedent this file's `pickerElement`/`renderPicker`/`openCreateForm` mirror.
 */
import { render, screen, configure } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RelatedToPicker, type CreateRecordResult, type RelatedToPickerProps } from '../RelatedToPicker';
import type { EntitySearchResult, EntityType } from '../../hooks/useEntitySearch';
import type { MatterTypeChoice } from '../../services/matterTypeLookupService';

const MATTER_TYPES: MatterTypeChoice[] = [
  { id: '11aed095-30da-f011-8406-7ced8d1dc988', name: 'Litigation', code: 'LITG' },
];

// ResizeObserver (Fluent v9 MessageBar/Dropdown reflow) is polyfilled globally in jest.setup.js
// (task 071) — removed the per-file copy that used to live here.

// Same budget rationale as RelatedToPicker.matterType.test.tsx: real-timer userEvent typing under
// parallel-worker load can approach the package's default 10s testTimeout. Every dependency here is
// mocked; no assertion depends on timing.
jest.setTimeout(60000);
configure({ asyncUtilTimeout: 10000 });

type PickerOverrides = Partial<Pick<RelatedToPickerProps, 'allowedTypes' | 'defaultType' | 'matterTypeOptions'>> & {
  onCreateRecord?: jest.Mock<Promise<CreateRecordResult | null>, [EntityType, string, string?]>;
  onSearch?: jest.Mock<Promise<EntitySearchResult[]>, [string, EntityType]>;
};

function pickerElement(props: PickerOverrides, onChange: jest.Mock) {
  return (
    <FluentProvider theme={webLightTheme}>
      <RelatedToPicker
        value={null}
        onChange={onChange}
        candidates={[]}
        onSearch={props.onSearch ?? jest.fn().mockResolvedValue([])}
        {...(props.onCreateRecord ? { onCreateRecord: props.onCreateRecord } : {})}
        allowedTypes={props.allowedTypes ?? ['Matter', 'Project', 'Invoice']}
        defaultType={props.defaultType ?? 'Project'}
        matterTypeOptions={props.matterTypeOptions ?? MATTER_TYPES}
      />
    </FluentProvider>
  );
}

function renderPicker(props: PickerOverrides) {
  const onChange = jest.fn();
  render(pickerElement(props, onChange));
  return { onChange };
}

async function openCreateForm(name: string) {
  await userEvent.click(screen.getByRole('button', { name: 'New' }));
  const nameInput = screen.getByLabelText(/New .* name/);
  await userEvent.type(nameInput, name);
  return nameInput;
}

describe('RelatedToPicker — Create surfaces the thrown error message, not a generic one (task 053)', () => {
  it('shows the SERVER-sourced message a rejected onCreateRecord throws (e.g. task 031 owner_unresolved)', async () => {
    const serverDetail =
      'Your account could not be matched to a Dataverse user, so the new project could not be assigned ' +
      'to you and was not created. Ask an administrator to check that your user is provisioned in this environment.';
    const onCreateRecord = jest.fn().mockRejectedValue(new Error(serverDetail));
    const { onChange } = renderPicker({ onCreateRecord, defaultType: 'Project' });

    await openCreateForm('Acme Expansion');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    const error = await screen.findByText(serverDetail);
    expect(error).toHaveAttribute('role', 'alert');
    // The old generic fallback must NOT be what the user sees for a server-explained failure.
    expect(screen.queryByText("Couldn't create the Project.")).toBeNull();
    expect(onChange).not.toHaveBeenCalled();
  });

  it('the pane is not left unchanged with no feedback: creating stops and the form is still usable', async () => {
    const onCreateRecord = jest.fn().mockRejectedValue(new Error('Ask an administrator to check your account.'));
    renderPicker({ onCreateRecord, defaultType: 'Project' });

    await openCreateForm('Acme Expansion');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    await screen.findByText('Ask an administrator to check your account.');
    // Create is clickable again (not stuck disabled/spinning) — the user has a way forward.
    expect(screen.getByRole('button', { name: 'Create' })).toBeEnabled();
  });

  it('falls back to a generic message only when something threw a non-Error value', async () => {
    // eslint-disable-next-line prefer-promise-reject-errors -- deliberately a non-Error rejection
    const onCreateRecord = jest.fn().mockRejectedValue('not an Error instance');
    renderPicker({ onCreateRecord, defaultType: 'Invoice' });

    await openCreateForm('New Invoice Name');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    const error = await screen.findByText("Couldn't create the Invoice.");
    expect(error).toHaveAttribute('role', 'alert');
  });

  it('a resolved null (the pre-053 contract) still shows a message — defensive, not a silent no-op', async () => {
    const onCreateRecord = jest.fn().mockResolvedValue(null);
    renderPicker({ onCreateRecord, defaultType: 'Invoice' });

    await openCreateForm('New Invoice Name');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(await screen.findByText("Couldn't create the Invoice.")).toHaveAttribute('role', 'alert');
  });
});

describe('RelatedToPicker — a failed search is visibly distinct from "no matches" (task 053)', () => {
  it('a rejected onSearch shows an error + Retry, not a silent empty list', async () => {
    const onSearch = jest
      .fn()
      .mockRejectedValue(new Error("Couldn't reach Spaarke to search. Check your connection and try again."));
    renderPicker({ onSearch, defaultType: 'Project' });

    await userEvent.type(screen.getByLabelText('Search Project records'), 'Acme');
    await userEvent.click(screen.getByRole('button', { name: 'Search' }));

    const error = await screen.findByText("Couldn't reach Spaarke to search. Check your connection and try again.");
    expect(error).toHaveAttribute('role', 'alert');
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('Retry re-runs the SAME query against onSearch', async () => {
    const onSearch = jest
      .fn()
      .mockRejectedValueOnce(new Error('Search failed.'))
      .mockResolvedValueOnce([
        { id: 'p-1', entityType: 'Project', logicalName: 'sprk_project', name: 'Acme Expansion' },
      ]);
    renderPicker({ onSearch, defaultType: 'Project' });

    await userEvent.type(screen.getByLabelText('Search Project records'), 'Acme');
    await userEvent.click(screen.getByRole('button', { name: 'Search' }));
    await screen.findByText('Search failed.');

    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(onSearch).toHaveBeenCalledTimes(2);
    expect(onSearch).toHaveBeenNthCalledWith(2, 'Acme', 'Project');
    await screen.findByText('Acme Expansion');
    // The error clears once the retry succeeds — failure and success never show at once.
    expect(screen.queryByText('Search failed.')).toBeNull();
  });

  it('a genuinely empty (resolved []) search shows NO error banner — failure and "no matches" are distinct states', async () => {
    const onSearch = jest.fn().mockResolvedValue([]);
    renderPicker({ onSearch, defaultType: 'Project' });

    await userEvent.type(screen.getByLabelText('Search Project records'), 'Nonexistent');
    await userEvent.click(screen.getByRole('button', { name: 'Search' }));

    // Give the (resolved, not rejected) promise a tick to settle.
    await screen.findByRole('button', { name: 'Search' });
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
    expect(onSearch).toHaveBeenCalledTimes(1);
  });

  it('switching the type chip clears a lingering search error', async () => {
    const onSearch = jest.fn().mockRejectedValue(new Error('Search failed.'));
    renderPicker({ onSearch, defaultType: 'Project', allowedTypes: ['Matter', 'Project', 'Invoice'] });

    await userEvent.type(screen.getByLabelText('Search Project records'), 'Acme');
    await userEvent.click(screen.getByRole('button', { name: 'Search' }));
    await screen.findByText('Search failed.');

    await userEvent.click(screen.getByRole('radio', { name: 'Matter' }));

    expect(screen.queryByText('Search failed.')).toBeNull();
  });
});
