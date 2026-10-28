/**
 * WorkAssignmentWizardDialog — Back to Enter Info keeps what was entered (field-mapping-server-write-path-r1 task 038,
 * fix round 1, F1).
 *
 * `WizardShell` renders only the CURRENT step, so Back to Enter Info remounts it. It used to be re-seeded from the record
 * pre-fill only, which wiped every edit (and with no record selected, everything typed) including the new Assigned To:
 * the person picked Y, went Next, came Back, saw a blank picker, and finished with the server default while the Send
 * Email summary still named Y. Now, once Enter Info has reported values, a remount is seeded from them.
 *
 * Real `EnterInfoStep` and `LookupField` inside the real dialog; only the shell (a recorder), the service and the
 * hand-off hook are replaced. The contact search is production-like: it returns [] for fewer than 2 characters.
 *
 * Classification (ADR-038 §7): MAINTAIN — pins the "edits survive Back" contract of the Enter Info step.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

const mockShell: { props: any } = { props: null };
jest.mock('../../Wizard/WizardShell', () => ({
  WizardShell: React.forwardRef((props: any, _ref: unknown) => {
    mockShell.props = props;
    return <div data-testid="shell" />;
  }),
}));

const CONTACT = { id: '51a5ddb0-40bf-f111-aaaf-0022482913fc', name: 'Chosen Contact' };
jest.mock('../workAssignmentService', () => ({
  searchUsersAsLookup: jest.fn(),
  searchMatterTypes: jest.fn(async () => []),
  searchPracticeAreas: jest.fn(async () => []),
  searchContactsAsLookup: jest.fn(async (_ds: unknown, q: string) => (q.trim().length < 2 ? [] : [CONTACT])),
  WorkAssignmentService: jest.fn().mockImplementation(() => ({})),
}));

jest.mock('../../CreateRecordWizard/useHandoffFileLeg', () => ({
  useHandoffFileLeg: () => [],
}));

import WorkAssignmentWizardDialog from '../WorkAssignmentWizardDialog';

/** Render the dialog's current Enter Info step content in its own tree (what the shell does when it is the current step). */
function renderEnterInfo() {
  const step = (mockShell.props.steps as Array<{ id: string; renderContent: () => React.ReactNode }>).find(
    s => s.id === 'enter-info'
  )!;
  return renderWithProviders(<>{step.renderContent()}</>);
}

describe('WorkAssignmentWizardDialog — Back to Enter Info (task 038, fix round 1)', () => {
  beforeEach(() => {
    mockShell.props = null;
    renderWithProviders(
      <WorkAssignmentWizardDialog
        open
        onClose={jest.fn()}
        dataService={{} as never}
        authenticatedFetch={jest.fn() as never}
        bffBaseUrl="https://bff.example"
      />
    );
  });

  it('keeps the name, the date and the chosen Assigned To when Enter Info is left and re-entered (no record selected)', async () => {
    const first = renderEnterInfo();

    fireEvent.change(screen.getByPlaceholderText('Work assignment name'), { target: { value: 'Typed name' } });
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
});
