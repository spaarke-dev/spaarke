/**
 * TeamsAuthError — the fail-loud sign-in error screen for a Teams bootstrap failure (acceptance criterion 4:
 * never silently degrade to a blank tab).
 *
 * Bootstrap-path infrastructure, not a feature component (it is rendered by main.tsx when Teams workforce auth
 * could not be established at all). Fluent v9 semantic tokens only (ADR-021).
 *
 * "Sign in" runs the MSAL popup path ON CLICK. The bootstrap popup fallback has no user gesture, so a Teams
 * webview or browser may block it (`popup_window_error`); a click is a gesture, so the user can recover here
 * without reloading the tab. On success the caller (main.tsx) renders the app and this screen unmounts; on a
 * failure the new diagnostic replaces the old one and the button stays available.
 */
import * as React from 'react';
import { Button, Text, makeStyles, tokens } from '@fluentui/react-components';
import { extractAuthDiagnostics } from '../../auth/auth-diagnostics';

export interface TeamsAuthErrorProps {
  /** The failure message (`TeamsWorkforceAuthError.message`). */
  message: string;
  /** Pre-formatted diagnostic detail (`extractAuthDiagnostics`) shown in the monospace block. */
  diagnostics: string;
  /**
   * Run the MSAL popup sign-in and, on success, continue the bootstrap. Called synchronously from the
   * button's click handler so the popup opens from a user gesture. Reject to keep the screen up.
   */
  onSignIn: () => Promise<void>;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100dvh',
    rowGap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
    textAlign: 'center',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  message: {
    color: tokens.colorNeutralForeground3,
    maxWidth: '32rem',
  },
  hint: {
    color: tokens.colorNeutralForeground4,
    maxWidth: '32rem',
  },
  diagnostics: {
    marginTop: tokens.spacingVerticalM,
    maxWidth: '46rem',
    maxHeight: '14rem',
    overflow: 'auto',
    textAlign: 'left',
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground3,
    backgroundColor: tokens.colorNeutralBackground3,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingHorizontalM,
  },
});

export const TeamsAuthError: React.FC<TeamsAuthErrorProps> = ({ message, diagnostics, onSignIn }) => {
  const s = useStyles();
  const [busy, setBusy] = React.useState(false);
  const [retry, setRetry] = React.useState<{ message: string; diagnostics: string } | null>(null);
  const mounted = React.useRef(true);
  React.useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const handleSignIn = (): void => {
    setBusy(true);
    // onSignIn is invoked synchronously inside the click handler: the popup it opens keeps the user gesture.
    onSignIn().then(
      () => {
        if (mounted.current) setBusy(false);
      },
      (err: unknown) => {
        if (!mounted.current) return;
        setBusy(false);
        setRetry({
          message: err instanceof Error ? err.message : 'Sign-in failed.',
          diagnostics: extractAuthDiagnostics(err),
        });
      }
    );
  };

  const shownMessage = retry?.message ?? message;
  const shownDiagnostics = retry?.diagnostics ?? diagnostics;

  return (
    <div className={s.root}>
      <Text size={500} weight="semibold">
        Sign-in failed
      </Text>
      <Text size={300} className={s.message}>
        {shownMessage}
      </Text>
      <Button appearance="primary" disabled={busy} onClick={handleSignIn}>
        {busy ? 'Signing in...' : 'Sign in'}
      </Button>
      <Text size={200} className={s.hint}>
        Sign in with your work account for your organization. If it keeps failing, contact your administrator.
      </Text>
      <pre className={s.diagnostics}>{`Diagnostic detail (share this):\n${shownDiagnostics}`}</pre>
    </div>
  );
};
