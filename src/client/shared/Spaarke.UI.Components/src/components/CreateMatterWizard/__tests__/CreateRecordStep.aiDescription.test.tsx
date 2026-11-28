/**
 * CreateRecordStep — "Generate with AI" beside Matter Description keeps the user's text when the draft fails
 * (ontology task 239 review).
 *
 * Pinned:
 *   - must fire: a failed draft (helper reports isFallback) leaves the typed description untouched and shows a short
 *     warning with no raw error text;
 *   - must not fire: a successful draft replaces the description and shows no warning (and clears an earlier warning).
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { IDataService } from '../../../types/serviceInterfaces';
import { CreateRecordStep } from '../CreateRecordStep';
import { AI_DRAFT_UNAVAILABLE_MESSAGE } from '../matterService';
import type { ICreateMatterFormState } from '../formTypes';

const mockDraft = jest.fn();
jest.mock('../matterService', () => {
  const actual = jest.requireActual('../matterService');
  return { ...actual, fetchAiDraftSummary: (...args: unknown[]) => mockDraft(...args) };
});

const dataService = {
  createRecord: jest.fn(),
  retrieveRecord: jest.fn(),
  retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
  updateRecord: jest.fn(),
  deleteRecord: jest.fn(),
} as unknown as IDataService;

const initial = (summary: string): ICreateMatterFormState => ({
  matterTypeId: '',
  matterTypeName: 'Litigation',
  practiceAreaId: '',
  practiceAreaName: 'Employment',
  matterName: 'Acme v. Beta',
  assignedAttorneyId: '',
  assignedAttorneyName: '',
  assignedParalegalId: '',
  assignedParalegalName: '',
  assignedOutsideCounselId: '',
  assignedOutsideCounselName: '',
  summary,
});

function renderStep(summary: string) {
  renderWithProviders(
    <CreateRecordStep
      dataService={dataService}
      uploadedFileNames={[]}
      uploadedFiles={[]}
      onValidChange={jest.fn()}
      onSubmit={jest.fn()}
      initialFormValues={initial(summary)}
      authenticatedFetch={jest.fn() as never}
      bffBaseUrl="https://bff.example.test"
    />
  );
}

const generateButton = () => screen.getByRole('button', { name: /generate description with ai/i });
const descriptionBox = () => screen.getByRole('textbox', { name: 'Matter Description' }) as HTMLTextAreaElement;

describe('CreateRecordStep — Generate with AI failure handling (task 239 review)', () => {
  let spies: jest.SpyInstance[];

  beforeEach(() => {
    mockDraft.mockReset();
    spies = (['info', 'error', 'warn'] as const).map(m => jest.spyOn(console, m).mockImplementation(() => {}));
  });

  afterEach(() => spies.forEach(s => s.mockRestore()));

  it('a failed draft keeps the typed text, shows the warning, and the button recovers', async () => {
    mockDraft.mockResolvedValue({ summary: '', isFallback: true });
    renderStep('My own notes');

    fireEvent.click(generateButton());

    expect(await screen.findByText(AI_DRAFT_UNAVAILABLE_MESSAGE)).toBeInTheDocument();
    expect(descriptionBox()).toHaveValue('My own notes');
    expect(generateButton()).toBeEnabled();
  });

  it('a successful draft replaces the text and shows no warning (and clears an earlier warning)', async () => {
    mockDraft.mockResolvedValueOnce({ summary: '', isFallback: true });
    renderStep('Old text');

    fireEvent.click(generateButton());
    expect(await screen.findByText(AI_DRAFT_UNAVAILABLE_MESSAGE)).toBeInTheDocument();

    mockDraft.mockResolvedValueOnce({ summary: 'Fresh draft.' });
    fireEvent.click(generateButton());

    await waitFor(() => expect(descriptionBox()).toHaveValue('Fresh draft.'));
    expect(screen.queryByText(AI_DRAFT_UNAVAILABLE_MESSAGE)).not.toBeInTheDocument();
  });

  it('shows no warning before anything is generated', () => {
    renderStep('Old text');

    expect(screen.queryByText(AI_DRAFT_UNAVAILABLE_MESSAGE)).not.toBeInTheDocument();
  });
});
