/**
 * GitHub #1571 review F3 (spaarkeai-word-add-in-r1 task 136, fix round 1): the "+ New" form's Assigned To picker (the
 * REAL shared `LookupField`, by exact alias) says "Could not search contacts" when the contact search FAILED — never
 * "No results found", which is what an empty match looks like.
 *
 * Same harness as CreateRecordForm.test.tsx.
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

describe('CreateRecordForm — a failed contact search says so (#1571 review F3)', () => {
  it('a rejected search shows "Could not search contacts", not "No results found"', async () => {
    renderForm({ onSearchContacts: jest.fn().mockRejectedValue(new Error('Could not search contacts. Forbidden.')) });
    jest.spyOn(console, 'error').mockImplementation(() => undefined);

    await userEvent.click(screen.getByRole('button', { name: 'Clear Assigned To Internal' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Assigned To Internal' }), 'Ja');

    expect(await screen.findByText('Could not search contacts. Try again.')).toBeInTheDocument();
    expect(screen.queryByText('No results found')).not.toBeInTheDocument();
  });

  it('NEGATIVE: a search that matches nothing still says "No results found"', async () => {
    renderForm({ onSearchContacts: jest.fn().mockResolvedValue([]) });

    await userEvent.click(screen.getByRole('button', { name: 'Clear Assigned To Internal' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Assigned To Internal' }), 'Zz');

    await waitFor(() => expect(screen.getByText('No results found')).toBeInTheDocument());
    expect(screen.queryByText('Could not search contacts. Try again.')).not.toBeInTheDocument();
  });
});
