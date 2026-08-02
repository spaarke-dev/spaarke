/**
 * Choice Dialog Component
 *
 * Rich option button dialog for presenting 2-4 mutually exclusive choices.
 * Each option displays icon, title, and description for clear user understanding.
 *
 * Standards: choice-dialog-pattern (formerly ADR-023), ADR-021 Fluent UI v9, ADR-012 shared lib.
 *
 * Re-based (spaarke-modal-system task 041, spec FR-13) onto the `ChoiceModal` preset
 * (`SprkModal/presets/ChoiceModal`, built task 005): chrome (envelope, header, window
 * controls, footer) AND per-choice rendering now come from `ChoiceModal` — this file is
 * a thin, backward-compatible adapter over it, mapping the ORIGINAL `options`/`onDismiss`
 * shape onto `ChoiceModal`'s `choices`/`onClose` shape. The public `IChoiceDialogProps` /
 * `IChoiceDialogOption` contract is UNCHANGED so existing call sites keep compiling and
 * behaving unchanged (see `projects/spaarke-modal-system/notes/task-041-completion.md`
 * for the full behavior audit). Supersedes the interim `ModalWindowControls` wiring
 * added in task 030 — that chrome now comes transitively via `ChoiceModal` → `SprkModal`.
 */

import * as React from 'react';
import { ChoiceModal, type ChoiceModalChoice } from '../SprkModal/presets/ChoiceModal';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Represents a single choice option in the dialog.
 */
export interface IChoiceDialogOption {
  /** Unique identifier for this option */
  id: string;
  /** Icon component (24px recommended) */
  icon: React.ReactNode;
  /** Short, action-oriented title */
  title: string;
  /** Explanation of what happens when chosen */
  description: string;
  /** Optional: disable this option */
  disabled?: boolean;
}

/**
 * Props for the ChoiceDialog component.
 */
export interface IChoiceDialogProps {
  /** Whether the dialog is open */
  open: boolean;
  /** Dialog title */
  title: string;
  /** Contextual message explaining the situation */
  message: string | React.ReactNode;
  /** Array of 2-4 choice options */
  options: IChoiceDialogOption[];
  /** Callback when user selects an option */
  onSelect: (optionId: string) => void;
  /** Callback when dialog is dismissed */
  onDismiss: () => void;
  /**
   * Optional: text for the cancel button (default: "Cancel"). Forwarded to
   * `ChoiceModal.cancelLabel` (added in the P2 consolidation, closing the
   * task-041 gap where this prop was accepted but not rendered).
   */
  cancelText?: string;
  /**
   * Optional `--sprk-ui-scale` factor, forwarded to `ChoiceModal` (task 041,
   * additive/backward-compatible; defaults to `SprkModal`'s own default of 1 when omitted).
   */
  uiScale?: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * ChoiceDialog - Rich option button dialog for 2-4 choices.
 *
 * @example
 * ```tsx
 * const options: IChoiceDialogOption[] = [
 *     { id: "resume", icon: <HistoryRegular />, title: "Resume", description: "Continue where you left off" },
 *     { id: "fresh", icon: <DocumentAddRegular />, title: "Start Fresh", description: "Begin new session" }
 * ];
 *
 * <ChoiceDialog
 *     open={open}
 *     title="Resume Session?"
 *     message="This analysis has existing history."
 *     options={options}
 *     onSelect={(id) => handleSelection(id)}
 *     onDismiss={() => setOpen(false)}
 * />
 * ```
 */
export const ChoiceDialog: React.FC<IChoiceDialogProps> = ({
  open,
  title,
  message,
  options,
  onSelect,
  onDismiss,
  cancelText,
  uiScale,
}) => {
  // Map the original {id, icon, title, description, disabled} option shape onto
  // ChoiceModal's {id, icon, label, description, disabled} choice shape (title → label
  // is the only rename; everything else is a direct passthrough — see task-041 notes).
  const choices: ChoiceModalChoice[] = options.map((option) => ({
    id: option.id,
    label: option.title,
    description: option.description,
    icon: option.icon,
    disabled: option.disabled,
  }));

  return (
    <ChoiceModal
      open={open}
      onClose={onDismiss}
      title={title}
      message={message}
      choices={choices}
      onSelect={onSelect}
      cancelLabel={cancelText}
      uiScale={uiScale}
    />
  );
};

export default ChoiceDialog;
