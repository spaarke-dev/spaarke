import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  LargeTitle,
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
  },
  brand: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, minWidth: 0 },
  logoWrapper: { display: 'flex', alignItems: 'center', flexShrink: 0 },
  divider: {
    width: tokens.strokeWidthThin,
    height: '20px',
    backgroundColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  brandName: { color: tokens.colorNeutralForeground1, whiteSpace: 'nowrap' },
  // Decorative portal nav strip (non-functional in task 011; real nav is a later task).
  navStrip: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalL },
  navLink: { color: tokens.colorNeutralForeground3, cursor: 'default' },
  navLinkActive: { color: tokens.colorBrandForeground1, fontWeight: tokens.fontWeightSemibold },
  right: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS, flexShrink: 0 },
  // Welcome band — gives the portal presence on the workspace landing.
  welcomeBand: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  welcomeTitle: { color: tokens.colorNeutralForeground1 },
  welcomeSub: { color: tokens.colorNeutralForeground3 },
  // Teams-embedded: the host provides its own header + theme; show only a thin hint.
  teamsHint: {
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
  /** Show the tall "Welcome to the Legal Department Service Portal" band (workspace landing only). */
  showWelcomeBand?: boolean;
}

/**
 * AppHeader — the branded portal header (ShellChrome) for the Legal Department
 * Service Portal (task 011 §2). A taller, "intranet/website" brand + nav strip
 * (Spaarke logo + "Legal Department Service Portal" + decorative sections) with a
 * shared `ThemeToggle` and an account menu; an optional welcome band gives the
 * workspace landing presence. Styled exclusively with Fluent v9 semantic tokens
 * (ADR-021 — no hardcoded colors). Teams-embedded hides redundant chrome.
 */
export const AppHeader: React.FC<AppHeaderProps> = ({
  isDark,
  portalUser,
  onSettingsClick,
  onSignOut,
  teamsHost = false,
  showWelcomeBand = false,
}) => {
  const styles = useStyles();

  if (teamsHost) {
    return (
      <div className={styles.teamsHint} role="banner">
        <Text size={200}>
          Teams-embedded: the host provides the header and theme. Your workspace is below.
        </Text>
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
            Legal Department Service Portal
          </Text>
          <span className={styles.divider} aria-hidden="true" />
          <nav className={styles.navStrip} aria-label="Portal sections">
            <Text size={200} className={styles.navLinkActive}>
              Home
            </Text>
            <Text size={200} className={styles.navLink}>
              Services
            </Text>
            <Text size={200} className={styles.navLink}>
              Resources
            </Text>
            <Text size={200} className={styles.navLink}>
              Help
            </Text>
          </nav>
        </div>

        <div className={styles.right}>
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
                    {portalUser.displayName}
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

      {showWelcomeBand && (
        <div className={styles.welcomeBand}>
          <LargeTitle className={styles.welcomeTitle}>
            Welcome to the Legal Department Service Portal
          </LargeTitle>
          {portalUser?.displayName && (
            <Text className={styles.welcomeSub}>{portalUser.displayName}</Text>
          )}
        </div>
      )}
    </header>
  );
};

export default AppHeader;
