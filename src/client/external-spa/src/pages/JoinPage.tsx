/**
 * JoinPage — `/join?customer={key}`, the start and return page of workforce-contact self-registration
 * (spaarke-SPA-external-access-platform-r3 task 056; spec FR-13; Option B, owner 2026-10-11).
 *
 * A PRE-SIGN-IN page: main.tsx renders it before the realm chooser and before any MSAL instance exists. It never
 * acquires a token. All logic lives in `auth/registration-client.ts`; this component only picks the state to show:
 *
 *   flag off, or no service URL configured  → "not available" (the key is not even looked up)
 *   inside Teams                             → "open this link in your browser" (browser-only flow; no redirect inside
 *                                              the Teams iframe, NFR-04)
 *   key missing / malformed / unknown        → "link not recognised" (one neutral message; no list, no echo)
 *   known key, first landing                 → intro + "Continue with your work account" → registration service
 *   known key, service return                → the outcome (registered, already registered, refusals, error), after
 *                                              the one-time `state` check; a mismatch shows the neutral "start again"
 *
 * "Sign in" on a registered / already-registered outcome hands off to the normal workforce sign-in for that customer.
 *
 * ADR-021: Fluent v9 semantic tokens only (light and dark). ADR-012: the shared `EmptyState` shell renders every state.
 */
import * as React from 'react';
import { Button, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import {
  CheckmarkCircle48Regular,
  DismissCircle48Regular,
  ErrorCircle48Regular,
  Globe48Regular,
  Link48Regular,
  PersonAdd48Regular,
  Warning48Regular,
} from '@fluentui/react-icons';
import { EmptyState } from '@spaarke/ui-components/components/EmptyState';
import type { BackendDirectory } from '../auth/backend-selection';
import {
  JOIN_PATH,
  assignLocation,
  consumeJoinState,
  getRegistrationServiceUrl,
  isJoinEnabled,
  parseJoinReturn,
  resolveJoinCustomer,
  startRegistration,
  startWorkforceSignIn,
  type JoinResultCode,
  type Navigate,
} from '../auth/registration-client';

export interface JoinPageProps {
  /** True inside the Teams tab: the join flow is browser-only, so the page explains that and does nothing else. */
  teamsHost: boolean;
  /** Defaults to `window.location.search`. */
  search?: string;
  /** Defaults to the task-041 directory for the workforce plane. */
  directory?: BackendDirectory;
  /** Full-page navigation (registration start, sign-in hand-off). Defaults to `window.location.assign`. */
  navigate?: Navigate;
}

type ViewState =
  | { kind: 'unavailable' }
  | { kind: 'teams' }
  | { kind: 'checking' }
  | { kind: 'unknown' }
  | { kind: 'intro'; customerKey: string }
  | { kind: 'stale'; customerKey: string }
  | { kind: 'outcome'; customerKey: string; result: JoinResultCode };

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '100dvh',
    width: '100%',
    boxSizing: 'border-box',
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  description: {
    maxWidth: '28rem',
  },
  iconNeutral: { color: tokens.colorNeutralForeground3 },
  iconBrand: { color: tokens.colorBrandForeground1 },
  iconSuccess: { color: tokens.colorStatusSuccessForeground1 },
  iconWarning: { color: tokens.colorStatusWarningForeground1 },
  iconDanger: { color: tokens.colorStatusDangerForeground1 },
});

/** The first state, decided synchronously from configuration and host before any lookup. */
function initialState(teamsHost: boolean): ViewState {
  if (!isJoinEnabled() || !getRegistrationServiceUrl()) return { kind: 'unavailable' };
  if (teamsHost) return { kind: 'teams' };
  return { kind: 'checking' };
}

export const JoinPage: React.FC<JoinPageProps> = ({ teamsHost, search, directory, navigate = assignLocation }) => {
  const s = useStyles();
  const [view, setView] = React.useState<ViewState>(() => initialState(teamsHost));
  const [startFailed, setStartFailed] = React.useState(false);

  React.useEffect(() => {
    if (view.kind !== 'checking') return;
    let cancelled = false;
    const query = search ?? window.location.search;
    void (async () => {
      const customer = await resolveJoinCustomer(query, directory);
      if (cancelled) return;
      if (customer.kind === 'unknown') {
        setView({ kind: 'unknown' });
        return;
      }
      const { customerKey } = customer;
      const ret = parseJoinReturn(query);
      if (!ret) {
        setView({ kind: 'intro', customerKey });
        return;
      }
      const matched = consumeJoinState(ret.state, customerKey);
      // Drop result/state from the address bar: a reload starts again instead of replaying the outcome.
      try {
        window.history.replaceState(null, '', `${JOIN_PATH}?customer=${encodeURIComponent(customerKey)}`);
      } catch {
        // history unavailable: harmless, the state is already consumed.
      }
      setView(matched ? { kind: 'outcome', customerKey, result: ret.result } : { kind: 'stale', customerKey });
    })();
    return () => {
      cancelled = true;
    };
  }, [view.kind, search, directory]);

  const register = React.useCallback(
    (customerKey: string) => {
      try {
        startRegistration(customerKey, navigate);
      } catch (err) {
        console.error('[Join] Registration could not start:', err);
        setStartFailed(true);
      }
    },
    [navigate]
  );

  const signIn = React.useCallback(
    (customerKey: string) => {
      startWorkforceSignIn(customerKey, navigate);
    },
    [navigate]
  );

  const render = (
    icon: React.ReactElement,
    heading: string,
    description: string,
    footer?: React.ReactNode
  ): React.ReactElement => (
    <div className={s.root}>
      <EmptyState
        icon={icon}
        heading={heading}
        description={description}
        descriptionClassName={s.description}
        footer={footer}
      />
    </div>
  );

  const startButton = (customerKey: string, label = 'Try again') => (
    <Button appearance="primary" onClick={() => register(customerKey)}>
      {label}
    </Button>
  );

  const renderOutcome = (customerKey: string, result: JoinResultCode): React.ReactElement => {
    const signInButton = (
      <Button appearance="primary" onClick={() => signIn(customerKey)}>
        Sign in
      </Button>
    );
    switch (result) {
      case 'registered':
        return render(
          <CheckmarkCircle48Regular className={s.iconSuccess} />,
          'You’re registered',
          'Your account is set up. Sign in with the same work account to open Spaarke. The first time, Microsoft ' +
            'may ask you to review permissions.',
          signInButton
        );
      case 'already_registered':
        return render(
          <CheckmarkCircle48Regular className={s.iconSuccess} />,
          'You’re already registered — sign in',
          'This work account already has access. Sign in to open Spaarke.',
          signInButton
        );
      case 'tenant_not_allowed':
        return render(
          <DismissCircle48Regular className={s.iconDanger} />,
          'Your company is not set up for access',
          'Your company’s account isn’t on the list for this link, so you can’t join with it. Contact the person ' +
            'who sent you this link.'
        );
      case 'guest_account':
        return render(
          <DismissCircle48Regular className={s.iconDanger} />,
          'Use your own company account',
          'You signed in with a guest account from another organization. Sign in with the account your own ' +
            'company gave you.',
          startButton(customerKey, 'Try a different account')
        );
      case 'account_conflict':
        return render(
          <DismissCircle48Regular className={s.iconDanger} />,
          'This account can’t join this way',
          'This account can’t be registered through a join link. Contact the person who sent you this link.'
        );
      case 'consent_required':
        return render(
          <Warning48Regular className={s.iconWarning} />,
          'Your company needs to approve Spaarke',
          'Your company only lets an administrator approve new apps. Ask your IT administrator to approve ' +
            '“Spaarke Registration”, then try again.',
          startButton(customerKey)
        );
      case 'cancelled':
        return render(
          <Warning48Regular className={s.iconWarning} />,
          'Sign-in was cancelled',
          'You didn’t finish signing in, so nothing was set up.',
          startButton(customerKey)
        );
      case 'error':
      default:
        return render(
          <ErrorCircle48Regular className={s.iconDanger} />,
          'Something went wrong',
          'We couldn’t finish setting up your access. Please try again.',
          startButton(customerKey)
        );
    }
  };

  if (startFailed) {
    return render(
      <ErrorCircle48Regular className={s.iconDanger} />,
      'Joining isn’t available right now',
      'Please try again later. If the problem continues, contact the person who sent you this link.'
    );
  }

  switch (view.kind) {
    case 'unavailable':
      return render(
        <Globe48Regular className={s.iconNeutral} />,
        'Joining isn’t available',
        'Self-registration is not available yet. Contact the person who sent you this link.'
      );
    case 'teams':
      return render(
        <Globe48Regular className={s.iconNeutral} />,
        'Open this link in your browser',
        'Joining Spaarke works only in a web browser, not inside Microsoft Teams. Copy the link you were sent and ' +
          'open it in your browser.'
      );
    case 'checking':
      return (
        <div className={s.root}>
          <Spinner size="large" label="Checking your link..." />
        </div>
      );
    case 'unknown':
      return render(
        <Link48Regular className={s.iconNeutral} />,
        'Link not recognised',
        'This join link isn’t valid. Check that you used the full link you were sent, or ask for a new one.'
      );
    case 'intro':
      return render(
        <PersonAdd48Regular className={s.iconBrand} />,
        'Join Spaarke',
        'Sign in with your company work account. Spaarke checks that your company is set up for access, then ' +
          'takes you to sign in.',
        startButton(view.customerKey, 'Continue with your work account')
      );
    case 'stale':
      return render(
        <Warning48Regular className={s.iconWarning} />,
        'Let’s start again',
        'We couldn’t match this page to a sign-up started in this browser tab. Start again to continue.',
        startButton(view.customerKey, 'Start again')
      );
    case 'outcome':
      return renderOutcome(view.customerKey, view.result);
  }
};

export default JoinPage;
