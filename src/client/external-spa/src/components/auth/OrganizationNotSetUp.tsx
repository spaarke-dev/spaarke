/**
 * OrganizationNotSetUp - the screen for a workforce contact whose company directory no customer lists
 * (spaarke-SPA-external-access-platform-r3 task 058; ADR-028 Amendment A9; design notes/a9-bff-design.md section 5).
 *
 * The signed-in tenant id (`tid`) picks the customer backend from the directory entries (`homeTenantIds`). When no entry
 * lists it the SPA knows no backend for this person: it makes no token request and no BFF call, and shows this screen.
 * Nothing about the tenant or the account is displayed (no tenant id, no address), so the screen is no oracle for which
 * directories are customers.
 *
 * Two ways out, both user gestures: "Sign out" (clears the tab's realm, invite code and backend, then signs the account
 * out of the sign-in instance) and "Choose a different sign-in" (back to the landing chooser, with the account still
 * signed in - so the text tells the person to sign out first to use a different company account).
 *
 * Teams variant (task 075): the tab's tenant is the Teams context tenant and Teams has no realm chooser and no account of
 * ours to sign out of, so the screen has NO actions at all - no "Sign out", no "Choose a different sign-in".
 *
 * ADR-021: Fluent v9 semantic tokens only (the Teams theme reaches them through the FluentProvider). ADR-012: the shared
 * `EmptyState` shell, with a real `h1`.
 */
import * as React from 'react';
import { Button, makeStyles, tokens } from '@fluentui/react-components';
import { Building48Regular } from '@fluentui/react-icons';
import { EmptyState } from '@spaarke/ui-components/components/EmptyState';
import type { IPublicClientApplication } from '@azure/msal-browser';
import { clearStoredRealm } from '../../auth/realm';
import { clearInviteCode } from '../../auth/invite-code';
import { clearActiveBackend } from '../../auth/backend-selection';

export type OrganizationNotSetUpProps =
  | {
      variant?: 'browser';
      /** The sign-in instance that holds the account; "Sign out" signs it out. */
      instance: IPublicClientApplication;
      /** "Choose a different sign-in": forget the realm and return to the landing chooser. */
      onChooseDifferent: () => void;
    }
  /** The Teams tab: a message only. */
  | { variant: 'teams' };

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
  footer: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    justifyContent: 'center',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalS,
  },
});

export const OrganizationNotSetUp: React.FC<OrganizationNotSetUpProps> = props => {
  const s = useStyles();
  const [signOutFailed, setSignOutFailed] = React.useState(false);
  const instance = props.variant === 'teams' ? null : props.instance;

  const signOut = React.useCallback(() => {
    if (!instance) return;
    clearStoredRealm();
    clearInviteCode();
    clearActiveBackend();
    setSignOutFailed(false);
    void Promise.resolve(instance.logoutRedirect()).catch(() => setSignOutFailed(true));
  }, [instance]);

  if (props.variant === 'teams') {
    return (
      <div className={s.root}>
        <EmptyState
          icon={<Building48Regular className={s.icon} />}
          heading="Your organization is not set up for Spaarke"
          headingAs="h1"
          description="This Teams app is not connected to a Spaarke customer for your organization yet. Contact your Spaarke administrator, or the person who added this app, to add your organization."
          descriptionClassName={s.description}
        />
      </div>
    );
  }
  const { onChooseDifferent } = props;

  return (
    <div className={s.root}>
      <EmptyState
        icon={<Building48Regular className={s.icon} />}
        heading="Your organization is not set up for Spaarke"
        headingAs="h1"
        description={
          signOutFailed
            ? 'Signing out did not complete. Close this tab, or try again.'
            : 'Your company account is not connected to a Spaarke customer yet. Contact the person who invited you, ' +
              'or ask your Spaarke administrator to add your organization. To use a different company account, ' +
              'select "Sign out" first: "Choose a different sign-in" keeps this account signed in.'
        }
        descriptionClassName={s.description}
        footer={
          <div className={s.footer}>
            <Button appearance="primary" onClick={signOut}>
              Sign out
            </Button>
            <Button appearance="secondary" onClick={onChooseDifferent}>
              Choose a different sign-in
            </Button>
          </div>
        }
      />
    </div>
  );
};

export default OrganizationNotSetUp;
