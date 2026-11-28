import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Tooltip,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
} from '@fluentui/react-components';
import { Person20Regular, Settings20Regular, SignOut20Regular } from '@fluentui/react-icons';
import { SpaarkeLogoSvg } from './SpaarkeLogoSvg';
// SHARED library — the canonical theme toggle (drives the 4-level theme cascade); §11 reuse.
import { ThemeToggle } from '@spaarke/ui-components/components/ThemeToggle';
import type { PortalUser } from '../types';
import { PHONE_MEDIA } from './phoneLayout';

const useStyles = makeStyles({
  header: {
    display: 'flex',
    flexDirection: 'column',
    flexShrink: 0,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  // Top brand / nav strip — a taller, more "intranet/website" portal feel.
  brandBar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
    minHeight: '40px',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    backgroundColor: tokens.colorNeutralBackground2,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke3,
    [PHONE_MEDIA]: { paddingLeft: tokens.spacingHorizontalM, paddingRight: tokens.spacingHorizontalM },
  },
  brand: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, minWidth: 0 },
  logoWrapper: { display: 'flex', alignItems: 'center', flexShrink: 0 },
  divider: {
    width: tokens.strokeWidthThin,
    height: '20px',
    backgroundColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  brandName: {
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'nowrap',
    // Phone (task 048): the portal title wraps instead of widening the page.
    [PHONE_MEDIA]: { whiteSpace: 'normal', minWidth: 0 },
  },
  right: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS, flexShrink: 0 },
  // Phone (task 048): the account button is icon-only (its aria-label keeps the name), so a long name or an email
  // address cannot widen the page.
  accountName: { [PHONE_MEDIA]: { display: 'none' } },
  // Teams-embedded: the host provides its own header + theme; show only a thin hint.
  teamsHint: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    color: tokens.colorNeutralForeground3,
    backgroundColor: tokens.colorNeutralBackground3,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    [PHONE_MEDIA]: { paddingLeft: tokens.spacingHorizontalM, paddingRight: tokens.spacingHorizontalM },
  },
});

interface AppHeaderProps {
  /** Current dark mode state (drives the Spaarke logo fill). */
  isDark: boolean;
  /** Authenticated portal user, or null while loading. */
  portalUser?: PortalUser | null;
  /** Navigate to the settings page. */
  onSettingsClick?: () => void;
  /** Sign the user out (MSAL logout). Optional — omitted → no Sign out item. */
  onSignOut?: () => void;
  /** When embedded in Teams, the host owns the header/theme — render only a thin hint. */
  teamsHost?: boolean;
  /** The portal title next to the logo — {@link portalTitleFor} the signed-in plane. */
  title: string;
  /**
   * The notification bell (task 055), rendered at the right of the header and of the Teams hint. A slot, not an import: the
   * caller mounts it only for a signed-in user, because the bell reads the BFF the moment it mounts.
   */
  bell?: React.ReactNode;
}

/** The portal title a partner (CIAM plane) sees. */
export const PARTNER_PORTAL_TITLE = 'Outside Counsel Collaboration';
/** The portal title an internal contact (workforce plane: company-account sign-in or Teams) sees. */
export const WORKFORCE_PORTAL_TITLE = 'Legal Service Request Portal';

/** The header title for the signed-in plane (owner, 2026-11-17): one SPA, two audiences, two names. */
export function portalTitleFor(workforcePlane: boolean): string {
  return workforcePlane ? WORKFORCE_PORTAL_TITLE : PARTNER_PORTAL_TITLE;
}

/**
 * AppHeader — the branded portal header (ShellChrome) (task 011 §2). A brand bar
 * (Spaarke logo + the plane's portal title) with the notification bell, a shared
 * `ThemeToggle` and an account menu. The decorative Home/Services/Resources/Help strip
 * and the "Welcome to …" landing band were removed (owner, 2026-11-17): the strip did
 * nothing and the band repeated the title. Styled exclusively with Fluent v9 semantic
 * tokens (ADR-021 — no hardcoded colors). Teams-embedded hides redundant chrome.
 */
export const AppHeader: React.FC<AppHeaderProps> = ({
  isDark,
  portalUser,
  onSettingsClick,
  onSignOut,
  teamsHost = false,
  title,
  bell = null,
}) => {
  const styles = useStyles();

  if (teamsHost) {
    return (
      <div className={styles.teamsHint} role="banner">
        <Text size={200}>Teams-embedded: the host provides the header and theme. Your workspace is below.</Text>
        {bell}
      </div>
    );
  }

  return (
    <header className={styles.header} role="banner">
      <div className={styles.brandBar}>
        <div className={styles.brand}>
          <div className={styles.logoWrapper}>
            <SpaarkeLogoSvg fill={isDark ? 'white' : 'black'} height={28} />
          </div>
          <span className={styles.divider} aria-hidden="true" />
          <Text size={400} weight="semibold" className={styles.brandName}>
            {title}
          </Text>
        </div>

        <div className={styles.right}>
          {bell}
          <ThemeToggle />
          {portalUser && (
            <Menu>
              <MenuTrigger disableButtonEnhancement>
                <Tooltip content="Account" relationship="label">
                  <Button
                    appearance="subtle"
                    icon={<Person20Regular />}
                    aria-label={`${portalUser.displayName} — account menu`}
                  >
                    <span className={styles.accountName} data-testid="account-name">
                      {portalUser.displayName}
                    </span>
                  </Button>
                </Tooltip>
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  {onSettingsClick && (
                    <MenuItem icon={<Settings20Regular />} onClick={onSettingsClick}>
                      Settings
                    </MenuItem>
                  )}
                  {onSignOut && (
                    <MenuItem icon={<SignOut20Regular />} onClick={onSignOut}>
                      Sign out
                    </MenuItem>
                  )}
                </MenuList>
              </MenuPopover>
            </Menu>
          )}
        </div>
      </div>
    </header>
  );
};

export default AppHeader;
