/**
 * SidePaneShell - Reusable layout shell for Dataverse side pane web resources
 *
 * Provides the standard layout for all side panes across Spaarke:
 * - Fixed header at top
 * - Scrollable content area (entity-specific sections)
 * - Sticky footer at bottom (save button, messages)
 * - Optional read-only banner
 * - Dark mode support via Fluent UI tokens
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9, dark mode support
 * @see Task 108 - Extract shared side pane components
 *
 * @example
 * ```tsx
 * <SidePaneShell
 *   header={<HeaderSection eventId={id} onClose={close} />}
 *   footer={<Footer isDirty={isDirty} onSave={save} />}
 *   isReadOnly={!canWrite}
 *   readOnlyMessage="You do not have permission to edit this record"
 * >
 *   <StatusSection value={status} onChange={setStatus} />
 *   <KeyFieldsSection dueDate={dueDate} priority={priority} />
 *   {config.isSectionVisible("dates") && <DatesSection ... />}
 * </SidePaneShell>
 * ```
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface SidePaneShellProps {
  /** Fixed header content (e.g., record name, close button) */
  header: React.ReactNode;
  /** Sticky footer content (e.g., save button, messages) */
  footer: React.ReactNode;
  /** Scrollable main content (entity-specific sections) */
  children: React.ReactNode;
  /** Whether the record is in read-only mode */
  isReadOnly?: boolean;
  /** Message shown in read-only banner */
  readOnlyMessage?: string;
  /** Ref forwarded to the scrollable content area (for scroll position persistence) */
  contentRef?: React.RefObject<HTMLElement>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  readOnlyBanner: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("8px", "16px"),
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontStyle: "italic",
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
  },
  content: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("0"),
    flexGrow: 1,
    overflowY: "auto",
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const SidePaneShell: React.FC<SidePaneShellProps> = ({
  header,
  footer,
  children,
  isReadOnly = false,
  readOnlyMessage = "Read-only: You do not have permission to edit this record",
  contentRef,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      {header}

      {isReadOnly && (
        <div className={styles.readOnlyBanner}>{readOnlyMessage}</div>
      )}

      <main className={styles.content} ref={contentRef}>
        {children}
      </main>

      {footer}
    </div>
  );
};

export default SidePaneShell;
