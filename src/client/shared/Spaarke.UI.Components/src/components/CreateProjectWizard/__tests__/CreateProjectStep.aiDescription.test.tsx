/**
 * CreateProjectStep — "Generate with AI" beside Project Description (ontology task 239, owner UAT round 4 A1).
 *
 * Pinned (mirrors Create Matter's button):
 *   - the button is disabled until the Project Name has text, and enabled after;
 *   - clicking it asks the shared helper for a project draft (name, type, practice area) and puts the draft in the
 *     description, which the user can then edit;
 *   - a failed draft (helper reports isFallback, or a real 429) keeps the user's text and shows a short warning, and
 *     the button recovers; a success replaces the text with no warning;
 *   - a host that passes no authenticated fetch / BFF base url gets no button;
 *   - while it runs the button reads "Generating…" and is disabled.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { IDataService } from '../../../types/serviceInterfaces';
import { CreateProjectStep } from '../CreateProjectStep';
import { AI_DRAFT_UNAVAILABLE_MESSAGE } from '../../CreateMatterWizard/matterService';
import { EMPTY_PROJECT_FORM } from '../projectFormTypes';

const mockStream = jest.fn();
jest.mock('../../CreateMatterWizard/matterService', () => {
  const actual = jest.requireActual('../../CreateMatterWizard/matterService');
  return { ...actual, streamAiDraftDescription: (...args: unknown[]) => mockStream(...args) };
});

const dataService = {
  createRecord: jest.fn(),
  retrieveRecord: jest.fn(),
  retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
  updateRecord: jest.fn(),
  deleteRecord: jest.fn(),
} as unknown as IDataService;

const authFetch = jest.fn() as unknown as typeof fetch;
const BFF = 'https://bff.example.test';

function renderStep(
  initial: Partial<typeof EMPTY_PROJECT_FORM> = {},
  hostOverrides: { authenticatedFetch?: typeof fetch; bffBaseUrl?: string } = {}
) {
  const props = {
    dataService,
    onValidChange: jest.fn(),
    onFormValues: jest.fn(),
    authenticatedFetch: authFetch,
    bffBaseUrl: BFF,
    // A seeded name makes the step start from these values (and skip the native AI pre-fill).
    initialFormValues: { ...EMPTY_PROJECT_FORM, ...initial },
    ...hostOverrides,
  };
  renderWithProviders(<CreateProjectStep {...props} />);
  return props;
}

const generateButton = () => screen.getByRole('button', { name: /generate description with ai/i });
const descriptionBox = () => screen.getByRole('textbox', { name: 'Project Description' }) as HTMLTextAreaElement;

describe('CreateProjectStep — Generate with AI (task 239)', () => {
  let spies: jest.SpyInstance[];

  beforeEach(() => {
    mockStream.mockReset();
    spies = (['info', 'error', 'warn'] as const).map(m => jest.spyOn(console, m).mockImplementation(() => {}));
  });

  afterEach(() => spies.forEach(s => s.mockRestore()));

  it('is disabled until the Project Name has text', async () => {
    renderStep();

    expect(generateButton()).toBeDisabled();

    await userEvent.type(screen.getByRole('textbox', { name: 'Project Name' }), 'Data Room Migration');

    expect(generateButton()).toBeEnabled();
  });

  it('stays disabled for a whitespace-only name', async () => {
    renderStep();

    await userEvent.type(screen.getByRole('textbox', { name: 'Project Name' }), '   ');

    expect(generateButton()).toBeDisabled();
  });

  it('asks for a project draft and streams it into the description, which stays editable', async () => {
    mockStream.mockResolvedValue({ summary: 'A drafted project description.' });
    const props = renderStep({
      projectName: 'Data Room Migration',
      projectTypeName: 'Transaction',
      practiceAreaName: 'Employment',
    });

    fireEvent.click(generateButton());

    await waitFor(() => expect(descriptionBox()).toHaveValue('A drafted project description.'));
    expect(mockStream).toHaveBeenCalledTimes(1);
    const [request, callbacks, signal, fetchArg, baseArg] = mockStream.mock.calls[0];
    expect(request).toEqual({
      kind: 'project',
      name: 'Data Room Migration',
      type: 'Transaction',
      practiceArea: 'Employment',
    });
    expect(callbacks).toEqual({});
    expect(signal).toBeUndefined();
    expect(fetchArg).toBe(authFetch);
    expect(baseArg).toBe(BFF);
    // The draft reaches the parent's form values, so it is what Create submits.
    expect(props.onFormValues).toHaveBeenLastCalledWith(
      expect.objectContaining({ description: 'A drafted project description.' })
    );

    await userEvent.type(descriptionBox(), ' Edited.');

    expect(descriptionBox()).toHaveValue('A drafted project description. Edited.');
  });

  it('replaces text the user already typed (as Create Matter does)', async () => {
    mockStream.mockResolvedValue({ summary: 'Fresh draft.' });
    renderStep({ projectName: 'P', description: 'Old text' });

    fireEvent.click(generateButton());

    await waitFor(() => expect(descriptionBox()).toHaveValue('Fresh draft.'));
  });

  it('shows "Generating…", disables the button while it runs, and recovers after', async () => {
    let finish: (v: { summary: string }) => void = () => {};
    mockStream.mockReturnValue(new Promise(res => (finish = res)));
    renderStep({ projectName: 'P' });

    fireEvent.click(generateButton());

    expect(await screen.findByRole('button', { name: /generate description with ai/i })).toBeDisabled();
    expect(screen.getByText('Generating…')).toBeInTheDocument();

    await act(async () => finish({ summary: 'Done.' }));

    await waitFor(() => expect(generateButton()).toBeEnabled());
    expect(screen.getByText('Generate with AI')).toBeInTheDocument();
  });

  it('a failed draft keeps the text the user typed, shows the warning, and the button recovers', async () => {
    mockStream.mockResolvedValue({ summary: '', isFallback: true });
    renderStep({ projectName: 'Data Room Migration', description: 'My own notes' });

    fireEvent.click(generateButton());

    expect(await screen.findByText(AI_DRAFT_UNAVAILABLE_MESSAGE)).toBeInTheDocument();
    expect(descriptionBox()).toHaveValue('My own notes');
    expect(generateButton()).toBeEnabled();
  });

  it('a real non-OK response (rate limit) keeps the typed text and shows the warning', async () => {
    // The real helper against a fetch that answers 429: no canned text, the user's text survives.
    mockStream.mockImplementation(
      jest.requireActual('../../CreateMatterWizard/matterService').streamAiDraftDescription
    );
    const rateLimited = jest.fn(
      async () => ({ ok: false, status: 429, text: async () => 'Too many requests', body: null }) as unknown as Response
    );
    renderStep(
      { projectName: 'Data Room Migration', description: 'My own notes' },
      { authenticatedFetch: rateLimited as unknown as typeof fetch }
    );

    fireEvent.click(generateButton());

    expect(await screen.findByText(AI_DRAFT_UNAVAILABLE_MESSAGE)).toBeInTheDocument();
    expect(rateLimited).toHaveBeenCalledTimes(1);
    expect(descriptionBox()).toHaveValue('My own notes');
  });

  it('a successful draft replaces the text and shows no warning (and clears an earlier warning)', async () => {
    mockStream.mockResolvedValueOnce({ summary: '', isFallback: true });
    renderStep({ projectName: 'P', description: 'Old text' });

    fireEvent.click(generateButton());
    expect(await screen.findByText(AI_DRAFT_UNAVAILABLE_MESSAGE)).toBeInTheDocument();

    mockStream.mockResolvedValueOnce({ summary: 'Fresh draft.' });
    fireEvent.click(generateButton());

    await waitFor(() => expect(descriptionBox()).toHaveValue('Fresh draft.'));
    expect(screen.queryByText(AI_DRAFT_UNAVAILABLE_MESSAGE)).not.toBeInTheDocument();
  });

  it.each([
    ['no authenticated fetch', { authenticatedFetch: undefined }],
    ['no BFF base url', { bffBaseUrl: undefined }],
  ])('a host with %s shows no "Generate with AI" button', (_label, overrides) => {
    renderStep({ projectName: 'P' }, overrides);

    expect(screen.queryByRole('button', { name: /generate description with ai/i })).not.toBeInTheDocument();
    expect(descriptionBox()).toBeInTheDocument();
  });
});
