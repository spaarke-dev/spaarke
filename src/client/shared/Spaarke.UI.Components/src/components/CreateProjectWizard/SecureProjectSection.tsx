/**
 * SecureProjectSection.tsx
 * Secure Project toggle section for the Create Project wizard.
 *
 * Displays a Fluent v9 Switch allowing users to designate the project as
 * "Secure". When toggled on, an expanded information panel explains what
 * provisioning actually does, and that the designation can be undone.
 *
 * This component is rendered as a section within CreateProjectStep rather
 * than as a standalone wizard step, so that toggle state persists naturally
 * through Back/Next navigation (it lives in the parent's form state).
 *
 * COPY REWRITTEN 2026-09-09 (task 068, spec FR-31). Two claims were wrong and
 * are gone:
 *
 *   1. The retired external-portal claim. That portal product is retired (the
 *      external surface is SWA + CIAM) and provisioning activates no portal at
 *      all — nothing in POST /api/v1/external-access/provision-project touches
 *      one. External participants reach a secure project through explicit
 *      `sprk_externalrecordaccess` grants instead.
 *
 *   2. The irreversibility warning. The designation CAN be removed: POST
 *      /api/v1/external-access/unsecure-project (UnsecureProjectEndpoint)
 *      reassigns the record to a named owner, revokes every share provisioning
 *      issued, then clears `sprk_issecure`. design.md §5.1 calls the designation
 *      reversible; spec FR-28 counts the reverse path as part of the mechanism.
 *
 * The verbatim retired copy is preserved in
 * `projects/unified-access-control-r2/notes/task-068-secure-step-copy.md` —
 * deliberately NOT reproduced here, so the FR-31 grep gate stays meaningful.
 *
 * Every sentence rendered below is traceable to ProvisionProjectEndpoint.cs as
 * merged by task 061 — see the per-item notes on PROVISIONING_ITEMS.
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
  MessageBarTitle,
  Switch,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { LockClosedRegular, BuildingRegular, StorageRegular, PeopleTeamRegular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/** Ties the toggle's `aria-controls` to the panel it discloses. */
const PANEL_ID = 'secure-project-details';

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

  // ── Notice bar (reversibility note) ───────────────────────────────────────
  noticeBar: {
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

/**
 * What provisioning does, in the order the server does it.
 *
 * Each entry maps to a step of `ProvisionProjectEndpoint.ProvisionProjectAsync`
 * (task 061). Nothing forward-looking belongs here — if the server does not do
 * it today, it must not be described here (CLAUDE.md §2, code wins).
 */
const PROVISIONING_ITEMS: IProvisioningItem[] = [
  {
    // Endpoint steps 2, 3 and 5: resolve the ONE canonical `Secure Project`
    // business unit BY NAME from configuration, resolve its default owner team,
    // assign the project to that team, and verify the assignment took effect.
    // Nothing is created — no business unit per project, and no account.
    //
    // "by design has no people in it" is deliberate hedging. Provisioning resolves
    // the BU's DEFAULT owner team and never asserts it is empty; a Dataverse default
    // owner team tracks BU membership, so emptiness holds only while no user is
    // placed in that BU. That is an environment invariant, not a guarantee this code
    // makes — and copy must not state it as one.
    icon: <BuildingRegular fontSize={16} />,
    title: 'Moved into the Secure Project business unit',
    description:
      'The project is reassigned to the Secure Project business unit’s owner team, which by design has no people in it. Ownership therefore grants nobody access to the project.',
  },
  {
    // Endpoint step 5.5 (ShareToCreatorAndPrincipalsAsync): the creator is
    // identified from their own token via WhoAmI and always shared to with
    // Read/Write/Append/Append To/Share. Provisioning FAILS rather than finish
    // without it — an unshared secure project is a record nobody can open.
    icon: <PeopleTeamRegular fontSize={16} />,
    title: 'Shared with you, and only with people you add',
    description:
      'You are given access to the project explicitly, including the right to bring colleagues in. Everyone else — internal or external — needs an explicit grant before they can see it.',
  },
  {
    // Endpoint steps 6 and 7: create the project's own SPE container and record
    // it on sprk_containerid, failing if the write cannot be verified.
    icon: <StorageRegular fontSize={16} />,
    title: 'Given its own document container',
    description:
      'An isolated SharePoint Embedded container is created for this project’s files and recorded on the project record.',
  },
];

// ---------------------------------------------------------------------------
// SecureProjectSection (exported)
// ---------------------------------------------------------------------------

export const SecureProjectSection: React.FC<ISecureProjectSectionProps> = ({ isSecure, onSecureChange }) => {
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
            Restricts this project to people it is explicitly shared with, and gives it its own document container.
          </Text>
        </div>

        {/*
          The accessible name CONTAINS the visible label ("Enabled" / "Disabled").

          It previously did not: the visible label said "Enabled" while `aria-label` said "Mark this
          project as a Secure Project", so the accessible name shared no words with the visible one.
          That fails WCAG 2.1 §2.5.3 Label in Name (Level A) — a speech-input user saying the word
          they can see gets no match. Naming it "Secure Project: Enabled" keeps the context a screen
          reader needs and the word a voice user says.

          `aria-expanded` / `aria-controls` are here because flipping this switch mounts the panel
          below. Without them the disclosure is silent, and the panel is exactly the copy this task
          exists to make truthful — it would be corrected for sighted users only.
        */}
        <Switch
          checked={isSecure}
          onChange={handleToggleChange}
          label={isSecure ? 'Enabled' : 'Disabled'}
          labelPosition="before"
          aria-label={`Secure Project: ${isSecure ? 'Enabled' : 'Disabled'}`}
          aria-expanded={isSecure}
          aria-controls={PANEL_ID}
        />
      </div>

      {/* Expanded info panel — shown when toggle is on */}
      {isSecure && (
        <>
          <div className={styles.infoPanel} id={PANEL_ID}>
            <Text size={300} weight="semibold" className={styles.infoPanelTitle}>
              What securing this project does:
            </Text>

            <div className={styles.provisioningList}>
              {PROVISIONING_ITEMS.map(item => (
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

          {/*
            Reversibility note — replaces the permanence warning removed by task
            068. Informational rather than a warning: undoing the designation is a
            supported operation (UnsecureProjectEndpoint), not a hazard.

            It names an ADMINISTRATOR on purpose. The endpoint ships, but no client
            surface calls /unsecure-project yet (grep over src/**: this file's own
            comments are the only references). "This can be undone later" would read
            as something the person at this screen can do, and they cannot — which
            would be a fresh instance of the promise-what-the-code-doesn't-do defect
            this task exists to remove. Reword when the surface ships.
          */}
          <MessageBar intent="info" className={styles.noticeBar}>
            <MessageBarBody>
              {/*
                Phrased as "can be reversed", not "is not permanent". The negated form would put the
                banned word back into the rendered copy, and the FR-31 gate is a blanket absence
                check on it — a gate that has to reason about negation is a gate that stops working.
              */}
              <MessageBarTitle>This can be reversed.</MessageBarTitle>{' '}
              <Text size={200}>
                An administrator can remove the secure designation later. Doing so returns the project to a named owner
                and revokes the access that securing it granted, so anyone who still needs it is added again.
              </Text>
            </MessageBarBody>
          </MessageBar>
        </>
      )}
    </div>
  );
};

export default SecureProjectSection;
