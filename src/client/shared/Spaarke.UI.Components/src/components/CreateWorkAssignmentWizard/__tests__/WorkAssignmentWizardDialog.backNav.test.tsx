/**
 * WorkAssignmentWizardDialog — Back to Enter Info keeps what was entered (field-mapping-server-write-path-r1 task 038,
 * fix rounds 1 and 2).
 *
 * `WizardShell` renders only the CURRENT step, so Back to Enter Info remounts it. It used to be re-seeded from the record
 * pre-fill only, which wiped every edit (and with no record selected, everything typed) including the new Assigned To:
 * the person picked Y, went Next, came Back, saw a blank picker, and finished with the server default while the Send
 * Email summary still named Y. Now, once Enter Info has reported values, a remount is seeded from them.
 *
 * The seed must not switch AI pre-fill off for good (fix round 2): AI pre-fill is skipped on a remount only when the form
 * holds something; an empty form (files added after an empty first visit, a run abandoned by Back) still gets its run.
 * A different record clears the seed (its pre-fill applies), a failed read never leaves the previous record's values,
 * and a reopen starts blank.
 *
 * Real `EnterInfoStep` and `LookupField` inside the real dialog; only the shell (a recorder), Select Work and Add Files
 * (recorders that let the test report a record / files), the service and the hand-off hook are replaced. The contact
 * search is production-like: it returns [] for fewer than 2 characters.
 *
 * Classification (ADR-038 §7): MAINTAIN — pins the "edits survive Back" contract of the Enter Info step.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

const mockShell: { props: any } = { props: null };
jest.mock('../../Wizard/WizardShell', () => ({
  WizardShell: React.forwardRef((props: any, _ref: unknown) => {
    mockShell.props = props;
    return <div data-testid="shell" />;
  }),
}));

const mockSelectWork: { props: any } = { props: null };
jest.mock('../SelectWorkStep', () => ({
  SelectWorkStep: (props: any) => {
    mockSelectWork.props = props;
    return null;
  },
}));

const mockAddFiles: { props: any } = { props: null };
jest.mock('../AddFilesStep', () => ({
  AddFilesStep: (props: any) => {
    mockAddFiles.props = props;
    return null;
  },
}));

const CONTACT = { id: '51a5ddb0-40bf-f111-aaaf-0022482913fc', name: 'Chosen Contact' };
const mockRead = jest.fn();
jest.mock('../workAssignmentService', () => ({
  searchUsersAsLookup: jest.fn(),
  searchMatterTypes: jest.fn(async () => []),
  searchPracticeAreas: jest.fn(async () => []),
  searchContactsAsLookup: jest.fn(async (_ds: unknown, q: string) => (q.trim().length < 2 ? [] : [CONTACT])),
  WorkAssignmentService: jest.fn().mockImplementation(() => ({
    readRecordForPrefill: (...args: unknown[]) => mockRead(...args),
  })),
}));

jest.mock('../../CreateRecordWizard/useHandoffFileLeg', () => ({
  useHandoffFileLeg: () => [],
}));

import WorkAssignmentWizardDialog from '../WorkAssignmentWizardDialog';

/** The AI pre-fill endpoint never answers: the run stays "in flight" (the step shows its spinner). */
const mockFetch = jest.fn(() => new Promise<never>(() => undefined));
const aiCalls = () => mockFetch.mock.calls.filter(c => String((c as unknown[])[0]).includes('/pre-fill')).length;

const FILE = { id: 'f1', name: 'brief.pdf', sizeBytes: 1, fileType: 'pdf', file: new File(['x'], 'brief.pdf') };

type Step = { id: string; renderContent: () => React.ReactNode };
const stepById = (id: string) => (mockShell.props.steps as Step[]).find(s => s.id === id)!;

/** Render the dialog's current Enter Info step content in its own tree (what the shell does when it is the current step). */
function renderEnterInfo() {
  return renderWithProviders(<>{stepById('enter-info').renderContent()}</>);
}

/** Report the files the Add Files step would hold. */
function setFiles(files: unknown[]) {
  renderWithProviders(<>{stepById('add-files').renderContent()}</>);
  act(() => mockAddFiles.props.onUploadedFilesChange(files));
}

/** Report the record the Select Work step would hold, and let the pre-fill read settle. */
async function selectRecord(recordId: string, read: () => Promise<Record<string, unknown>>) {
  mockRead.mockImplementationOnce(read);
  renderWithProviders(<>{stepById('select-work').renderContent()}</>);
  const calls = mockRead.mock.calls.length;
  act(() => mockSelectWork.props.onFormValues({ recordType: 'matter', recordId, recordName: recordId }));
  await waitFor(() => expect(mockRead.mock.calls.length).toBe(calls + 1));
  await act(async () => {
    await mockRead.mock.results[mockRead.mock.results.length - 1].value.catch(() => undefined);
  });
  await act(async () => undefined);
}

const nameInput = () => screen.getByPlaceholderText('Work assignment name') as HTMLInputElement;

describe('WorkAssignmentWizardDialog — Back to Enter Info (task 038, fix rounds 1 and 2)', () => {
  let dialog: ReturnType<typeof renderWithProviders>;
  let consoleInfo: jest.SpyInstance;
  const props = () => ({
    open: true,
    onClose: jest.fn(),
    dataService: {} as never,
    authenticatedFetch: mockFetch as never,
    bffBaseUrl: 'https://bff.example',
  });

  beforeEach(() => {
    mockShell.props = null;
    mockRead.mockReset();
    mockFetch.mockClear();
    consoleInfo = jest.spyOn(console, 'info').mockImplementation(() => undefined);
    dialog = renderWithProviders(<WorkAssignmentWizardDialog {...props()} />);
  });

  afterEach(() => {
    consoleInfo.mockRestore();
  });

  it('keeps the name, the date and the chosen Assigned To when Enter Info is left and re-entered (no record selected)', async () => {
    const first = renderEnterInfo();

    fireEvent.change(nameInput(), { target: { value: 'Typed name' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Assigned To' }), { target: { value: 'ch' } });
    await waitFor(() => expect(screen.getByText(CONTACT.name)).toBeInTheDocument());
    fireEvent.click(screen.getByText(CONTACT.name));
    await waitFor(() => expect(screen.queryByRole('textbox', { name: 'Assigned To' })).toBeNull());

    // Next Steps: Enter Info unmounts. Back: the shell renders it again from the dialog.
    first.unmount();
    renderEnterInfo();

    expect(await screen.findByDisplayValue('Typed name')).toBeInTheDocument();
    expect(screen.getByText(CONTACT.name)).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Assigned To' })).toBeNull();
  });

  it('a cleared Assigned To stays cleared on re-entry', async () => {
    const first = renderEnterInfo();
    fireEvent.change(screen.getByRole('textbox', { name: 'Assigned To' }), { target: { value: 'ch' } });
    await waitFor(() => expect(screen.getByText(CONTACT.name)).toBeInTheDocument());
    fireEvent.click(screen.getByText(CONTACT.name));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Clear Assigned To' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Clear Assigned To' }));
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Assigned To' })).toBeInTheDocument());

    first.unmount();
    renderEnterInfo();

    expect(await screen.findByRole('textbox', { name: 'Assigned To' })).toBeInTheDocument();
    expect(screen.queryByText(CONTACT.name)).toBeNull();
  });

  it('a reopened dialog starts with a blank Enter Info', async () => {
    const first = renderEnterInfo();
    fireEvent.change(nameInput(), { target: { value: 'Typed name' } });
    await waitFor(() => expect(nameInput().value).toBe('Typed name'));
    first.unmount();

    dialog.rerender(<WorkAssignmentWizardDialog {...props()} open={false} />);
    dialog.rerender(<WorkAssignmentWizardDialog {...props()} open />);
    renderEnterInfo();

    expect(nameInput().value).toBe('');
  });

  it('a different record applies its own pre-fill, not the edits made for the earlier one', async () => {
    await selectRecord('rec-a', async () => ({ name: 'From A' }));
    const first = renderEnterInfo();
    expect(nameInput().value).toBe('From A');
    fireEvent.change(nameInput(), { target: { value: 'Edited for A' } });
    await waitFor(() => expect(nameInput().value).toBe('Edited for A'));
    first.unmount();

    await selectRecord('rec-b', async () => ({ name: 'From B' }));
    renderEnterInfo();

    expect(nameInput().value).toBe('From B');
  });

  it('a failed pre-fill read for the new record does not seed Enter Info with the previous record values', async () => {
    await selectRecord('rec-a', async () => ({ name: 'From A' }));
    await selectRecord('rec-b', async () => {
      throw new Error('read failed');
    });

    renderEnterInfo();

    expect(nameInput().value).toBe('');
  });

  describe('AI pre-fill across Back (fix round 2)', () => {
    it('runs on the first visit when files are present', () => {
      setFiles([FILE]);
      renderEnterInfo();
      expect(aiCalls()).toBe(1);
    });

    it('is skipped on a remount once the form holds entered values', async () => {
      const first = renderEnterInfo();
      fireEvent.change(nameInput(), { target: { value: 'Typed name' } });
      await waitFor(() => expect(nameInput().value).toBe('Typed name'));
      first.unmount();

      setFiles([FILE]);
      renderEnterInfo();

      expect(aiCalls()).toBe(0);
      expect(nameInput().value).toBe('Typed name');
    });

    it('runs when files are added after an empty first visit (the form holds nothing)', () => {
      const first = renderEnterInfo();
      expect(aiCalls()).toBe(0);
      first.unmount();

      setFiles([FILE]);
      renderEnterInfo();

      expect(aiCalls()).toBe(1);
    });

    it('runs again when Back abandoned the first run before it filled anything', () => {
      setFiles([FILE]);
      const first = renderEnterInfo();
      expect(aiCalls()).toBe(1);
      first.unmount();

      renderEnterInfo();

      expect(aiCalls()).toBe(2);
    });
  });
});
