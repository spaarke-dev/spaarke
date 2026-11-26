/**
 * WorkAssignmentWizardDialog.sendEmail.test.tsx - spaarke-ontology-platform-r1 task 182, fix round 2.
 *
 * The Work Assignment wizard's Finish sends what the Send Email step composed. The dialog test fails if the composed
 * email stops reaching `WorkAssignmentService.sendEmail`; the service test fails if the service stops passing it to
 * `sendComposedEmail` (To, Cc, Bcc, From, body format, and the work assignment as association).
 */
import * as React from 'react';
import { act, render, waitFor } from '@testing-library/react';
import { EMPTY_COMPOSED_EMAIL } from '../../EmailComposer/composeOnlySend';
import type { IComposedEmail } from '../../EmailComposer/composeOnlySend';

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

const mockSendEmail = jest.fn();
jest.mock('../workAssignmentService', () => {
  const actual = jest.requireActual('../workAssignmentService');
  return {
    ...actual,
    searchUsersAsLookup: jest.fn(),
    WorkAssignmentService: jest.fn().mockImplementation(() => ({
      createWorkAssignment: jest.fn().mockResolvedValue({ status: 'ok', workAssignmentId: 'wa-1', warnings: [] }),
      sendEmail: (...args: unknown[]) => mockSendEmail(...args),
    })),
    RealWorkAssignmentService: actual.WorkAssignmentService,
  };
});

jest.mock('../../CreateRecordWizard/useHandoffFileLeg', () => ({ useHandoffFileLeg: () => [] }));

import WorkAssignmentWizardDialog from '../WorkAssignmentWizardDialog';

const COMPOSED: IComposedEmail = {
  ...EMPTY_COMPOSED_EMAIL,
  to: [{ email: 'counsel@firm.example', resolved: false }],
  cc: [{ email: 'partner@firm.example', resolved: false }],
  bcc: [{ email: 'records@firm.example', resolved: false }],
  subject: 'New work assignment: WA-1',
  body: 'Please take this on.\n\nThanks',
  bodyFormat: 'PlainText',
  sendMode: 'user',
};

describe('WorkAssignmentWizardDialog — Finish sends the composed email (task 182)', () => {
  beforeEach(() => {
    mockShell.props = null;
    mockEnterInfo.props = null;
    mockGrid.props = null;
    mockEmail.props = null;
    mockSendEmail.mockReset().mockResolvedValue({ success: true });
  });

  it('passes what the Send Email step composed to the service, for the created work assignment', async () => {
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
    act(() =>
      mockEnterInfo.props.onFormValues({
        name: 'WA-1',
        description: '',
        matterTypeId: '',
        matterTypeName: '',
        practiceAreaId: '',
        practiceAreaName: '',
        priority: 100000001,
        responseDueDate: '2026-12-01',
      })
    );
    stepContent('next-steps');
    act(() => mockGrid.props.onSelectionChange(['send-email']));

    const card = (mockGrid.props.cards as Array<{ id: string; renderStep: () => React.ReactNode }>).find(
      c => c.id === 'send-email'
    )!;
    const tree = render(<>{card.renderStep()}</>);
    await waitFor(() => expect(mockEmail.props?.value.subject).toBeTruthy()); // the pre-fill arrived
    act(() => mockEmail.props.onChange(COMPOSED));
    tree.rerender(<>{card.renderStep()}</>);

    await act(async () => {
      await mockShell.props.onFinish();
    });

    expect(mockSendEmail).toHaveBeenCalledTimes(1);
    expect(mockSendEmail).toHaveBeenCalledWith('wa-1', 'WA-1', COMPOSED, undefined);
  });
});

describe('WorkAssignmentWizardDialog — closing deletes the Send Email step unsent uploads (task 220, #2813)', () => {
  const dialog = (open: boolean) => (
    <WorkAssignmentWizardDialog
      open={open}
      onClose={jest.fn()}
      dataService={{} as never}
      authenticatedFetch={jest.fn() as never}
      bffBaseUrl="https://bff.example"
    />
  );

  it('closing without Finish deletes the document the step uploaded, and leaves a passed-in document alone', async () => {
    const discard = jest.fn(async () => undefined);
    const view = render(dialog(true));
    const nextSteps = (mockShell.props.steps as Array<{ id: string; renderContent: () => React.ReactNode }>).find(
      st => st.id === 'next-steps'
    )!;
    render(<>{nextSteps.renderContent()}</>);
    act(() => mockGrid.props.onSelectionChange(['send-email']));
    const card = (mockGrid.props.cards as Array<{ id: string; renderStep: () => React.ReactNode }>).find(
      c => c.id === 'send-email'
    )!;
    render(<>{card.renderStep()}</>);
    act(() =>
      mockEmail.props.onChange({
        ...COMPOSED,
        attachments: [
          { id: 'local:1', source: 'local', fileName: 'fees.pdf', sizeBytes: 1, documentId: 'doc-up', discard },
          { id: 'doc:d-9', source: 'related', fileName: 'brief.pdf', sizeBytes: 1, documentId: 'd-9' },
        ],
      })
    );
    expect(discard).not.toHaveBeenCalled();

    view.rerender(dialog(false));

    await waitFor(() => expect(discard).toHaveBeenCalledTimes(1));
  });
});

describe('WorkAssignmentService.sendEmail (task 182)', () => {
  it('sends the composed email with Cc, Bcc and From, filed under the work assignment', async () => {
    const { RealWorkAssignmentService } = jest.requireMock('../workAssignmentService') as {
      RealWorkAssignmentService: new (...args: unknown[]) => {
        sendEmail: (id: string, name: string, email: IComposedEmail) => Promise<{ success: boolean }>;
      };
    };
    const sends: Array<Record<string, unknown>> = [];
    const authFetch = jest.fn(async (url: string, init?: RequestInit) => {
      if (String(url).includes('/api/communications/send')) sends.push(JSON.parse(init?.body as string));
      return { ok: true, status: 200, json: async () => ({ communicationId: 'c-1' }) } as unknown as Response;
    });
    const service = new RealWorkAssignmentService({} as never, authFetch, 'https://bff.example');

    const result = await service.sendEmail('wa-1', 'WA-1', COMPOSED);

    expect(result.success).toBe(true);
    expect(sends).toHaveLength(1);
    expect(sends[0]).toEqual(
      expect.objectContaining({
        to: ['counsel@firm.example'],
        cc: ['partner@firm.example'],
        bcc: ['records@firm.example'],
        body: 'Please take this on.\n\nThanks',
        bodyFormat: 'PlainText',
        sendMode: 'User',
      })
    );
    expect(sends[0].associations).toEqual([
      expect.objectContaining({ entityType: 'sprk_workassignment', entityId: 'wa-1', entityName: 'WA-1' }),
    ]);
  });
});
