/**
 * TeamsGuestAccount - the Teams screen for a signed-in B2B GUEST of the Teams tenant
 * (spaarke-SPA-external-access-platform-r3 task 075; ADR-028 Amendment A9, Teams amendment).
 *
 * In Teams, `user.tenant.id` is the HOST tenant, so a guest of a listed customer's tenant gets a guest token through NAA
 * on `/{tid}`; the customer BFF refuses it (401), and "Sign in again" would reload into the same 401 forever. The bootstrap
 * detects the guest right after the token is acquired and shows this screen instead: no BFF call is made, and there is
 * nothing to retry here (no buttons - the person opens Spaarke from their own organization's Teams).
 *
 * ADR-021: Fluent v9 semantic tokens only (the Teams theme reaches them through the FluentProvider). ADR-012: the shared
 * `EmptyState` shell, with a real `h1`.
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { PersonProhibited32Regular } from '@fluentui/react-icons';
import { EmptyState } from '@spaarke/ui-components/components/EmptyState';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100dvh',
    width: '100%',
    boxSizing: 'border-box',
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  description: { maxWidth: '28rem' },
  icon: { color: tokens.colorNeutralForeground3 },
  detail: {
    marginTop: tokens.spacingVerticalL,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    fontFamily: tokens.fontFamilyMonospace,
  },
});

export const TEAMS_GUEST_HEADING = "This tab can't be used with a guest account";
export const TEAMS_GUEST_DESCRIPTION = 'Open Spaarke in Teams signed in to your own organization.';

export interface TeamsGuestAccountProps {
  /** Masked diagnostic from the guest verdict (sign-in path and signals; no ids, names or addresses). */
  detail?: string;
}

export const TeamsGuestAccount: React.FC<TeamsGuestAccountProps> = ({ detail }) => {
  const s = useStyles();
  return (
    <div className={s.root}>
      <EmptyState
        icon={<PersonProhibited32Regular className={s.icon} />}
        heading={TEAMS_GUEST_HEADING}
        headingAs="h1"
        description={TEAMS_GUEST_DESCRIPTION}
        descriptionClassName={s.description}
      />
      {detail ? <div className={s.detail}>Diagnostic: {detail}</div> : null}
    </div>
  );
};

export default TeamsGuestAccount;
