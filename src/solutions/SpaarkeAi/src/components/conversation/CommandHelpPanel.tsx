/**
 * CommandHelpPanel.tsx — R6 task 081 / D-D-02 (Pillar 8 `/help` panel).
 *
 * Fluent v9 `Dialog`-based panel listing the Pillar 8 closed vocabulary
 * (6 hard slashes + 4 soft slashes + 3 reference shapes). Opened by the
 * `/help` hard slash via `HardSlashExecutor.execHelp`. Also reused by task
 * 085 as the chat-input-bar's "help" affordance surface.
 *
 * ## Why a Dialog (not a Drawer)?
 *
 * Per spec FR-49, `/help` must be instant. A modal Dialog is the simplest
 * surface that:
 *   - blocks no rendering work (Fluent v9 Dialog uses portals so the
 *     surrounding conversation stays intact);
 *   - presents a tabular reference cleanly;
 *   - has zero positioning concerns vs. a Drawer (which the host has to
 *     compose with existing OverlayDrawer instances like
 *     ManageWorkspacesPane);
 *   - inherits semantic tokens from `FluentProvider` per ADR-021 (dark-mode
 *     parity).
 *
 * The Dialog is dismissable via the Close button, Escape key, or background
 * click — all native Fluent v9 behavior. No custom keyboard handlers needed.
 *
 * ## Vocabulary source
 *
 * Commands are pulled from `CommandRouter.HardSlashes` + `CommandRouter.SoftSlashes`
 * so the help panel is automatically in lock-step with the parser. Q6 closed
 * vocabulary — do NOT add commands by editing the panel; extend the parser.
 *
 * ## ADR compliance
 *
 *   - ADR-012 Fluent v9 only — no Fluent v8 imports.
 *   - ADR-021 semantic tokens only — no hardcoded colors. Tokens for spacing,
 *     borders, text colors flow through `makeStyles` + `tokens.*`.
 *   - ADR-022 functional component + hooks.
 *   - ADR-029 frontend-only — zero BFF surface; publish-size delta = 0 MB.
 *
 * @see HardSlashExecutor.tsx — opens this panel via setHelpOpen(true)
 * @see ConversationPane.tsx — owns the open/closed state via useState
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  makeStyles,
  tokens,
  Text,
} from '@fluentui/react-components';

import { HardSlashes, SoftSlashes } from './CommandRouter';
import type { HardSlashCommand, SoftSlashCommand } from './CommandRouter';

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  content: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
  },
  sectionHeading: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  list: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalL,
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
  },
  commandRow: {
    display: 'grid',
    gridTemplateColumns: 'minmax(140px, 200px) 1fr',
    columnGap: tokens.spacingHorizontalM,
    alignItems: 'baseline',
  },
  commandName: {
    fontFamily: tokens.fontFamilyMonospace,
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase300,
  },
  commandDescription: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  example: {
    fontFamily: tokens.fontFamilyMonospace,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Vocabulary descriptions — kept colocated so the panel is self-contained.
// If the parser vocabulary changes, update these entries.
// ---------------------------------------------------------------------------

const HARD_SLASH_DESCRIPTIONS: Readonly<Record<HardSlashCommand, string>> = {
  '/clear':
    'Clear the current conversation. Bypasses the assistant — instant.',
  '/new-session':
    'End the current session and start a fresh one. All panes reset.',
  '/help': 'Open this panel listing all available commands.',
  '/export': 'Download the conversation history as a markdown file.',
  '/save-to-matter':
    'Save the conversation to a matter as a pinned memory. Argument: matter id (optional if a matter is in context).',
  '/pin': 'Pin the currently focused workspace tab so it survives session expiry.',
};

const SOFT_SLASH_DESCRIPTIONS: Readonly<Record<SoftSlashCommand, string>> = {
  '/summarize':
    'Ask the assistant to summarize. Can reference files (e.g. `/summarize #engagement-letter.docx`).',
  '/draft':
    'Ask the assistant to draft a response or document. References supported.',
  '/extract-entities':
    'Ask the assistant to extract entities (parties, dates, amounts) from referenced text.',
  '/analyze':
    'Ask the assistant to analyze a referenced item against a scope or criterion.',
};

const REFERENCE_HELP: ReadonlyArray<{
  syntax: string;
  description: string;
  example: string;
}> = [
  {
    syntax: '#scope',
    description: 'Insert the active scope reference into your message.',
    example: 'What does #scope cover?',
  },
  {
    syntax: '@<entity>',
    description: 'Reference a Dataverse entity (matter, person, organization).',
    example: '/draft response to @opposing-counsel',
  },
  {
    syntax: '#<filename>',
    description: 'Reference an attached or workspace file.',
    example: '/summarize #engagement-letter.docx',
  },
];

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface CommandHelpPanelProps {
  /**
   * Controlled open/closed state. The host owns this via `useState` — the
   * `/help` hard slash calls `setOpen(true)` on the host.
   */
  open: boolean;

  /**
   * Called when the user dismisses the panel (Close button, Escape, or
   * outside click). The host sets `open` to `false`.
   */
  onClose: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Renders the Pillar 8 `/help` reference. Fluent v9 Dialog; semantic tokens;
 * dark-mode safe per ADR-021.
 */
export function CommandHelpPanel(
  props: CommandHelpPanelProps,
): React.JSX.Element {
  const { open, onClose } = props;
  const styles = useStyles();

  const handleOpenChange = React.useCallback(
    (_event: unknown, data: { open: boolean }): void => {
      if (!data.open) onClose();
    },
    [onClose],
  );

  return (
    <Dialog open={open} onOpenChange={handleOpenChange} modalType="modal">
      <DialogSurface aria-label="Chat command reference">
        <DialogBody>
          <DialogTitle>Chat commands</DialogTitle>
          <DialogContent>
            <div className={styles.content}>
              {/* ── Hard slashes ─────────────────────────────────────────── */}
              <section className={styles.section}>
                <Text className={styles.sectionHeading}>
                  Quick actions (instant — no assistant)
                </Text>
                <ul className={styles.list}>
                  {HardSlashes.map((cmd) => (
                    <li key={cmd} className={styles.commandRow}>
                      <span className={styles.commandName}>{cmd}</span>
                      <span className={styles.commandDescription}>
                        {HARD_SLASH_DESCRIPTIONS[cmd]}
                      </span>
                    </li>
                  ))}
                </ul>
              </section>

              {/* ── Soft slashes ─────────────────────────────────────────── */}
              <section className={styles.section}>
                <Text className={styles.sectionHeading}>
                  Assistant shortcuts
                </Text>
                <ul className={styles.list}>
                  {SoftSlashes.map((cmd) => (
                    <li key={cmd} className={styles.commandRow}>
                      <span className={styles.commandName}>{cmd}</span>
                      <span className={styles.commandDescription}>
                        {SOFT_SLASH_DESCRIPTIONS[cmd]}
                      </span>
                    </li>
                  ))}
                </ul>
              </section>

              {/* ── References ──────────────────────────────────────────── */}
              <section className={styles.section}>
                <Text className={styles.sectionHeading}>References</Text>
                <ul className={styles.list}>
                  {REFERENCE_HELP.map((ref) => (
                    <li key={ref.syntax} className={styles.commandRow}>
                      <span className={styles.commandName}>{ref.syntax}</span>
                      <span className={styles.commandDescription}>
                        {ref.description}
                        {' '}
                        <span className={styles.example}>
                          (e.g. {ref.example})
                        </span>
                      </span>
                    </li>
                  ))}
                </ul>
              </section>
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" onClick={onClose}>
              Close
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
