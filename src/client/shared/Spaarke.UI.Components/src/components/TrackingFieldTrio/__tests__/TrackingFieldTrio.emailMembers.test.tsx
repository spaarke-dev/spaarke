/**
 * TrackingFieldTrio — email-members action (task 042, teams-app-r1; task 196, unified-access-control-r2).
 *
 * `src/client/pcf/TrackingFieldTrio/index.ts` wires the shared core's `onOpenEmailMembers` callback (task 040) to:
 *   1. resolve the record's people through the SHARED `resolveEmailMembersRecipients` (task 196): role contacts +
 *      contacts with an active grant + users the record is shared with, de-duplicated by email, No Access applied;
 *   2. open the canonical `SendEmailDialog` (`EmailComposer` engine, `@spaarke/ui-components`, ADR-045) pre-populated
 *      with those emails, with a visible notice when some source could not be read;
 *   3. OR show the message dialog instead: the empty state only when every source was read and found nobody, an error
 *      when no source could be read — never a composer with zero recipients by mistake.
 *
 * `index.ts` is a PCF `ComponentFramework.StandardControl` class (not resolvable from this package's Jest environment —
 * it imports `./generated/ManifestTypes` and PCF-only globals), so it is not directly unit-testable here. This suite
 * mounts the REAL `TrackingFieldTrio` core + the REAL `SendEmailDialog` + the REAL shared resolver behind a thin harness
 * that reproduces `index.ts`'s click handler (`openEmailMembers`): the resolution rule itself is not copied here (task
 * 196 removed the harness's copy of it), only the three-way branch on its answer.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { TrackingFieldTrio } from '../TrackingFieldTrio';
import type { ITrackingFieldTrioProps, IAccessPermissionOption } from '../types';
import { SendEmailDialog } from '../../EmailComposer';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import {
  EMAIL_MEMBERS_EMPTY_MESSAGE,
  EMAIL_MEMBERS_FAILED_MESSAGE,
  resolveEmailMembersRecipients,
  type EmailMembersResolution,
  type IEmailMembersSources,
} from '../../AccessGrantModal';
import type { IAccessGrantCandidate } from '../../AccessGrantModal';

const renderWithTheme = (ui: React.ReactElement, theme = webLightTheme) =>
  render(<FluentProvider theme={theme}>{ui}</FluentProvider>);

const ACCESS_PERMISSION_OPTIONS: IAccessPermissionOption[] = [
  { value: 100000000, label: 'Standard', color: '#00B050' },
  { value: 100000001, label: 'Limited', color: '#FFC000' },
  { value: 100000002, label: 'Restricted', color: '#FF0000' },
];

function makeProps(overrides?: Partial<ITrackingFieldTrioProps>): ITrackingFieldTrioProps {
  return {
    monitor: false,
    highPriority: false,
    accessPermission: 100000000,
    accessPermissionOptions: ACCESS_PERMISSION_OPTIONS,
    monitorLabel: 'Monitor',
    highPriorityLabel: 'High Priority',
    accessPermissionLabel: 'Access Permission',
    onMonitorChange: jest.fn(),
    onHighPriorityChange: jest.fn(),
    onAccessPermissionChange: jest.fn(),
    ...overrides,
  };
}

const COMPOSER_ACTIONS = { name: 'Composer actions' } as const;
const BFF = 'https://bff.example.com';
const RECORD_ID = '6f1c2d3e-4a5b-4c6d-8e7f-90a1b2c3d4e5';
const GRANTEE_ID = 'aaaaaaaa-0000-0000-0000-000000000002';
const USER_ID = 'cccccccc-0000-0000-0000-000000000001';

function candidate(email?: string, n = 1): IAccessGrantCandidate {
  return { contactId: `aaaaaaaa-0000-0000-0000-00000000010${n}`, fullName: `Person ${n}`, email, role: 'Role' };
}

/** Role contacts only — a host that is not a grant root (`index.ts` passes no `root`). */
const rolesOnly = (candidates: IAccessGrantCandidate[]): IEmailMembersSources => ({
  readRoleContacts: async () => candidates,
});

function json(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as unknown as Response;
}

/** A grant root whose role contact, grantee and shared user all have emails; `grantsFail` makes the grants read 403. */
function rootSources(opts: { grantsFail?: boolean; everythingFails?: boolean } = {}): IEmailMembersSources {
  const fail = async (): Promise<never> => {
    throw new Error('403');
  };
  return {
    readRoleContacts: opts.everythingFails ? fail : async () => [candidate('rita@example.com')],
    readContactEmails: async () => new Map([[GRANTEE_ID, 'gary@example.com']]),
    root: {
      recordType: 'project',
      recordId: RECORD_ID,
      authenticatedFetch: jest.fn(async (url: string) => {
        if (opts.everythingFails) return json({}, 500);
        if (url.includes('/user-shares')) {
          return json({ shares: [{ systemUserId: USER_ID, fullName: 'Uma User', accessLevel: 100000000 }] });
        }
        return json({
          recordType: 'sprk_project',
          recordId: RECORD_ID,
          secure: 'doesNotApply',
          noAccess: 'doesNotApply',
          entriesState: 'complete',
          entries: [],
        });
      }) as unknown as AuthenticatedFetchFn,
      readGrants:
        opts.grantsFail || opts.everythingFails
          ? fail
          : async () => [{ accessRecordId: 'g1', contactId: GRANTEE_ID, fullName: 'Gary Grantee', accessLevel: 1 }],
      readUserEmails: async () => new Map([[USER_ID, 'uma@spaarke.example']]),
      accessPermissionState: 'standard',
      isSecureRecord: false,
    },
  };
}

/**
 * Test-harness reproduction of `TrackingFieldTrio/index.ts`'s `openEmailMembers` click handler: the shared resolver's
 * answer opens the canonical `SendEmailDialog` (recipients + notice), or the message dialog (`empty` / `failed`).
 */
function EmailMembersHarness({
  sources,
  authenticatedFetch,
}: {
  sources: IEmailMembersSources;
  authenticatedFetch: AuthenticatedFetchFn;
}) {
  const [dialogOpen, setDialogOpen] = React.useState(false);
  const [message, setMessage] = React.useState<'empty' | 'failed' | null>(null);
  const [recipients, setRecipients] = React.useState<string[]>([]);
  const [notice, setNotice] = React.useState<string | null>(null);

  const onOpenEmailMembers = React.useCallback(() => {
    void (async () => {
      const resolution: EmailMembersResolution = await resolveEmailMembersRecipients(sources);
      if (resolution.kind === 'recipients') {
        setRecipients(resolution.recipients);
        setNotice(resolution.notice);
        setDialogOpen(true);
      } else {
        setMessage(resolution.kind);
      }
    })();
  }, [sources]);

  return (
    <>
      <TrackingFieldTrio {...makeProps({ onOpenGrantModal: jest.fn(), onOpenEmailMembers })} />
      <SendEmailDialog
        open={dialogOpen}
        onClose={() => setDialogOpen(false)}
        initialTo={recipients}
        notice={notice ?? undefined}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={BFF}
        titleOverride="Email Members"
      />
      {message && (
        <div role="status" aria-label={`Email members ${message}`}>
          {message === 'failed' ? EMAIL_MEMBERS_FAILED_MESSAGE : EMAIL_MEMBERS_EMPTY_MESSAGE}
          <button onClick={() => setMessage(null)}>OK</button>
        </div>
      )}
    </>
  );
}

describe('TrackingFieldTrio — email-members action (task 042; task 196)', () => {
  const authenticatedFetch = jest.fn() as unknown as AuthenticatedFetchFn;

  describe('email-icon-opens-prepopulated-dialog', () => {
    it('opens SendEmailDialog pre-populated with the record role-contact emails', async () => {
      renderWithTheme(
        <EmailMembersHarness
          authenticatedFetch={authenticatedFetch}
          sources={rolesOnly([candidate('alice@example.com', 1), candidate('bob@example.com', 2)])}
        />
      );

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));

      await waitFor(() => expect(screen.getByRole('alertdialog')).toBeInTheDocument());
      expect(screen.getByRole('region', COMPOSER_ACTIONS)).toBeInTheDocument();

      const toGroup = screen.getByRole('group', { name: 'To' });
      expect(within(toGroup).getByText('alice@example.com')).toBeInTheDocument();
      expect(within(toGroup).getByText('bob@example.com')).toBeInTheDocument();
      expect(screen.queryByTestId('send-email-notice')).not.toBeInTheDocument();
    });

    it('task 196: includes the people with an active grant and the users the record is shared with', async () => {
      renderWithTheme(<EmailMembersHarness authenticatedFetch={authenticatedFetch} sources={rootSources()} />);

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));

      const toGroup = await screen.findByRole('group', { name: 'To' });
      for (const email of ['rita@example.com', 'gary@example.com', 'uma@spaarke.example']) {
        expect(within(toGroup).getByText(email)).toBeInTheDocument();
      }
      expect(screen.queryByTestId('send-email-notice')).not.toBeInTheDocument();
    });

    it('task 196: a source that could not be read opens the dialog with what was read AND a visible notice', async () => {
      renderWithTheme(
        <EmailMembersHarness authenticatedFetch={authenticatedFetch} sources={rootSources({ grantsFail: true })} />
      );

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));

      const toGroup = await screen.findByRole('group', { name: 'To' });
      expect(within(toGroup).getByText('rita@example.com')).toBeInTheDocument();
      expect(within(toGroup).getByText('uma@spaarke.example')).toBeInTheDocument();
      expect(screen.getByTestId('send-email-notice')).toHaveTextContent(
        'Some people may be missing from the recipients: the people granted access could not be read.'
      );
    });

    it('task 196: every source failing shows the error, never the empty state or the composer', async () => {
      renderWithTheme(
        <EmailMembersHarness authenticatedFetch={authenticatedFetch} sources={rootSources({ everythingFails: true })} />
      );

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));

      expect(await screen.findByLabelText('Email members failed')).toHaveTextContent(EMAIL_MEMBERS_FAILED_MESSAGE);
      expect(screen.queryByLabelText('Email members empty')).not.toBeInTheDocument();
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });

    it('dedupes candidates that share an email and drops candidates with no email', async () => {
      renderWithTheme(
        <EmailMembersHarness
          authenticatedFetch={authenticatedFetch}
          sources={rolesOnly([
            candidate('alice@example.com', 1),
            candidate('alice@example.com', 2), // duplicate — same recipient assigned two roles
            candidate(undefined, 3), // no email — cannot be pre-populated
            candidate('   ', 4), // blank — treated as no email
          ])}
        />
      );

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));

      const toGroup = await screen.findByRole('group', { name: 'To' });
      // Exactly one chip for the deduped recipient.
      expect(within(toGroup).getAllByText('alice@example.com')).toHaveLength(1);
    });

    it('does not open the dialog and shows the empty state when no one has an email (acceptance criterion 2)', async () => {
      renderWithTheme(
        <EmailMembersHarness authenticatedFetch={authenticatedFetch} sources={rolesOnly([candidate(undefined)])} />
      );

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));

      await waitFor(() =>
        expect(screen.getByLabelText('Email members empty')).toHaveTextContent(
          'This record has no members with an email address yet. Grant access or assign a role first.'
        )
      );
      // No sendable composer with zero recipients was opened.
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
      expect(screen.queryByRole('region', COMPOSER_ACTIONS)).not.toBeInTheDocument();
    });

    it('shows the empty state for a record with no members at all', async () => {
      renderWithTheme(<EmailMembersHarness authenticatedFetch={authenticatedFetch} sources={rolesOnly([])} />);

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));

      await waitFor(() => expect(screen.getByLabelText('Email members empty')).toBeInTheDocument());
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
  });

  describe('adr-021-dark-mode', () => {
    it('renders the email icon and the pre-populated dialog (with a notice) under webDarkTheme', async () => {
      renderWithTheme(
        <EmailMembersHarness authenticatedFetch={authenticatedFetch} sources={rootSources({ grantsFail: true })} />,
        webDarkTheme
      );

      const emailButton = screen.getByRole('button', { name: 'Email members' });
      expect(emailButton).toBeInTheDocument();

      fireEvent.click(emailButton);

      await waitFor(() => expect(screen.getByRole('alertdialog')).toBeInTheDocument());
      const toGroup = screen.getByRole('group', { name: 'To' });
      expect(within(toGroup).getByText('rita@example.com')).toBeInTheDocument();
      expect(screen.getByTestId('send-email-notice')).toBeInTheDocument();
    });
  });

  describe('console-error-check', () => {
    it('renders + opens the pre-populated dialog with zero console errors/warnings — light theme', async () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      renderWithTheme(
        <EmailMembersHarness
          authenticatedFetch={authenticatedFetch}
          sources={rolesOnly([candidate('alice@example.com')])}
        />
      );
      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));
      await waitFor(() => expect(screen.getByRole('alertdialog')).toBeInTheDocument());

      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });

    it('renders + shows the empty state with zero console errors/warnings — dark theme', async () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      renderWithTheme(
        <EmailMembersHarness authenticatedFetch={authenticatedFetch} sources={rolesOnly([])} />,
        webDarkTheme
      );
      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));
      await waitFor(() => expect(screen.getByLabelText('Email members empty')).toBeInTheDocument());

      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });
  });
});
