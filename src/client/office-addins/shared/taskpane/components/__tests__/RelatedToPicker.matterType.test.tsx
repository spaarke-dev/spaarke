/**
 * spaarkeai-word-add-in-r1 task 038: Matter Type is a REQUIRED field on the pane's Matter quick-create
 * (owner decision 2026-09-11) — the create action is blocked until one is chosen, and the request
 * always carries the chosen id. Project/Invoice quick-create is unchanged (no Matter Type field, no
 * `matterTypeId` sent). A non-fatal server warning is shown, never swallowed.
 *
 * These tests exercise `RelatedToPicker` directly — the component that actually owns the inline
 * "New <type>" create form (`onCreateRecord`) — rather than the full `SaveFlow` tree, which the sibling
 * `SaveFlow.matterTypeQuickCreate.test.tsx` covers end to end for the POST body shape.
 */
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RelatedToPicker, type CreateRecordResult } from '../RelatedToPicker';
import type { EntityType } from '../../hooks/useEntitySearch';
import type { MatterTypeChoice } from '../../services/matterTypeLookupService';

const MATTER_TYPES: MatterTypeChoice[] = [
  { id: '11aed095-30da-f011-8406-7ced8d1dc988', name: 'Litigation', code: 'LITG' },
  { id: '46c35aa2-30da-f011-8406-7ced8d1dc988', name: 'Patent', code: 'PAT' },
];

// jsdom has no ResizeObserver; Fluent's Dropdown popup needs one to render its listbox.
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
Object.defineProperty(window, 'ResizeObserver', { configurable: true, writable: true, value: ResizeObserverStub });

// Real-timer userEvent typing + a Dropdown popup interaction runs close to the package's default 10s
// testTimeout, especially under parallel-worker load (documented package characteristic — CLAUDE.md
// "run-to-run count drift... bail at different points under load"). A longer budget for every test in
// this file avoids that flakiness without touching shared jest config.
jest.setTimeout(20000);

function renderPicker(props: {
  onCreateRecord: jest.Mock<Promise<CreateRecordResult | null>, [EntityType, string, string?]>;
  allowedTypes?: EntityType[];
  defaultType?: EntityType;
  matterTypeOptions?: MatterTypeChoice[];
}) {
  const onChange = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <RelatedToPicker
        value={null}
        onChange={onChange}
        candidates={[]}
        onSearch={jest.fn().mockResolvedValue([])}
        onCreateRecord={props.onCreateRecord}
        allowedTypes={props.allowedTypes ?? ['Matter', 'Project', 'Invoice']}
        defaultType={props.defaultType ?? 'Matter'}
        matterTypeOptions={props.matterTypeOptions ?? MATTER_TYPES}
      />
    </FluentProvider>
  );
  return { onChange };
}

async function openCreateForm(name: string) {
  await userEvent.click(screen.getByRole('button', { name: 'New' }));
  const nameInput = screen.getByLabelText(/New .* name/);
  await userEvent.type(nameInput, name);
  return nameInput;
}

describe('RelatedToPicker — required Matter Type on Matter quick-create (task 038)', () => {
  it('the Create button stays disabled for a Matter until a type is chosen', async () => {
    const onCreateRecord = jest.fn();
    renderPicker({ onCreateRecord });

    await openCreateForm('New Matter Name');

    expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled();
    expect(onCreateRecord).not.toHaveBeenCalled();
  });

  it('pressing Enter in the name field without a chosen type shows a labelled, announced field error and does not create', async () => {
    const onCreateRecord = jest.fn();
    renderPicker({ onCreateRecord });

    const nameInput = await openCreateForm('New Matter Name');
    fireEvent.keyDown(nameInput, { key: 'Enter' });

    const error = await screen.findByText('Choose a Matter Type before creating a Matter.');
    expect(error).toHaveAttribute('role', 'alert');
    expect(onCreateRecord).not.toHaveBeenCalled();
  });

  it('choosing a Matter Type and creating sends the exact matterTypeId to onCreateRecord, and selects the result', async () => {
    const created: CreateRecordResult = {
      record: { id: 'm-1', entityType: 'Matter', logicalName: 'sprk_matter', name: 'New Matter Name' },
    };
    const onCreateRecord = jest.fn().mockResolvedValue(created);
    const { onChange } = renderPicker({ onCreateRecord });

    await openCreateForm('New Matter Name');

    await userEvent.click(screen.getByRole('combobox', { name: 'Matter Type' }));
    await userEvent.click(screen.getByRole('option', { name: 'Litigation' }));
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(onCreateRecord).toHaveBeenCalledWith('Matter', 'New Matter Name', '11aed095-30da-f011-8406-7ced8d1dc988');
    expect(onChange).toHaveBeenCalledWith(created.record);
  });

  it('a non-fatal server warning is shown as a non-blocking notice, and the record is still selected', async () => {
    const created: CreateRecordResult = {
      record: { id: 'm-2', entityType: 'Matter', logicalName: 'sprk_matter', name: 'New Matter Name' },
      warnings: ['The selected matter type could not be checked; the matter was created without it.'],
    };
    const onCreateRecord = jest.fn().mockResolvedValue(created);
    const { onChange } = renderPicker({ onCreateRecord });

    await openCreateForm('New Matter Name');
    await userEvent.click(screen.getByRole('combobox', { name: 'Matter Type' }));
    await userEvent.click(screen.getByRole('option', { name: 'Litigation' }));
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(onChange).toHaveBeenCalledWith(created.record);
    const warning = await screen.findByText(
      'The selected matter type could not be checked; the matter was created without it.'
    );
    expect(warning).toHaveAttribute('role', 'status');
  });

  it('Project quick-create renders no Matter Type field and sends no matterTypeId', async () => {
    const created: CreateRecordResult = {
      record: { id: 'p-1', entityType: 'Project', logicalName: 'sprk_project', name: 'New Project Name' },
    };
    const onCreateRecord = jest.fn().mockResolvedValue(created);
    renderPicker({ onCreateRecord, defaultType: 'Project' });

    await openCreateForm('New Project Name');

    expect(screen.queryByText('Matter Type')).toBeNull();
    expect(screen.getByRole('button', { name: 'Create' })).toBeEnabled();

    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(onCreateRecord).toHaveBeenCalledWith('Project', 'New Project Name', undefined);
  });
});
