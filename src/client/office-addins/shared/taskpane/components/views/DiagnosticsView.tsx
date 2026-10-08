import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Button, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { ArrowClockwiseRegular, CopyRegular, DismissRegular } from '@fluentui/react-icons';
import type { SignInDiagnostics } from '@shared/services';
import { describeTokenClaims, diagnosticsAsText, type DiagnosticRow } from '../../services/tokenDiagnostics';

/**
 * DiagnosticsView — task 113 (customer-provisioning-orchestration-r1 request R1, 2026-10-07). A DEV-ONLY panel for
 * the guest sign-in test: which tenant issued the backend token and whether the user is a guest, plus how the pane
 * signed in. Reached from "⋮ → Diagnostics", which exists only in a build with `ADDIN_DIAGNOSTICS_ENABLED=true`
 * (the dev deploy workflow). Shows named claims only — never the token itself.
 */

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  table: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 1fr)',
    gap: tokens.spacingVerticalS,
  },
  row: {
    display: 'flex',
    flexDirection: 'column',
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  label: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  value: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    overflowWrap: 'anywhere',
  },
  actions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
  },
});

export interface DiagnosticsViewProps {
  /** The backend access token, acquired fresh for each refresh (decoded for display only). */
  getAccessToken: () => Promise<string | null>;
  /** How the pane signed in (authority, NAA vs popup), when the auth service reports it. */
  signIn?: SignInDiagnostics;
  /** The Office host and platform, e.g. "Word · PC". */
  hostDescription: string;
  onClose: () => void;
}

export const DiagnosticsView: React.FC<DiagnosticsViewProps> = ({
  getAccessToken,
  signIn,
  hostDescription,
  onClose,
}) => {
  const styles = useStyles();
  const [claims, setClaims] = useState<DiagnosticRow[] | null>(null);
  const [tokenError, setTokenError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const load = useCallback(async () => {
    setClaims(null);
    setTokenError(null);
    try {
      const token = await getAccessToken();
      if (!token) setTokenError('No access token — not signed in.');
      setClaims(describeTokenClaims(token));
    } catch (err) {
      setTokenError(err instanceof Error ? err.message : 'Could not get an access token.');
      setClaims(describeTokenClaims(null));
    }
  }, [getAccessToken]);

  useEffect(() => {
    void load();
  }, [load]);

  const rows = useMemo<DiagnosticRow[]>(
    () => [
      ...(claims ?? []),
      { label: 'Office host', value: hostDescription },
      {
        label: 'Sign-in path',
        value: signIn ? (signIn.naaActive ? 'NAA (Office broker)' : 'Popup fallback') : 'unknown',
      },
      { label: 'Authority', value: signIn?.authority ?? 'unknown' },
    ],
    [claims, hostDescription, signIn]
  );

  const copy = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(diagnosticsAsText(rows));
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
    }
  }, [rows]);

  return (
    <section className={styles.root} aria-label="Sign-in diagnostics">
      <div className={styles.header}>
        <Text weight="semibold">Sign-in diagnostics</Text>
        <Button appearance="subtle" icon={<DismissRegular />} aria-label="Close diagnostics" onClick={onClose} />
      </div>
      {tokenError && <Text role="alert">{tokenError}</Text>}
      {claims === null ? (
        <Spinner size="small" label="Reading the access token…" />
      ) : (
        <div className={styles.table}>
          {rows.map(row => (
            <div key={row.label} className={styles.row}>
              <span className={styles.label}>{row.label}</span>
              <span className={styles.value}>{row.value}</span>
            </div>
          ))}
        </div>
      )}
      <div className={styles.actions}>
        <Button icon={<CopyRegular />} onClick={() => void copy()} disabled={claims === null}>
          {copied ? 'Copied' : 'Copy'}
        </Button>
        <Button icon={<ArrowClockwiseRegular />} onClick={() => void load()}>
          Refresh
        </Button>
      </div>
    </section>
  );
};
