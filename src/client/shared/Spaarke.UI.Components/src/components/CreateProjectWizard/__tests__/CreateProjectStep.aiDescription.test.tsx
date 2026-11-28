/**
 * CreateProjectStep — "Generate with AI" beside Project Description (ontology task 239, owner UAT round 4 A1).
 *
 * Pinned (mirrors Create Matter's button):
 *   - the button is disabled until the Project Name has text, and enabled after;
 *   - clicking it asks the shared helper for a project draft (name, type, practice area) and puts the draft in the
 *     description, which the user can then edit;
 *   - a failed draft shows the helper's existing fallback text (never a blank or a crash), and the button recovers;
 *   - while it runs the button reads "Generating…" and is disabled.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { IDataService } from '../../../types/serviceInterfaces';
import { CreateProjectStep } from '../CreateProjectStep';
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

function renderStep(initial: Partial<typeof EMPTY_PROJECT_FORM> = {}) {
  const props = {
    dataService,
    onValidChange: jest.fn(),
    onFormValues: jest.fn(),
    authenticatedFetch: authFetch,
    bffBaseUrl: BFF,
    // A seeded name makes the step start from these values (and skip the native AI pre-fill).
    initialFormValues: { ...EMPTY_PROJECT_FORM, ...initial },
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

  it('an unavailable AI shows the helper’s fallback text and the button recovers (existing AI error treatment)', async () => {
    // The real helper is used here: no host, so it answers with its project fallback.
    mockStream.mockImplementation(
      jest.requireActual('../../CreateMatterWizard/matterService').streamAiDraftDescription
    );
    renderStep({ projectName: 'Data Room Migration', practiceAreaName: 'Employment' });

    fireEvent.click(generateButton());

    await waitFor(() => expect(descriptionBox().value).toContain('project, "Data Room Migration"'));
    expect(descriptionBox().value).not.toMatch(/matter/i);
    expect(generateButton()).toBeEnabled();
  });
});
