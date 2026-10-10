/**
 * Task 113 (provisioning request R1): the dev-only sign-in Diagnostics view — named claims of the backend token plus
 * how the pane signed in, a Copy button, Refresh, Close; never the token itself. The "⋮ → Diagnostics" entry exists
 * only when a handler is supplied (a diagnostics-enabled build).
 */
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DiagnosticsView } from '../DiagnosticsView';
import { TaskPaneToolbar } from '../../TaskPaneToolbar';

const b64url = (value: string): string =>
  Buffer.from(value, 'utf8').toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const jwt = (payload: Record<string, unknown>): string =>
  `${b64url('{"alg":"RS256"}')}.${b64url(JSON.stringify(payload))}.sig`;

const GUEST_TOKEN = jwt({ tid: 'spaarke-tenant', oid: 'guest-oid', acct: 1, idp: 'https://sts.windows.net/home/' });

const renderView = (getAccessToken: () => Promise<string | null>, onClose = jest.fn()) =>
  render(
    <FluentProvider theme={webLightTheme}>
      <DiagnosticsView
        getAccessToken={getAccessToken}
        signIn={{ authority: 'https://login.microsoftonline.com/spaarke-tenant', naaActive: true }}
        hostDescription="Word · PC · 16.0"
        onClose={onClose}
      />
    </FluentProvider>
  );

describe('DiagnosticsView (task 113)', () => {
  it('shows the claims and the sign-in context, never the token', async () => {
    const { container } = renderView(() => Promise.resolve(GUEST_TOKEN));

    expect(await screen.findByText('1 — guest')).toBeInTheDocument();
    expect(screen.getByText('spaarke-tenant')).toBeInTheDocument();
    expect(screen.getByText('guest-oid')).toBeInTheDocument();
    expect(screen.getByText('https://sts.windows.net/home/')).toBeInTheDocument();
    expect(screen.getByText('NAA (Office broker)')).toBeInTheDocument();
    expect(screen.getByText('https://login.microsoftonline.com/spaarke-tenant')).toBeInTheDocument();
    expect(screen.getByText('Word · PC · 16.0')).toBeInTheDocument();
    expect(container.textContent).not.toContain(GUEST_TOKEN.split('.')[1] as string);
  });

  it('Copy writes the rows as text (no token); Refresh reads a fresh token; Close closes', async () => {
    const writeText = jest.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });
    const getAccessToken = jest.fn().mockResolvedValue(GUEST_TOKEN);
    const onClose = jest.fn();
    renderView(getAccessToken, onClose);
    await screen.findByText('1 — guest');

    fireEvent.click(screen.getByRole('button', { name: 'Copy' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledTimes(1));
    const copied = writeText.mock.calls[0]?.[0] as string;
    expect(copied).toContain('acct (account status): 1 — guest');
    expect(copied).toContain('Sign-in path: NAA (Office broker)');
    expect(copied).not.toContain(GUEST_TOKEN.split('.')[1] as string);
    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(getAccessToken).toHaveBeenCalledTimes(2));

    fireEvent.click(screen.getByRole('button', { name: 'Close diagnostics' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('no token: says so and shows every claim as not present', async () => {
    renderView(() => Promise.resolve(null));
    expect(await screen.findByRole('alert')).toHaveTextContent('No access token');
    expect(screen.getAllByText('(not present)').length).toBeGreaterThan(0);
  });

  it('a token failure is shown, not thrown', async () => {
    renderView(() => Promise.reject(new Error('interaction_required')));
    expect(await screen.findByRole('alert')).toHaveTextContent('interaction_required');
  });
});

describe('"⋮ → Diagnostics" menu entry (task 113)', () => {
  const toolbar = (onShowDiagnostics?: () => void) =>
    render(
      <FluentProvider theme={webLightTheme}>
        <TaskPaneToolbar
          hostType="word"
          isAuthenticated
          selectedTab="save"
          onThemeChange={() => undefined}
          {...(onShowDiagnostics ? { onShowDiagnostics } : {})}
        />
      </FluentProvider>
    );

  it('is offered only when a handler is supplied, and opens the view', async () => {
    const onShowDiagnostics = jest.fn();
    toolbar(onShowDiagnostics);
    fireEvent.click(screen.getByRole('button', { name: 'More options' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Diagnostics' }));
    expect(onShowDiagnostics).toHaveBeenCalledTimes(1);
  });

  it('is absent without a handler (a build without the diagnostics flag)', async () => {
    toolbar();
    fireEvent.click(screen.getByRole('button', { name: 'More options' }));
    await screen.findByRole('menuitem', { name: 'Theme' });
    expect(screen.queryByRole('menuitem', { name: 'Diagnostics' })).toBeNull();
  });
});
