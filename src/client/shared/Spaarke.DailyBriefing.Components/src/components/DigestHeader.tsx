/**
 * DigestHeader — Header bar for the Daily Briefing digest.
 *
 * Shows the title, unread count, refresh button, and a slot for
 * the preferences dropdown.
 *
 * ADR-021: Uses Fluent v9 tokens for all styling; supports dark mode.
 *
 * Hoisted into `@spaarke/daily-briefing-components/components` by R2 task 011
 * (Wave 3 / Group A). Source of truth; the original-location file at
 * `src/solutions/DailyBriefing/src/components/DigestHeader.tsx` is now a
 * re-export shim pending full cleanup in R2 task 017.
 *
 * R7 task 095 / FR-18 (2026-06-28): added optional overflow Menu hosting a
 * "Browse Playbooks" item. The actual `Xrm.Navigation.navigateTo` call lives
 * in the consuming host code page (shared lib stays Xrm-free per ADR-012).
 * Per task 093 audit Q6 PRIMARY recommendation: the affordance is always
 * visible on the header (matches the existing `preferencesSlot` extension
 * pattern). Affordance only renders when `onBrowsePlaybooks` is provided —
 * preserves back-compat for hosts that don't wire the callback.
 *
 * Pattern parity with task 094: same browse-mode launch path through the
 * existing `sprk_playbooklibrary` Code Page (preserves Path A.5 routing per
 * ADR-013). No new BFF surface; no new modal infrastructure.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Title2,
  Text,
  Button,
  Tooltip,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
} from '@fluentui/react-components';
import {
  AlertRegular,
  ArrowClockwiseRegular,
  MoreHorizontalRegular,
  BookRegular,
} from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    paddingTop: tokens.spacingVerticalM,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  icon: {
    fontSize: '24px',
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  titleGroup: {
    flex: 1,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  unreadCount: {
    color: tokens.colorNeutralForeground3,
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface DigestHeaderProps {
  /** Total number of unread notifications across all channels. */
  totalUnreadCount: number;
  /** Called when the user clicks the refresh button. */
  onRefresh?: () => void;
  /** Slot for the preferences dropdown (rendered in the actions area). */
  preferencesSlot?: React.ReactNode;
  /**
   * R7 task 095 / FR-18 — called when the user clicks the "Browse Playbooks"
   * overflow menu item. The consuming host wires this to the existing
   * `Xrm.Navigation.navigateTo({pageType:'webresource',
   * webresourceName:'sprk_playbooklibrary', data:''}, {target:2, ...})` thunk
   * which opens the Library Code Page in browse mode (lists every playbook
   * with consumer mappings courtesy of task 094's shared-lib extension).
   *
   * Shared lib stays Xrm-free per ADR-012 — this is a pure callback prop.
   * When omitted (back-compat / non-Dataverse hosts), the overflow menu is
   * not rendered.
   */
  onBrowsePlaybooks?: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DigestHeader: React.FC<DigestHeaderProps> = ({
  totalUnreadCount,
  onRefresh,
  preferencesSlot,
  onBrowsePlaybooks,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      <AlertRegular className={styles.icon} />
      <div className={styles.titleGroup}>
        <Title2>Daily Briefing</Title2>
        {totalUnreadCount > 0 && (
          <Text size={200} className={styles.unreadCount}>
            {totalUnreadCount} unread
          </Text>
        )}
      </div>
      <div className={styles.actions}>
        {onRefresh && (
          <Tooltip content="Refresh notifications" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<ArrowClockwiseRegular />}
              onClick={onRefresh}
              aria-label="Refresh notifications"
            />
          </Tooltip>
        )}
        {preferencesSlot}
        {/*
          R7 task 095 / FR-18 — overflow menu hosting "Browse Playbooks".
          Only rendered when the host wires `onBrowsePlaybooks` (back-compat
          for hosts that don't expose Xrm.Navigation). Mirrors task 094's
          chat surface affordance: opens Library in browse mode → Path A.5
          launch preserved via existing Code Page wrapper.
        */}
        {onBrowsePlaybooks && (
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <Tooltip content="More actions" relationship="label">
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<MoreHorizontalRegular />}
                  aria-label="More actions"
                />
              </Tooltip>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem icon={<BookRegular />} onClick={onBrowsePlaybooks}>
                  Browse Playbooks
                </MenuItem>
              </MenuList>
            </MenuPopover>
          </Menu>
        )}
      </div>
    </div>
  );
};
