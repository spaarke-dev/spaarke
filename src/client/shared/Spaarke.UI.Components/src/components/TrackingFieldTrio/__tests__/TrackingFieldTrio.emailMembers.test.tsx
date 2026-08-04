/**
 * TrackingFieldTrio — email-members action (task 042, teams-app-r1).
 *
 * `src/client/pcf/TrackingFieldTrio/index.ts` (task 042) wires the shared
 * core's `onOpenEmailMembers` callback (task 040) to:
 *   1. resolve the record's membership contacts via `fetchCandidates()` —
 *      the SAME allowlist-filtered `sprk_assigned*` data source task 041
 *      wired for the grant modal (no separate recipient-derivation rule);
 *   2. drop any candidate with no populated email, dedupe by email;
 *   3. open the canonical `SendEmailDialog` (`EmailComposer` engine,
 *      `@spaarke/ui-components`, ADR-045) pre-populated with those emails —
 *      OR, if the resulting list is empty, show an empty-state alert
 *      INSTEAD of opening a dialog with zero recipients.
 *
 * `index.ts` is a PCF `ComponentFramework.StandardControl` class (not a
 * renderable React component / not resolvable from this package's Jest
 * environment — it imports `./generated/ManifestTypes` and PCF-only globals),
 * so it is not directly unit-testable here (same constraint task 040/041
 * worked under). This suite instead mounts the REAL, unmodified
 * `TrackingFieldTrio` core + the REAL canonical `SendEmailDialog` behind a
 * thin harness component that reproduces `index.ts`'s decision logic
 * verbatim (see the inline comment on `resolveEmailMembersRecipients` below,
 * which mirrors `index.ts`'s private method of the same name) — so what's
 * under test is the real toolbar affordance + the real canonical dialog
 * receiving genuinely pre-populated recipients, not a mocked stand-in.
 *
 * Covers the task's `<ui-tests>`:
 *  - email-icon-opens-prepopulated-dialog
 *  - adr-021-dark-mode
 *  - console-error-check
 * ...and acceptance criterion 2 (empty-membership empty state, no sendable
 * dialog with zero recipients).
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { TrackingFieldTrio } from '../TrackingFieldTrio';
import type { ITrackingFieldTrioProps, IAccessPermissionOption } from '../types';
import { SendEmailDialog } from '../../EmailComposer';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';

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

interface IMembershipCandidate {
  email?: string;
}

/**
 * Test-harness reproduction of `TrackingFieldTrio/index.ts`'s
 * `resolveEmailMembersRecipients()` + `onOpenEmailMembers` click handler
 * (task 042): reuses the injected candidate list (standing in for
 * `fetchCandidates()`), drops candidates without a populated email, dedupes
 * by email, then either opens the canonical `SendEmailDialog` pre-populated
 * with those emails or flags the empty state — never both, never a dialog
 * with zero recipients.
 */
function EmailMembersHarness({
  candidates,
  authenticatedFetch,
}: {
  candidates: IMembershipCandidate[];
  authenticatedFetch: AuthenticatedFetchFn;
}) {
  const [dialogOpen, setDialogOpen] = React.useState(false);
  const [emptyOpen, setEmptyOpen] = React.useState(false);
  const [recipients, setRecipients] = React.useState<string[]>([]);

  const resolveEmailMembersRecipients = React.useCallback(async (): Promise<string[]> => {
    const emails = new Set<string>();
    for (const candidate of candidates) {
      if (candidate.email && candidate.email.trim().length > 0) {
        emails.add(candidate.email.trim());
      }
    }
    return Array.from(emails);
  }, [candidates]);

  const onOpenEmailMembers = React.useCallback(() => {
    void (async () => {
      const resolved = await resolveEmailMembersRecipients();
      if (resolved.length === 0) {
        setEmptyOpen(true);
      } else {
        setRecipients(resolved);
        setDialogOpen(true);
      }
    })();
  }, [resolveEmailMembersRecipients]);

  return (
    <>
      <TrackingFieldTrio {...makeProps({ onOpenGrantModal: jest.fn(), onOpenEmailMembers })} />
      <SendEmailDialog
        open={dialogOpen}
        onClose={() => setDialogOpen(false)}
        initialTo={recipients}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={BFF}
        titleOverride="Email Members"
      />
      {emptyOpen && (
        <div role="status" aria-label="Email members empty state">
          This record has no membership contacts with an email address yet.
          <button onClick={() => setEmptyOpen(false)}>OK</button>
        </div>
      )}
    </>
  );
}

describe('TrackingFieldTrio — email-members action (task 042)', () => {
  const authenticatedFetch = jest.fn() as unknown as AuthenticatedFetchFn;

  describe('email-icon-opens-prepopulated-dialog', () => {
    it('opens SendEmailDialog pre-populated with the record membership-contact emails', async () => {
      renderWithTheme(
        <EmailMembersHarness
          authenticatedFetch={authenticatedFetch}
          candidates={[{ email: 'alice@example.com' }, { email: 'bob@example.com' }]}
        />
      );

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));

      await waitFor(() => expect(screen.getByRole('alertdialog')).toBeInTheDocument());
      expect(screen.getByRole('region', COMPOSER_ACTIONS)).toBeInTheDocument();

      const toGroup = screen.getByRole('group', { name: 'To' });
      expect(within(toGroup).getByText('alice@example.com')).toBeInTheDocument();
      expect(within(toGroup).getByText('bob@example.com')).toBeInTheDocument();
    });

    it('dedupes candidates that share an email and drops candidates with no email', async () => {
      renderWithTheme(
        <EmailMembersHarness
          authenticatedFetch={authenticatedFetch}
          candidates={[
            { email: 'alice@example.com' },
            { email: 'alice@example.com' }, // duplicate — same recipient assigned two roles
            { email: undefined }, // no email — cannot be pre-populated
            { email: '   ' }, // blank — treated as no email
          ]}
        />
      );

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));

      const toGroup = await screen.findByRole('group', { name: 'To' });
      // Exactly one chip for the deduped recipient.
      expect(within(toGroup).getAllByText('alice@example.com')).toHaveLength(1);
    });

    it('does not open the dialog and shows an empty state when no candidate has an email (acceptance criterion 2)', async () => {
      renderWithTheme(
        <EmailMembersHarness authenticatedFetch={authenticatedFetch} candidates={[{ email: undefined }]} />
      );

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));

      await waitFor(() =>
        expect(screen.getByLabelText('Email members empty state')).toBeInTheDocument()
      );
      // No sendable composer with zero recipients was opened.
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
      expect(screen.queryByRole('region', COMPOSER_ACTIONS)).not.toBeInTheDocument();
    });

    it('shows the empty state for a record with no membership contacts at all', async () => {
      renderWithTheme(<EmailMembersHarness authenticatedFetch={authenticatedFetch} candidates={[]} />);

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));

      await waitFor(() =>
        expect(screen.getByLabelText('Email members empty state')).toBeInTheDocument()
      );
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
  });

  describe('adr-021-dark-mode', () => {
    it('renders the email icon and the pre-populated dialog under webDarkTheme without a broken state', async () => {
      renderWithTheme(
        <EmailMembersHarness authenticatedFetch={authenticatedFetch} candidates={[{ email: 'alice@example.com' }]} />,
        webDarkTheme
      );

      const emailButton = screen.getByRole('button', { name: 'Email members' });
      expect(emailButton).toBeInTheDocument();

      fireEvent.click(emailButton);

      await waitFor(() => expect(screen.getByRole('alertdialog')).toBeInTheDocument());
      const toGroup = screen.getByRole('group', { name: 'To' });
      expect(within(toGroup).getByText('alice@example.com')).toBeInTheDocument();
    });
  });

  describe('console-error-check', () => {
    it('renders + opens the pre-populated dialog with zero console errors/warnings — light theme', async () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      renderWithTheme(
        <EmailMembersHarness authenticatedFetch={authenticatedFetch} candidates={[{ email: 'alice@example.com' }]} />
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
        <EmailMembersHarness authenticatedFetch={authenticatedFetch} candidates={[]} />,
        webDarkTheme
      );
      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));
      await waitFor(() => expect(screen.getByLabelText('Email members empty state')).toBeInTheDocument());

      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });
  });
});
