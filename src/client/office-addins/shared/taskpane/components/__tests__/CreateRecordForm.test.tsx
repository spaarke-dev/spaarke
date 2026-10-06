/**
 * spaarkeai-word-add-in-r1 task 100 (owner UAT round 5 item 3, decisions B and C): the "+ New" form.
 *
 * Pins:
 *  - exactly the listed fields per type, in the listed order (Matter: Name*, Description, Matter Type*, Practice
 *    Area*, Assigned To Internal; Project: Name*, Project Type, Description, Assigned To Internal; Invoice: Name*,
 *    Description, Assigned To);
 *  - the Assigned To picker is the REAL shared `LookupField`, by exact alias (never a copy, never the barrel);
 *  - Assigned To is prefilled with the server's default and optional: clearing it sends nothing (= the server's
 *    default, which on a Matter/Project is the user — the form says so), choosing someone sends their id, and a late
 *    prefill never overwrites the user's choice;
 *  - every value entered is submitted (description trimmed; empty values omitted).
 */
import { render, screen, configure, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { CreateRecordForm, type CreateRecordFormProps, type CreateRecordInput } from '../CreateRecordForm';
import type { ReferenceListState } from '../../hooks/useCreateRecordFormData';
import type { ContactOption } from '../views/CreateTodoView';

jest.setTimeout(60000);
configure({ asyncUtilTimeout: 10000 });

const list = (options: { id: string; name: string }[]): ReferenceListState => ({
  options,
  loading: false,
  error: null,
  retry: jest.fn(),
});

const ME = { id: 'eeeeeeee-1000-4000-8000-000000000100', name: 'Ralph Schroeder', email: 'ralph@spaarke.test' };
const COLLEAGUE: ContactOption = {
  id: 'cccccccc-1000-4000-8000-000000000100',
  name: 'Jane Cooper',
  email: 'jane@acme.test',
};

function renderForm(over: Partial<CreateRecordFormProps> = {}) {
  const onSubmit = jest.fn<Promise<void>, [CreateRecordInput]>().mockResolvedValue(undefined);
  const props: CreateRecordFormProps = {
    type: 'Matter',
    matterTypes: list([{ id: 'mt-1', name: 'Litigation' }]),
    practiceAreas: list([{ id: 'pa-1', name: 'Appellate' }]),
    projectTypes: list([{ id: 'pt-1', name: 'Due Diligence' }]),
    defaultAssignee: ME,
    onSearchContacts: jest.fn().mockResolvedValue([COLLEAGUE]),
    onSubmit,
    onCancel: jest.fn(),
    ...over,
  };
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <CreateRecordForm {...props} />
    </FluentProvider>
  );
  return {
    ...utils,
    onSubmit,
    rerenderWith: (next: Partial<CreateRecordFormProps>) =>
      utils.rerender(
        <FluentProvider theme={webLightTheme}>
          <CreateRecordForm {...props} {...next} />
        </FluentProvider>
      ),
  };
}

/** The visible field labels, top to bottom (the form is one column). */
function fieldOrder(): string[] {
  const wanted = [
    'Name',
    'Description',
    'Matter Type',
    'Practice Area',
    'Project Type',
    'Assigned To Internal',
    'Assigned To',
  ];
  return Array.from(document.querySelectorAll('label'))
    .map(l => (l.textContent ?? '').replace('*', '').trim())
    .filter(t => wanted.includes(t));
}

async function choose(field: string, option: string) {
  await userEvent.click(screen.getByRole('combobox', { name: field }));
  await userEvent.click(screen.getByRole('option', { name: option }));
}

describe('CreateRecordForm — the listed fields per type (task 100)', () => {
  it('the Assigned To picker is the shared LookupField source, by exact alias — not a copy', () => {
    expect(require.resolve('@spaarke/ui-components/lookup-field').replace(/\\/g, '/')).toMatch(
      /\/shared\/Spaarke\.UI\.Components\/src\/components\/LookupField\/LookupField\.tsx$/
    );
  });

  it('Matter: Name*, Description, Matter Type*, Practice Area*, Assigned To Internal', () => {
    renderForm({ type: 'Matter' });
    expect(fieldOrder()).toEqual(['Name', 'Description', 'Matter Type', 'Practice Area', 'Assigned To Internal']);
    expect(screen.getByRole('combobox', { name: 'Matter Type' })).toHaveAttribute('aria-required', 'true');
    expect(screen.getByRole('combobox', { name: 'Practice Area' })).toHaveAttribute('aria-required', 'true');
  });

  it('Project: Name*, Project Type (optional), Description, Assigned To Internal', () => {
    renderForm({ type: 'Project' });
    expect(fieldOrder()).toEqual(['Name', 'Project Type', 'Description', 'Assigned To Internal']);
    expect(screen.getByRole('combobox', { name: 'Project Type' })).toHaveAttribute('aria-required', 'false');
  });

  it('Invoice: Name*, Description, Assigned To', () => {
    renderForm({ type: 'Invoice' });
    expect(fieldOrder()).toEqual(['Name', 'Description', 'Assigned To']);
  });

  it('Matter: every value entered is submitted — name and description trimmed, both ids, the prefilled assignee', async () => {
    const { onSubmit } = renderForm({ type: 'Matter' });

    await userEvent.type(screen.getByLabelText('New Matter name'), '  Acme v. Globex  ');
    await userEvent.type(screen.getByLabelText('Description'), '  From Word  ');
    await choose('Matter Type', 'Litigation');
    await choose('Practice Area', 'Appellate');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Acme v. Globex',
      description: 'From Word',
      matterTypeId: 'mt-1',
      practiceAreaId: 'pa-1',
      assignedToContactId: ME.id,
    });
  });
});

describe('CreateRecordForm — Assigned To: prefilled, optional (owner decision B)', () => {
  it('shows the prefill; clearing it submits NO assignee and says the server will assign the user', async () => {
    const { onSubmit } = renderForm({ type: 'Project' });

    expect(screen.getByText('Ralph Schroeder (ralph@spaarke.test)')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Clear Assigned To Internal' }));
    expect(screen.getByText('Left empty, it is assigned to you (Ralph Schroeder).')).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('New Project name'), 'Due Diligence');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(onSubmit).toHaveBeenCalledWith({ name: 'Due Diligence' });
  });

  it('an Invoice cleared is simply unassigned — no "assigned to you" note (an invoice has no server default)', async () => {
    renderForm({ type: 'Invoice' });

    await userEvent.click(screen.getByRole('button', { name: 'Clear Assigned To' }));

    expect(screen.queryByText(/assigned to you/)).toBeNull();
  });

  it('choosing someone else from the contact search submits their id', async () => {
    const onSearchContacts = jest.fn().mockResolvedValue([COLLEAGUE]);
    const { onSubmit } = renderForm({ type: 'Invoice', onSearchContacts });

    await userEvent.click(screen.getByRole('button', { name: 'Clear Assigned To' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Assigned To' }), 'Ja');
    await userEvent.click(await screen.findByText('Jane Cooper (jane@acme.test)'));
    await userEvent.type(screen.getByLabelText('New Invoice name'), 'INV-1');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(onSearchContacts).toHaveBeenCalledWith('Ja');
    expect(onSubmit).toHaveBeenCalledWith({ name: 'INV-1', assignedToContactId: COLLEAGUE.id });
  });

  it('no linked contact → the field starts empty, and a prefill that arrives later fills it', async () => {
    const { rerenderWith } = renderForm({ type: 'Matter', defaultAssignee: null });
    expect(screen.queryByText(/Ralph Schroeder/)).toBeNull();

    rerenderWith({ defaultAssignee: ME });

    await waitFor(() => expect(screen.getByText('Ralph Schroeder (ralph@spaarke.test)')).toBeInTheDocument());
  });

  it('a prefill that arrives AFTER the user cleared the field never overwrites that choice', async () => {
    const { rerenderWith } = renderForm({ type: 'Project', defaultAssignee: ME });
    await userEvent.click(screen.getByRole('button', { name: 'Clear Assigned To Internal' }));

    rerenderWith({ defaultAssignee: { ...ME, name: 'Ralph S.' } });

    expect(screen.queryByText(/Ralph S\. \(/)).toBeNull();
    expect(screen.getByRole('textbox', { name: 'Assigned To Internal' })).toHaveValue('');
  });
});

describe('CreateRecordForm — submit and failure', () => {
  it('a thrown server message is shown (role="alert") and the form stays usable', async () => {
    const onSubmit = jest
      .fn()
      .mockRejectedValue(new Error('The person chosen in Assigned To is not available to you.'));
    renderForm({ type: 'Invoice', onSubmit });

    await userEvent.type(screen.getByLabelText('New Invoice name'), 'INV-2');
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(await screen.findByText('The person chosen in Assigned To is not available to you.')).toHaveAttribute(
      'role',
      'alert'
    );
    expect(screen.getByRole('button', { name: 'Create' })).toBeEnabled();
  });

  it('Cancel is left of Create (the Save tab footer pattern)', () => {
    renderForm({ type: 'Invoice' });

    const cancel = screen.getByRole('button', { name: 'Cancel' });
    const create = screen.getByRole('button', { name: 'Create' });
    expect(cancel.compareDocumentPosition(create) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('a type change keeps Name/Description/Assigned To and drops Matter Type / Practice Area (task 101)', async () => {
    const { onSubmit, rerenderWith } = renderForm({ type: 'Matter' });
    await userEvent.type(screen.getByLabelText('New Matter name'), 'Acme');
    await userEvent.type(screen.getByLabelText('Description'), 'From Word');
    await choose('Matter Type', 'Litigation');
    await choose('Practice Area', 'Appellate');

    rerenderWith({ type: 'Project' });
    await userEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Acme',
      description: 'From Word',
      assignedToContactId: ME.id,
    });
  });

  it('Cancel calls onCancel and submits nothing', async () => {
    const onCancel = jest.fn();
    const { onSubmit } = renderForm({ type: 'Invoice', onCancel });

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onSubmit).not.toHaveBeenCalled();
  });
});
