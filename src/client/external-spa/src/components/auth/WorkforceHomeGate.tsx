/**
 * WorkforceHomeGate - registers or links a signed-in workforce contact before the workspace shows
 * (spaarke-SPA-external-access-platform-r3 task 058; ADR-028 Amendment A9; design notes/a9-bff-design.md section 5).
 *
 * Active ONLY on the workforce HOME plane (`isWorkforceHomePlaneActive()`): the browser "company account" sign-in AND the
 * Teams tab (task 075, ADR-028 A9 Teams amendment), which sets the same flag; the CIAM plane renders its children at once,
 * with no request. It sits inside `AuthGuard` (in Teams the bootstrap already holds a token for the tenant's backend). Order:
 *
 *   1. GET /api/v1/external/me. A contact the BFF knows -> the workspace shows.
 *   2. 403 `registration_required` (the first time only): try the STORED LINK, silently - a Spaarke-tenant guest token
 *      (`acquireSpaarkeGuestToken('silent')`, the home account as login hint) goes in the body of
 *      POST /api/v1/external/workforce/link, then /me is read again. No token ({@link afterSilentLinkFailure}), or the guest
 *      has no licensed user (`no_systemuser`) -> the profile form (`RegistrationPage`). Any other refusal -> an
 *      administrator must resolve it. In Teams the silent token comes through NAA (`setGuestLinkHost('teams')`).
 *   3. A second `registration_required` always shows the form (never a loop). The form registers through the BFF (or, for
 *      a licensed user whose silent link failed, links through the popup), then /me is read again.
 *
 * 401 -> the sign-in state; any other 403 -> the denied state; 5xx or a network fault -> a generic error with a retry.
 * `onReady(true)` tells the shell the workspace is showing (the notification bell waits for it, so it never reads the BFF
 * for a person who is not registered yet).
 *
 * ADR-021: Fluent v9 semantic tokens only.
 */
import * as React from 'react';
import {
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
} from '@fluentui/react-components';
import { useMsal } from '@azure/msal-react';
import { accountsForActivePlane, acquireSpaarkeGuestToken, isWorkforceHomePlaneActive } from '../../auth/msal-auth';
import { getActiveBackend } from '../../auth/backend-selection';
import { getExternalUserContext } from '../../auth/bff-client';
import {
  linkWorkforceAccount,
  parseRegistrationRequired,
  type RegistrationPrefill,
} from '../../auth/registration-client';
import { BffAccessState, accessFailureOf, type BffAccessFailure } from './BffAccessState';
import { LinkBlockedNotice, RegistrationPage } from '../../pages/RegistrationPage';

/** True when the gate does anything: the workforce home plane, in a browser or in Teams. CIAM renders its children directly. */
export function isHomeGateActive(): boolean {
  return isWorkforceHomePlaneActive();
}

/**
 * THE switch point for the link popup (task 075, AC3; owner-confirmed 2026-10-19): what the gate shows when the SILENT link
 * produced no token. The answer is the profile form, in the browser and in Teams alike. The Spaarke-sign-in popup is NOT
 * opened here: it opens only from the "Link my account" click on `RegistrationPage`, after the registration POST returns
 * `licensed_user_link_required`. A gesture-less popup here would greet every new contact (not a guest of Spaarke's tenant)
 * with a sign-in window that must fail. To change the decision, change this one function.
 */
export function afterSilentLinkFailure(prefill: RegistrationPrefill): GateState {
  return { kind: 'register', prefill };
}

export interface WorkforceHomeGateProps {
  /** Called with `false` while the gate holds the workspace back and `true` once it shows. Not called when inactive. */
  onReady?: (ready: boolean) => void;
  children: React.ReactNode;
}

export type GateState =
  | { kind: 'checking' }
  | { kind: 'linking' }
  | { kind: 'register'; prefill: RegistrationPrefill }
  | { kind: 'linkBlocked' }
  | { kind: 'retry' }
  | { kind: 'access'; failure: BffAccessFailure }
  | { kind: 'ready' };

export const WorkforceHomeGate: React.FC<WorkforceHomeGateProps> = ({ onReady, children }) => {
  if (!isHomeGateActive()) return <>{children}</>;
  return <ActiveGate onReady={onReady}>{children}</ActiveGate>;
};

const Centered: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', flex: '1 1 auto' }}>{children}</div>
);

const ActiveGate: React.FC<{ onReady?: (ready: boolean) => void; children: React.ReactNode }> = ({
  onReady,
  children,
}) => {
  const { instance, accounts } = useMsal();
  const [state, setState] = React.useState<GateState>({ kind: 'checking' });
  // The latest run wins: a superseded run (StrictMode's second effect, a retry) never sets state.
  const runId = React.useRef(0);

  const loginHint = React.useMemo(() => {
    const own = accountsForActivePlane(accounts);
    const activeId = instance.getActiveAccount?.()?.homeAccountId;
    return (own.find(a => a.homeAccountId === activeId) ?? own[0])?.username;
  }, [accounts, instance]);

  /** Read /me; on `registration_required` try the silent link once (`allowLink`), else show the form. */
  const probe = React.useCallback(
    async (allowLink: boolean) => {
      const id = ++runId.current;
      const current = () => runId.current === id;
      setState({ kind: 'checking' });
      try {
        await getExternalUserContext();
        if (current()) setState({ kind: 'ready' });
        return;
      } catch (err) {
        if (!current()) return;
        const prefill = parseRegistrationRequired(err);
        if (!prefill) {
          const failure = accessFailureOf(err);
          setState(failure ? { kind: 'access', failure } : { kind: 'retry' });
          return;
        }
        if (!allowLink || !loginHint) {
          setState({ kind: 'register', prefill });
          return;
        }

        setState({ kind: 'linking' });
        let token: string | null = null;
        try {
          token = await acquireSpaarkeGuestToken('silent', loginHint, getActiveBackend().bffScope);
        } catch {
          token = null;
        }
        if (!current()) return;
        if (!token) {
          setState(afterSilentLinkFailure(prefill));
          return;
        }
        const link = await linkWorkforceAccount(token);
        if (!current()) return;
        if (link.kind === 'ok') {
          void probe(false);
          return;
        }
        switch (link.reason) {
          case 'no_systemuser':
            setState({ kind: 'register', prefill });
            break;
          case 'signin':
            setState({ kind: 'access', failure: 'signin' });
            break;
          case 'unavailable':
            setState({ kind: 'retry' });
            break;
          case 'blocked':
          default:
            setState({ kind: 'linkBlocked' });
        }
      }
    },
    [loginHint]
  );

  React.useEffect(() => {
    const run = runId;
    void probe(true);
    // Unmount (or StrictMode's second effect) supersedes the in-flight run.
    return () => {
      run.current++;
    };
    // The first probe only: later probes are started by the retry button and by the form's completion.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  React.useEffect(() => {
    onReady?.(state.kind === 'ready');
  }, [state.kind, onReady]);

  switch (state.kind) {
    case 'ready':
      return <>{children}</>;
    case 'checking':
      return (
        <Centered>
          <Spinner size="large" label="Checking your account..." />
        </Centered>
      );
    case 'linking':
      return (
        <Centered>
          <Spinner size="large" label="Linking your account..." />
        </Centered>
      );
    case 'register':
      return <RegistrationPage prefill={state.prefill} loginHint={loginHint} onDone={() => void probe(false)} />;
    case 'linkBlocked':
      return <LinkBlockedNotice />;
    case 'access':
      return <BffAccessState failure={state.failure} />;
    case 'retry':
    default:
      return (
        <Centered>
          <MessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>Something went wrong</MessageBarTitle>
              We could not check your account. Please try again.
            </MessageBarBody>
            <MessageBarActions>
              <Button appearance="primary" onClick={() => void probe(true)}>
                Try again
              </Button>
            </MessageBarActions>
          </MessageBar>
        </Centered>
      );
  }
};

export default WorkforceHomeGate;
