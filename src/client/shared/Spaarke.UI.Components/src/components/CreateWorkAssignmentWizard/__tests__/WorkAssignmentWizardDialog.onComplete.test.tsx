/**
 * WorkAssignmentWizardDialog.onComplete.test.tsx — #1420 (spaarke-ontology-platform-r1 task 113).
 *
 * The dialog gained `onComplete?: (recordId) => void`, the surface-launch honest-ack seam
 * `CreateMatterWizard` already has: leaving the SUCCESS screen (Close or View Record) calls
 * `onComplete(workAssignmentId)` INSTEAD of `onClose`, so a launched create reads back as committed
 * (the code page and `InAppWizardHost` write the committed hand-off result). Every cancel path
 * (the shell's ×, the error screen's Close) keeps calling `onClose` and never `onComplete`.
 *
 * `WizardShell` is replaced by a recorder so the test drives the real `handleFinish` and renders the
 * real success/error actions.
 *
 * Classification (ADR-038 §7): MAINTAIN — pins the honest-ack contract Quick Start's Assign Work depends on.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';

// The shell recorder: captures the props the dialog passes (onFinish, onClose, ...).
const mockShell: { props: any } = { props: null };
jest.mock('../../Wizard/WizardShell', () => ({
  WizardShell: React.forwardRef((props: any, _ref: unknown) => {
    mockShell.props = props;
    return <div data-testid="shell" />;
  }),
}));

const mockCreateWorkAssignment = jest.fn();
jest.mock('../workAssignmentService', () => ({
  searchUsersAsLookup: jest.fn(),
  WorkAssignmentService: jest.fn().mockImplementation(() => ({
    createWorkAssignment: (...args: unknown[]) => mockCreateWorkAssignment(...args),
  })),
}));

jest.mock('../../CreateRecordWizard/useHandoffFileLeg', () => ({
  useHandoffFileLeg: () => [],
}));

import WorkAssignmentWizardDialog from '../WorkAssignmentWizardDialog';

function mountDialog(extra: Record<string, unknown> = {}) {
  const onClose = jest.fn();
  const openRecord = jest.fn();
  render(
    <WorkAssignmentWizardDialog
      open
      onClose={onClose}
      dataService={{} as never}
      authenticatedFetch={jest.fn() as never}
      bffBaseUrl="https://bff.example"
      navigationService={{ openRecord } as never}
      {...extra}
    />
  );
  return { onClose, openRecord };
}

/** Run the dialog's finish handler and render the success/error `actions` it returns. */
async function finishAndRenderActions(): Promise<void> {
  const config = await mockShell.props.onFinish();
  render(<div data-testid="actions">{config.actions}</div>);
}

beforeEach(() => {
  mockShell.props = null;
  mockCreateWorkAssignment.mockReset();
});

describe('WorkAssignmentWizardDialog — onComplete (#1420)', () => {
  it('Close on the success screen calls onComplete(recordId) and NOT onClose', async () => {
    mockCreateWorkAssignment.mockResolvedValue({ status: 'ok', workAssignmentId: 'wa-1', warnings: [] });
    const onComplete = jest.fn();
    const { onClose } = mountDialog({ onComplete });

    await finishAndRenderActions();
    fireEvent.click(screen.getByText('Close'));

    expect(onComplete).toHaveBeenCalledTimes(1);
    expect(onComplete).toHaveBeenCalledWith('wa-1');
    expect(onClose).not.toHaveBeenCalled();
  });

  it('View Record opens the record and then completes with the record id', async () => {
    mockCreateWorkAssignment.mockResolvedValue({ status: 'ok', workAssignmentId: 'wa-2', warnings: [] });
    const onComplete = jest.fn();
    const { onClose, openRecord } = mountDialog({ onComplete });

    await finishAndRenderActions();
    fireEvent.click(screen.getByText('View Record'));

    expect(openRecord).toHaveBeenCalledWith('sprk_workassignment', 'wa-2');
    expect(onComplete).toHaveBeenCalledWith('wa-2');
    expect(onClose).not.toHaveBeenCalled();
  });

  it('without onComplete the success screen still just closes (non-launch callers unchanged)', async () => {
    mockCreateWorkAssignment.mockResolvedValue({ status: 'ok', workAssignmentId: 'wa-3', warnings: [] });
    const { onClose } = mountDialog();

    await finishAndRenderActions();
    fireEvent.click(screen.getByText('Close'));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('the error screen Close is a cancel path: onClose, never onComplete', async () => {
    mockCreateWorkAssignment.mockResolvedValue({ status: 'error', errorMessage: 'boom', warnings: [] });
    const onComplete = jest.fn();
    const { onClose } = mountDialog({ onComplete });

    await finishAndRenderActions();
    fireEvent.click(screen.getByText('Close'));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onComplete).not.toHaveBeenCalled();
  });

  it('the shell close (x / cancel) is wired to onClose, never onComplete', async () => {
    const onComplete = jest.fn();
    const { onClose } = mountDialog({ onComplete });

    await waitFor(() => expect(mockShell.props).not.toBeNull());
    mockShell.props.onClose();

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onComplete).not.toHaveBeenCalled();
  });
});
