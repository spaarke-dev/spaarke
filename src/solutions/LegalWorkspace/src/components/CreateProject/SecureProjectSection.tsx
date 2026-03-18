/**
 * SecureProjectSection.tsx
 * Secure Project toggle section for the Create Project wizard.
 *
 * Displays a Fluent v9 Switch allowing users to designate the project as
 * "Secure". When toggled on, an expanded information panel explains:
 *   - What a Secure Project is
 *   - What additional infrastructure will be provisioned
 *   - That the designation is IRREVERSIBLE after creation
 *
 * This component is rendered as a section within CreateProjectStep rather
 * than as a standalone wizard step, so that toggle state persists naturally
 * through Back/Next navigation (it lives in the parent's form state).
 *
 * Constraints:
 *   - Fluent v9 only: Switch, Text, Divider, MessageBar, makeStyles
 *   - makeStyles with semantic tokens — ZERO hard-coded colours
 *   - Supports light, dark, and high-contrast modes (ADR-021)
 */

import * as React from 'react';
import {
  Divider,
  MessageBar,
  MessageBarBody,
  Switch,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  LockClosedRegular,
  BuildingRegular,
  StorageRegular,
  PeopleTeamRegular,
  WarningRegular,
} from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISecureProjectSectionProps {
  /** Current toggle state — controlled by parent. */
  isSecure: boolean;
  /** Called when user flips the toggle. */
  onSecureChange: (value: boolean) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },

  // ── Divider row ───────────────────────────────────────────────────────────
  dividerRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },

  // ── Toggle row ────────────────────────────────────────────────────────────
  toggleRow: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalM,
  },
  toggleIcon: {
    marginTop: '2px',
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  toggleIconSecure: {
    marginTop: '2px',
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  toggleText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    flex: 1,
  },
  toggleLabel: {
    color: tokens.colorNeutralForeground1,
  },
  toggleDescription: {
    color: tokens.colorNeutralForeground3,
  },

  // ── Expanded info panel ───────────────────────────────────────────────────
  infoPanel: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    borderLeft: `3px solid ${tokens.colorBrandBackground}`,
  },
  infoPanelTitle: {
    color: tokens.colorNeutralForeground1,
  },

  // ── Provisioning list ─────────────────────────────────────────────────────
  provisioningList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  provisioningItem: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
  },
  provisioningIcon: {
    marginTop: '2px',
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  provisioningText: {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
  },
  provisioningItemTitle: {
    color: tokens.colorNeutralForeground1,
  },
  provisioningItemDesc: {
    color: tokens.colorNeutralForeground3,
  },

  // ── Warning bar ───────────────────────────────────────────────────────────
  warningBar: {
    borderRadius: tokens.borderRadiusMedium,
  },
});

// ---------------------------------------------------------------------------
// Provisioning item data
// ---------------------------------------------------------------------------

interface IProvisioningItem {
  icon: React.ReactElement;
  title: string;
  description: string;
}

const PROVISIONING_ITEMS: IProvisioningItem[] = [
  {
    icon: <BuildingRegular fontSize={16} />,
    title: 'Dedicated Business Unit',
    description:
      'A Dataverse Business Unit is created to scope security roles and data access for this project.',
  },
  {
    icon: <StorageRegular fontSize={16} />,
    title: 'SharePoint Embedded Container',
    description:
      'An isolated SPE document container is provisioned exclusively for this project\u2019s files.',
  },
  {
    icon: <PeopleTeamRegular fontSize={16} />,
    title: 'External Access Portal',
    description:
      'A Power Pages workspace is activated so invited external users can access project documents and events.',
  },
];

// ---------------------------------------------------------------------------
// SecureProjectSection (exported)
// ---------------------------------------------------------------------------

export const SecureProjectSection: React.FC<ISecureProjectSectionProps> = ({
  isSecure,
  onSecureChange,
}) => {
  const styles = useStyles();

  const handleToggleChange = React.useCallback(
    (_ev: React.ChangeEvent<HTMLInputElement>, data: { checked: boolean }) => {
      onSecureChange(data.checked);
    },
    [onSecureChange]
  );

  return (
    <div className={styles.root}>
      {/* Section divider */}
      <div className={styles.dividerRow}>
        <Divider />
      </div>

      {/* Toggle row */}
      <div className={styles.toggleRow}>
        <LockClosedRegular
          fontSize={20}
          className={isSecure ? styles.toggleIconSecure : styles.toggleIcon}
          aria-hidden="true"
        />

        <div className={styles.toggleText}>
          <Text size={400} weight="semibold" className={styles.toggleLabel}>
            Secure Project
          </Text>
          <Text size={200} className={styles.toggleDescription}>
            Enables external access, an isolated document container, and dedicated
            security boundaries for this project.
          </Text>
        </div>

        <Switch
          checked={isSecure}
          onChange={handleToggleChange}
          label={isSecure ? 'Enabled' : 'Disabled'}
          labelPosition="before"
          aria-label="Mark this project as a Secure Project"
        />
      </div>

      {/* Expanded info panel — shown when toggle is on */}
      {isSecure && (
        <>
          <div className={styles.infoPanel}>
            <Text size={300} weight="semibold" className={styles.infoPanelTitle}>
              What will be provisioned when this project is created:
            </Text>

            <div className={styles.provisioningList}>
              {PROVISIONING_ITEMS.map((item) => (
                <div key={item.title} className={styles.provisioningItem}>
                  <span className={styles.provisioningIcon} aria-hidden="true">
                    {item.icon}
                  </span>
                  <div className={styles.provisioningText}>
                    <Text size={300} weight="semibold" className={styles.provisioningItemTitle}>
                      {item.title}
                    </Text>
                    <Text size={200} className={styles.provisioningItemDesc}>
                      {item.description}
                    </Text>
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Irreversibility warning */}
          <MessageBar intent="warning" className={styles.warningBar}>
            <MessageBarBody>
              <Text size={200} weight="semibold">
                <WarningRegular fontSize={14} aria-hidden="true" />{' '}
                This designation is permanent.{' '}
              </Text>
              <Text size={200}>
                Once a project is marked as Secure and created, the secure designation
                cannot be removed. Please confirm this is correct before proceeding.
              </Text>
            </MessageBarBody>
          </MessageBar>
        </>
      )}
    </div>
  );
};

export default SecureProjectSection;
