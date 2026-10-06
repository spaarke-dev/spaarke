/**
 * spaarkeai-word-add-in-r1 task 038, extended by task 100 (owner UAT round 5 item 3 + decision B): on the pane's
 * "+ New" Matter form, Matter Type AND Practice Area are REQUIRED — the create action is blocked until both are chosen,
 * and the request carries both ids. Project's Project Type is optional; Invoice has neither. A reference list that
 * failed to load shows its message with Retry (never a required field that blocks silently). A non-fatal server
 * warning is shown, never swallowed.
 *
 * These tests exercise `RelatedToPicker` (which hosts `CreateRecordForm`) directly; the sibling
 * `SaveFlow.matterTypeQuickCreate.test.tsx` covers the POST body end to end.
 */
import { render, screen, fireEvent, configure } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RelatedToPicker, type CreateRecordResult, type RelatedToPickerProps } from '../RelatedToPicker';
import type { CreateRecordInput } from '../CreateRecordForm';
import type { EntityType } from '../../hooks/useEntitySearch';
import type { CreateRecordFormData, ReferenceListState } from '../../hooks/useCreateRecordFormData';
import type { ReferenceChoice } from '../../services/referenceListService';

const MATTER_TYPES: ReferenceChoice[] = [
  { id: '11aed095-30da-f011-8406-7ced8d1dc988', name: 'Litigation', code: 'LITG' },
  { id: '46c35aa2-30da-f011-8406-7ced8d1dc988', name: 'Patent', code: 'PAT' },
];
const PRACTICE_AREAS: ReferenceChoice[] = [
  { id: 'b41377db-690e-f111-8342-7c1e520aa4df', name: 'Appellate', code: 'APPL' },
];
const PROJECT_TYPES: ReferenceChoice[] = [{ id: '0ed9d8ac-b018-f111-8343-7ced8d1dc988', name: 'Litigation' }];

const LOAD_ERROR = "Couldn't load matter types. Try again, or search for an existing record instead.";

// Real-timer userEvent typing + Dropdown popups run close to the package's default 10s testTimeout under parallel
// load (documented package characteristic). Every dependency is mocked; no assertion depends on timing.
jest.setTimeout(60000);
configure({ asyncUtilTimeout: 10000 });

function list(options: ReferenceChoice[], over: Partial<ReferenceListState> = {}): ReferenceListState {
  return { options, loading: false, error: null, retry: jest.fn(), ...over };
}

function formData(over: Partial<CreateRecordFormData> = {}): CreateRecordFormData {
  return {
    matterTypes: list(MATTER_TYPES),
    practiceAreas: list(PRACTICE_AREAS),
    projectTypes: list(PROJECT_TYPES),
    defaultAssignee: null,
    ...over,
  };
}

type PickerOverrides = Partial<Pick<RelatedToPickerProps, 'allowedTypes' | 'defaultType' | 'createForm'>> & {
  onCreateRecord: jest.Mock<Promise<CreateRecordResult | null>, [EntityType, CreateRecordInput]>;
};

function pickerElement(props: PickerOverrides, onChange: jest.Mock) {
  return (
    <FluentProvider theme={webLightTheme}>
      <RelatedToPicker
        value={null}
        onChange={onChange}
        candidates={[]}
        onSearch={jest.fn().mockResolvedValue([])}
        onCreateRecord={props.onCreateRecord}
        allowedTypes={props.allowedTypes ?? ['Matter', 'Project', 'Invoice']}
        defaultType={props.defaultType ?? 'Matter'}
        createForm={props.createForm ?? formData()}
      />
    </FluentProvider>
  );
}

function renderPicker(props: PickerOverrides) {
  const onChange = jest.fn();
  const { rerender } = render(pickerElement(props, onChange));
  return {
    onChange,
    rerender: (next: PickerOverrides) => rerender(pickerElement(next, onChange)),
  };
}

async function openCreateForm(name: string) {
  await userEvent.click(screen.getByRole('button', { name: 'New' }));
  const nameInput = screen.getByLabelText(/New .* name/);
  await userEvent.type(nameInput, name);
  return nameInput;
}

async function choose(field: string, option: string) {
  await userEvent.click(screen.getByRole('combobox', { name: field }));
  await userEvent.click(screen.getByRole('option', { name: option }));
}

describe('RelatedToPicker — required Matter Type and Practice Area on Matter quick-create (tasks 038, 100)', () => {
  it('the Create button stays disabled for a Matter until BOTH a type and a practice area are chosen', async () => {
    const onCreateRecord = jest.fn();
    renderPicker({ onCreateRecord });

    await openCreateForm('New Matter Name');
    expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled();

    await choose('Matter Type', 'Litigation');
    expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled();

    await choose('Practice Area', 'Appellate');
    expect(screen.getByRole('button', { name: 'Create' })).toBeEnabled();
    expect(onCreateRecord).not.toHaveBeenCalled();
  });

  it('pressing Enter in the name field with neither chosen shows a labelled, announced error per field and does not create', async () => {
    const onCreateRecord = jest.fn();
    renderPicker({ onCreateRecord });

    const nameInput = await openCreateForm('New Matter Name');
    fireEvent.keyDown(nameInput, { key: 'Enter' });

    expect(await screen.findByText('Choose a Matter Type before creating a Matter.')).toHaveAttribute('role', 'alert');
    expect(screen.getByText('Choose a Practice Area before creating a Matter.')).toHaveAttribute('role', 'alert');
    expect(onCreateRecord).not.toHaveBeenCalled();
  });

  it('choosing both and creating sends the exact ids to onCreateRecord, and selects the result', async () => {
    const created: CreateRecordResult = {
      record: { id: 'm-1', entityType: 'Matter', logicalName: 'sprk_matter', name: 'New Matter Name' },
    };
    const onCreateRecord = jest.fn().mockResolvedValue(created);
    const { onChange } = renderPicker({ onCreateRecord });

    await openCreateForm('New Matter Name');
    await choose('Matter Type', 'Litigation');
    await choose('Practice Area', 'Appellate');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(onCreateRecord).toHaveBeenCalledWith('Matter', {
      name: 'New Matter Name',
      matterTypeId: '11aed095-30da-f011-8406-7ced8d1dc988',
      practiceAreaId: 'b41377db-690e-f111-8342-7c1e520aa4df',
    });
    expect(onChange).toHaveBeenCalledWith(created.record);
  });

  it('a non-fatal server warning is shown as a non-blocking notice, and the record is still selected', async () => {
    const created: CreateRecordResult = {
      record: { id: 'm-2', entityType: 'Matter', logicalName: 'sprk_matter', name: 'New Matter Name' },
      warnings: [
        'The selected practice area could not be checked just now, so the matter was created without a practice area.',
      ],
    };
    const onCreateRecord = jest.fn().mockResolvedValue(created);
    const { onChange } = renderPicker({ onCreateRecord });

    await openCreateForm('New Matter Name');
    await choose('Matter Type', 'Litigation');
    await choose('Practice Area', 'Appellate');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(onChange).toHaveBeenCalledWith(created.record);
    const warning = await screen.findByText(created.warnings![0]!);
    expect(warning).toHaveAttribute('role', 'status');
  });

  it('Project quick-create renders no Matter Type or Practice Area, an OPTIONAL Project Type, and sends it when chosen', async () => {
    const created: CreateRecordResult = {
      record: { id: 'p-1', entityType: 'Project', logicalName: 'sprk_project', name: 'New Project Name' },
    };
    const onCreateRecord = jest.fn().mockResolvedValue(created);
    renderPicker({ onCreateRecord, defaultType: 'Project' });

    await openCreateForm('New Project Name');

    expect(screen.queryByText('Matter Type')).toBeNull();
    expect(screen.queryByText('Practice Area')).toBeNull();
    expect(screen.getByRole('button', { name: 'Create' })).toBeEnabled(); // Project Type is optional

    await choose('Project Type', 'Litigation');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(onCreateRecord).toHaveBeenCalledWith('Project', {
      name: 'New Project Name',
      projectTypeId: '0ed9d8ac-b018-f111-8343-7ced8d1dc988',
    });
  });

  it('Invoice quick-create has no reference fields at all', async () => {
    const onCreateRecord = jest.fn().mockResolvedValue({
      record: { id: 'i-1', entityType: 'Invoice', logicalName: 'sprk_invoice', name: 'INV-1' },
    });
    renderPicker({ onCreateRecord, defaultType: 'Invoice' });

    await openCreateForm('INV-1');

    expect(screen.queryByRole('combobox')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));
    expect(onCreateRecord).toHaveBeenCalledWith('Invoice', { name: 'INV-1' });
  });
});

describe('RelatedToPicker — a failed reference-list load is recoverable, not a dead end (task 038 coordinator fix)', () => {
  it('a failed load shows the error and a Retry action, and Create stays disabled', async () => {
    const onCreateRecord = jest.fn();
    const retry = jest.fn();
    renderPicker({
      onCreateRecord,
      createForm: formData({ matterTypes: list([], { error: LOAD_ERROR, retry }) }),
    });

    await openCreateForm('New Matter Name');

    const error = screen.getByText(LOAD_ERROR);
    expect(error).toHaveAttribute('role', 'alert');
    // The genuine-empty-table note must NOT also show — the two states are mutually exclusive.
    expect(screen.queryByText('No matter types are available right now.')).toBeNull();

    expect(screen.getByRole('combobox', { name: 'Matter Type' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(retry).toHaveBeenCalledTimes(1);
    expect(onCreateRecord).not.toHaveBeenCalled();
  });

  it('a failed practice-area load gets its own message and Retry', async () => {
    const retry = jest.fn();
    const message = "Couldn't load practice areas. Try again, or search for an existing record instead.";
    renderPicker({
      onCreateRecord: jest.fn(),
      createForm: formData({ practiceAreas: list([], { error: message, retry }) }),
    });

    await openCreateForm('New Matter Name');

    expect(screen.getByText(message)).toHaveAttribute('role', 'alert');
    expect(screen.getByRole('combobox', { name: 'Practice Area' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(retry).toHaveBeenCalledTimes(1);
  });

  it('while loading the field shows a loading state, never an empty-looking final dropdown, and no Retry', async () => {
    renderPicker({
      onCreateRecord: jest.fn(),
      createForm: formData({ matterTypes: list([], { loading: true }) }),
    });

    await openCreateForm('New Matter Name');

    expect(screen.getByRole('combobox', { name: 'Matter Type' })).toHaveTextContent('Loading matter types…');
    expect(screen.getByRole('combobox', { name: 'Matter Type' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
    expect(screen.queryByText('No matter types are available right now.')).toBeNull();
  });

  it('a Retry that succeeds lets the user choose and create — the field recovers fully', async () => {
    const created: CreateRecordResult = {
      record: { id: 'm-3', entityType: 'Matter', logicalName: 'sprk_matter', name: 'New Matter Name' },
    };
    const onCreateRecord = jest.fn().mockResolvedValue(created);
    const retry = jest.fn();
    const { onChange, rerender } = renderPicker({
      onCreateRecord,
      createForm: formData({ matterTypes: list([], { error: LOAD_ERROR, retry }) }),
    });

    await openCreateForm('New Matter Name');
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(retry).toHaveBeenCalledTimes(1);

    // The host re-fetches: loading, then success — simulated as the prop transitions a real retry drives.
    rerender({ onCreateRecord, createForm: formData({ matterTypes: list([], { loading: true }) }) });
    expect(screen.getByRole('combobox', { name: 'Matter Type' })).toBeDisabled();
    rerender({ onCreateRecord, createForm: formData() });

    expect(screen.queryByText(LOAD_ERROR)).toBeNull();
    await choose('Matter Type', 'Litigation');
    await choose('Practice Area', 'Appellate');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(onCreateRecord).toHaveBeenCalledWith('Matter', {
      name: 'New Matter Name',
      matterTypeId: '11aed095-30da-f011-8406-7ced8d1dc988',
      practiceAreaId: 'b41377db-690e-f111-8342-7c1e520aa4df',
    });
    expect(onChange).toHaveBeenCalledWith(created.record);
  });

  it('Project quick-create is unaffected by a matter-types load failure', async () => {
    const onCreateRecord = jest.fn().mockResolvedValue({
      record: { id: 'p-2', entityType: 'Project', logicalName: 'sprk_project', name: 'New Project Name' },
    });
    renderPicker({
      onCreateRecord,
      defaultType: 'Project',
      createForm: formData({ matterTypes: list([], { error: LOAD_ERROR }) }),
    });

    await openCreateForm('New Project Name');

    expect(screen.queryByText(LOAD_ERROR)).toBeNull();
    expect(screen.getByRole('button', { name: 'Create' })).toBeEnabled();
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));
    expect(onCreateRecord).toHaveBeenCalledWith('Project', { name: 'New Project Name' });
  });
});
