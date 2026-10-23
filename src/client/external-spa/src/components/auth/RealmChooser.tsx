/**
 * RealmChooser — the browser sign-in landing (spec FR-03, ux-brief §3; reworked per owner UAT
 * 2026-08-12 #5B).
 *
 * This is the EXTERNAL partner portal, so the sign-in is now **Partner-primary** rather than a
 * symmetric "My organization / Partner" choice (which UAT found confusing — a partner shouldn't have
 * to reason about planes). The primary action signs in as a Partner (CIAM); a subtle secondary link
 * covers the workforce contact who signs in with their COMPANY account (ADR-028 A9, task 058): "Work for
 * {customer}? Sign in with your company account", where {customer} is the configured customer a
 * `?customer={key}` link names, else "your organization". It is shown ONLY in a browser and ONLY when
 * the tab has no stored realm yet; it NEVER appears inside Teams (the Teams host does silent
 * workforce SSO). The workforce link is kept BEFORE any authority redirect so an employee is never
 * stranded on the partner IdP (which a full auto-redirect-to-CIAM would risk).
 *
 * Shared-library mandate (CLAUDE.md §11 + ADR-050): built on Fluent v9 primitives with semantic
 * tokens only (light/dark/Teams correct, zero hardcoded hex).
 */
import * as React from 'react';
import { Button, Link, Text, Title2, makeStyles, tokens } from '@fluentui/react-components';
import { PeopleTeam24Regular } from '@fluentui/react-icons';
import type { Realm } from '../../auth/realm';

export interface RealmChooserProps {
  /** Invoked with the plane the user picked; the bootstrap then signs in against that authority. */
  onChoose: (realm: Realm) => void;
  /**
   * The display name of the configured customer a `?customer={key}` link names (`landingCustomerName`), or null/absent
   * for "your organization". Display only: it never selects a backend.
   */
  customerName?: string | null;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    width: '100%',
    padding: tokens.spacingHorizontalXXL,
    boxSizing: 'border-box',
  },
  card: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalL,
    maxWidth: '26rem',
    textAlign: 'center',
  },
  lead: {
    color: tokens.colorNeutralForeground3,
  },
  primaryBtn: {
    minWidth: '16rem',
  },
  workforceRow: {
    marginTop: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

export const RealmChooser: React.FC<RealmChooserProps> = ({ onChoose, customerName }) => {
  const s = useStyles();
  return (
    <div className={s.root}>
      <div className={s.card}>
        <Title2>Sign in to Spaarke</Title2>
        <Text className={s.lead} block>
          Access the records, documents, and requests shared with you.
        </Text>
        <Button
          className={s.primaryBtn}
          appearance="primary"
          size="large"
          icon={<PeopleTeam24Regular />}
          onClick={() => onChoose('ciam')}
        >
          Continue as Partner
        </Button>
        <Text className={s.workforceRow} block>
          Work for {customerName ?? 'your organization'}?{' '}
          <Link onClick={() => onChoose('workforce')}>Sign in with your company account</Link>
        </Text>
      </div>
    </div>
  );
};

export default RealmChooser;
