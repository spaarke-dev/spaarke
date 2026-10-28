/**
 * WorkAssignmentWizardDialog — the "Assigned To: {name}" line of the Send Email summary (field-mapping-server-write-path-r1
 * task 038, FR-37). The line appears only when a contact was chosen on Enter Info; a blank picker adds nothing.
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

/** Open the dialog, report Enter Info values, select the Send Email follow-on, and return the pre-filled email body. */
async function emailBodyFor(assignedTo: { assignedToId: string; assignedToName: string }): Promise<string> {
  mockShell.props = null;
  mockEnterInfo.props = null;
  mockGrid.props = null;
  mockEmail.props = null;
  render(
    <WorkAssignmentWizardDialog
      open
      onClose={jest.fn()}
      dataService={{} as never}
      authenticatedFetch={jest.fn() as never}
      bffBaseUrl="https://bff.example"
    />
  );

  const stepContent = (id: string) => {
    const step = (mockShell.props.steps as Array<{ id: string; renderContent: () => React.ReactNode }>).find(
      s => s.id === id
    )!;
    render(<>{step.renderContent()}</>);
  };

  stepContent('enter-info');
  act(() => mockEnterInfo.props.onFormValues({ ...ENTER_INFO, ...assignedTo }));

  stepContent('next-steps');
  act(() => mockGrid.props.onSelectionChange(['send-email']));

  // The pre-fill effect writes the body; the follow-on card reads it fresh when its step renders.
  const sendEmailCard = (mockGrid.props.cards as Array<{ id: string; renderStep: () => React.ReactNode }>).find(
    c => c.id === 'send-email'
  )!;
  await waitFor(() => {
    mockEmail.props = null;
    render(<>{sendEmailCard.renderStep()}</>);
    expect(mockEmail.props.emailBody).toContain('Priority:');
  });
  return mockEmail.props.emailBody as string;
}

describe('WorkAssignmentWizardDialog — Assigned To in the summary (task 038, FR-37)', () => {
  it('names the chosen contact in the summary', async () => {
    const body = await emailBodyFor({ assignedToId: 'c-1', assignedToName: 'Chosen Contact' });
    expect(body).toContain('Assigned To: Chosen Contact');
  });

  it('adds no Assigned To line when the picker is blank', async () => {
    const body = await emailBodyFor({ assignedToId: '', assignedToName: '' });
    expect(body).toContain('Name: WA-1');
    expect(body).not.toContain('Assigned To');
  });
});
