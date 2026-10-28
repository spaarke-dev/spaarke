/**
 * WorkAssignmentWizardDialog — the "Assigned To: {name}" line of the Send Email summary (field-mapping-server-write-path-r1
 * task 038, FR-37). The line appears only when a contact was chosen on Enter Info; a blank picker adds nothing. While the
 * person has not edited the pre-filled email, a later change on Enter Info refreshes it (fix round 1: Back to Enter Info,
 * change Assigned To, and the summary must not name the old one); an edited email is left alone.
 *
 * `WizardShell`, `EnterInfoStep` and the shared Next-Steps grid are replaced by recorders so the test drives the real
 * dialog state (form values from Enter Info, a selected Send Email follow-on) and reads the body the dialog pre-fills.
 *
 * Classification (ADR-038 §7): MAINTAIN — pins that the summary names an assignee only when one was chosen.
 */
import * as React from 'react';
import { act, render, waitFor } from '@testing-library/react';

const mockShell: { props: any } = { props: null };
jest.mock('../../Wizard/WizardShell', () => ({
  WizardShell: React.forwardRef((props: any, _ref: unknown) => {
    mockShell.props = props;
    return <div data-testid="shell" />;
  }),
}));

const mockEnterInfo: { props: any } = { props: null };
jest.mock('../EnterInfoStep', () => ({
  EnterInfoStep: (props: any) => {
    mockEnterInfo.props = props;
    return null;
  },
}));

const mockGrid: { props: any } = { props: null };
const mockEmail: { props: any } = { props: null };
jest.mock('../../WizardFollowOns', () => ({
  FollowOnGrid: (props: any) => {
    mockGrid.props = props;
    return null;
  },
  SendEmailFollowOnStep: (props: any) => {
    mockEmail.props = props;
    return null;
  },
  followOnStepId: (id: string) => `followon-${id}`,
}));

jest.mock('../workAssignmentService', () => ({
  searchUsersAsLookup: jest.fn(),
  WorkAssignmentService: jest.fn().mockImplementation(() => ({})),
}));

jest.mock('../../CreateRecordWizard/useHandoffFileLeg', () => ({
  useHandoffFileLeg: () => [],
}));

import WorkAssignmentWizardDialog from '../WorkAssignmentWizardDialog';

const ENTER_INFO = {
  name: 'WA-1',
  description: '',
  matterTypeId: '',
  matterTypeName: '',
  practiceAreaId: '',
  practiceAreaName: '',
  priority: 100000001,
  responseDueDate: '2026-12-01',
};

type Assignee = { assignedToId: string; assignedToName: string };

/** Open the dialog, report Enter Info values, select the Send Email follow-on; return handles to read/drive it. */
async function openWithSendEmail(assignedTo: Assignee) {
  mockShell.props = null;
  mockEnterInfo.props = null;
  mockGrid.props = null;
  mockEmail.props = null;
  const dialogProps = {
    onClose: jest.fn(),
    dataService: {} as never,
    authenticatedFetch: jest.fn() as never,
    bffBaseUrl: 'https://bff.example',
  };
  const dialog = render(<WorkAssignmentWizardDialog open {...dialogProps} />);
  const reopen = () => {
    dialog.rerender(<WorkAssignmentWizardDialog open={false} {...dialogProps} />);
    dialog.rerender(<WorkAssignmentWizardDialog open {...dialogProps} />);
  };

  const stepContent = (id: string) => {
    const step = (mockShell.props.steps as Array<{ id: string; renderContent: () => React.ReactNode }>).find(
      s => s.id === id
    )!;
    render(<>{step.renderContent()}</>);
  };
  const reportEnterInfo = (over: Partial<typeof ENTER_INFO> & Partial<Assignee>) =>
    act(() => mockEnterInfo.props.onFormValues({ ...ENTER_INFO, ...assignedTo, ...over }));

  stepContent('enter-info');
  reportEnterInfo({});

  stepContent('next-steps');
  act(() => mockGrid.props.onSelectionChange(['send-email']));

  // The Send Email step reads the dialog's pre-filled email fresh each time it renders.
  // One tree, re-rendered per read: a fresh `render` inside `waitFor` mutates the DOM, which re-triggers `waitFor`
  // endlessly when the awaited condition never holds. The mocked step renders nothing, so a re-render mutates nothing.
  const emailTree = render(<></>);
  const readEmail = () => {
    const card = (mockGrid.props.cards as Array<{ id: string; renderStep: () => React.ReactNode }>).find(
      c => c.id === 'send-email'
    )!;
    mockEmail.props = null;
    emailTree.rerender(<>{card.renderStep()}</>);
    return mockEmail.props as {
      emailBody: string;
      emailSubject: string;
      onEmailBodyChange: (v: string) => void;
      onEmailSubjectChange: (v: string) => void;
    };
  };
  await waitFor(() => expect(readEmail().emailBody).toContain('Priority:'));
  return { readEmail, reportEnterInfo, reopen };
}

describe('WorkAssignmentWizardDialog — Assigned To in the summary (task 038, FR-37)', () => {
  it('names the chosen contact in the summary', async () => {
    const { readEmail } = await openWithSendEmail({ assignedToId: 'c-1', assignedToName: 'Chosen Contact' });
    expect(readEmail().emailBody).toContain('Assigned To: Chosen Contact');
  });

  it('adds no Assigned To line when the picker is blank', async () => {
    const { readEmail } = await openWithSendEmail({ assignedToId: '', assignedToName: '' });
    expect(readEmail().emailBody).toContain('Name: WA-1');
    expect(readEmail().emailBody).not.toContain('Assigned To');
  });

  it('refreshes the unedited summary when Assigned To changes on Enter Info (Back, change, Next)', async () => {
    const { readEmail, reportEnterInfo } = await openWithSendEmail({
      assignedToId: 'c-1',
      assignedToName: 'Contact Y',
    });
    expect(readEmail().emailBody).toContain('Assigned To: Contact Y');

    reportEnterInfo({ assignedToId: '', assignedToName: '' });

    await waitFor(() => expect(readEmail().emailBody).not.toContain('Assigned To'));
    reportEnterInfo({ name: 'WA-2', assignedToId: 'c-2', assignedToName: 'Contact Z' });
    await waitFor(() => expect(readEmail().emailBody).toContain('Assigned To: Contact Z'));
    expect(readEmail().emailSubject).toBe('Work Assignment: WA-2');
  });

  it('leaves an email the person edited alone', async () => {
    const { readEmail, reportEnterInfo } = await openWithSendEmail({
      assignedToId: 'c-1',
      assignedToName: 'Contact Y',
    });

    const email = readEmail();
    act(() => email.onEmailBodyChange('My own words'));
    reportEnterInfo({ assignedToId: 'c-2', assignedToName: 'Contact Z' });

    expect(readEmail().emailBody).toBe('My own words');
  });

  it('clearing the subject to retype it neither rewrites the subject nor overwrites an edited body (fix round 2)', async () => {
    const { readEmail } = await openWithSendEmail({ assignedToId: 'c-1', assignedToName: 'Contact Y' });

    const first = readEmail();
    act(() => first.onEmailBodyChange('My own words'));
    const second = readEmail();
    act(() => second.onEmailSubjectChange(''));
    await act(async () => undefined);

    const after = readEmail();
    expect(after.emailBody).toBe('My own words');
    expect(after.emailSubject).toBe('');
  });

  it('a reopened dialog pre-fills the email afresh (the earlier pre-fill does not block it)', async () => {
    const { readEmail, reportEnterInfo, reopen } = await openWithSendEmail({
      assignedToId: 'c-1',
      assignedToName: 'Contact Y',
    });

    reopen();
    reportEnterInfo({ name: 'WA-9', assignedToId: '', assignedToName: '' });
    act(() => mockGrid.props.onSelectionChange(['send-email']));

    await waitFor(() => expect(readEmail().emailBody).toContain('Name: WA-9'));
    expect(readEmail().emailSubject).toBe('Work Assignment: WA-9');
  });
});
