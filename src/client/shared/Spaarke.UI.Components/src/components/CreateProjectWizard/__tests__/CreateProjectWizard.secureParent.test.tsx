/**
 * CreateProjectWizard — a project under a secure matter is secure too (field-mapping-server-write-path-r1 task 022 fix
 * round 2). Preview only: the server decides the create; the wizard shows the rule.
 *
 * Pinned:
 *   - choosing a secure Matter in "Associate To" turns Secure on and locks it, with a short reason;
 *   - a non-secure Matter, an account, or a matter whose flag cannot be read leaves Secure editable;
 *   - the section itself, locked, is on and disabled and reports no change.
 */
import * as React from 'react';
import { act, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { ICreateRecordWizardConfig } from '../../CreateRecordWizard';
import type { IDataService, INavigationService } from '../../../types/serviceInterfaces';
import type { AssociationResult } from '../../AssociateToStep/types';
import { CreateProjectWizard } from '../CreateProjectWizard';
import { SECURE_PARENT_LOCK_REASON, SecureProjectSection } from '../SecureProjectSection';

const mockShell: { config?: ICreateRecordWizardConfig } = {};
jest.mock('../../CreateRecordWizard', () => ({
  CreateRecordWizard: (props: { config: ICreateRecordWizardConfig }) => {
    mockShell.config = props.config;
    return null;
  },
}));

const SECURE_MATTER = 'd0220000-0000-4000-8000-000000000001';
const ORDINARY_MATTER = 'd0220000-0000-4000-8000-000000000002';
const UNREADABLE_MATTER = 'd0220000-0000-4000-8000-000000000003';

const retrieveRecord = jest.fn(async (_entity: string, id: string) => {
  if (id === UNREADABLE_MATTER) throw new Error('read failed');
  return { sprk_issecure: id === SECURE_MATTER };
});

const dataService = {
  createRecord: jest.fn(),
  retrieveRecord,
  retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
  updateRecord: jest.fn(),
  deleteRecord: jest.fn(),
} as unknown as IDataService;

const navigationService = { openRecord: jest.fn(), openLookup: jest.fn() } as unknown as INavigationService;

function matter(id: string): AssociationResult {
  return { entityType: 'sprk_matter', recordId: id, recordName: 'MAT-1' };
}

/** Opens the wizard, announces the association, and renders the Enter Info step the shell would show. */
async function enterInfoAfterChoosing(association: AssociationResult | null) {
  renderWithProviders(
    <CreateProjectWizard
      open
      onClose={jest.fn()}
      dataService={dataService}
      navigationService={navigationService}
      initialFormValues={{ projectName: 'Acme', isSecure: false }}
    />
  );
  await act(async () => {
    mockShell.config!.associateToStep!.onAssociationChange!(association);
    await Promise.resolve();
  });
  renderWithProviders(<>{mockShell.config!.infoStep.renderContent([])}</>);
  return screen.getByRole('switch', { name: /secure project/i });
}

describe('CreateProjectWizard — a project under a secure matter is secure (task 022 fix round 2)', () => {
  let spies: jest.SpyInstance[];

  beforeEach(() => {
    mockShell.config = undefined;
    retrieveRecord.mockClear();
    spies = (['info', 'error', 'warn'] as const).map(m => jest.spyOn(console, m).mockImplementation(() => {}));
  });

  afterEach(() => spies.forEach(s => s.mockRestore()));

  it('a secure Matter turns Secure on and locks it, with the reason', async () => {
    const toggle = await enterInfoAfterChoosing(matter(SECURE_MATTER));

    expect(retrieveRecord).toHaveBeenCalledWith('sprk_matter', SECURE_MATTER, '?$select=sprk_issecure');
    expect(toggle).toBeChecked();
    expect(toggle).toBeDisabled();
    expect(screen.getByText(SECURE_PARENT_LOCK_REASON)).toBeInTheDocument();
  });

  it('a non-secure Matter leaves Secure off and editable', async () => {
    const toggle = await enterInfoAfterChoosing(matter(ORDINARY_MATTER));

    expect(toggle).not.toBeChecked();
    expect(toggle).toBeEnabled();
    expect(screen.queryByText(SECURE_PARENT_LOCK_REASON)).not.toBeInTheDocument();
  });

  it('a matter whose flag cannot be read leaves Secure editable', async () => {
    const toggle = await enterInfoAfterChoosing(matter(UNREADABLE_MATTER));

    expect(toggle).toBeEnabled();
  });

  it('an account reads nothing and leaves Secure editable', async () => {
    const toggle = await enterInfoAfterChoosing({ entityType: 'account', recordId: SECURE_MATTER, recordName: 'Acme' });

    expect(retrieveRecord).not.toHaveBeenCalledWith('sprk_matter', expect.anything(), expect.anything());
    expect(toggle).toBeEnabled();
  });
});

describe('SecureProjectSection — locked on', () => {
  it('is on and disabled, says why, and reports no change', async () => {
    const onSecureChange = jest.fn();
    renderWithProviders(<SecureProjectSection isSecure={false} onSecureChange={onSecureChange} lockedOn />);

    const toggle = screen.getByRole('switch', { name: /secure project/i });
    expect(toggle).toBeChecked();
    expect(toggle).toBeDisabled();
    expect(screen.getByTestId('secure-project-locked-reason')).toHaveTextContent(SECURE_PARENT_LOCK_REASON);
    await userEvent.click(toggle);
    expect(onSecureChange).not.toHaveBeenCalled();
  });

  it('not locked: editable, no reason', () => {
    renderWithProviders(<SecureProjectSection isSecure={false} onSecureChange={jest.fn()} />);

    expect(screen.getByRole('switch', { name: /secure project/i })).toBeEnabled();
    expect(screen.queryByTestId('secure-project-locked-reason')).not.toBeInTheDocument();
  });
});
