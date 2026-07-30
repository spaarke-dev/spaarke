/**
 * FileAttachSessionPrompt.tsx — the new-session vs add-to-current-session prompt shown when a
 * file is attached to a LIVE chat (ai-advanced-capabilities-analysis-hub-r1 task 024, spec FR-08).
 *
 * Scope: attaching a file mid-conversation must not silently pick a behavior (project constraint,
 * negative acceptance criterion). This component is a thin wrapper around the established
 * `ChoiceDialog` pattern (`.claude/patterns/ui/choice-dialog-pattern.md`, ADR-023) — a rich
 * 2-option button dialog, Cancel always present, no auto-selection. Kept as its OWN small file
 * (not folded into `ConversationPane.tsx`) per the task's merge-order constraint: `ConversationPane`
 * is a decomposed thin host (task 045 / FR-P3-06, ≤300-line budget) that
 * `spaarke-ai-architecture-redesign-r1` is concurrently maintaining — mirrors how task 023's
 * "Promote to Analysis…" affordance was kept self-contained in `HistoryOverlay.tsx` rather than
 * added to `ConversationPane.tsx` directly.
 *
 * Fluent v9 dialog + theme tokens (ADR-021): entirely inherited from `ChoiceDialog`, which uses
 * `tokens.*` exclusively (no hex/rgba) — dark mode is correct by construction (theme-token-driven).
 *
 * Wiring: the two choices call back into `useAttachments`' `chooseAddToCurrentSession` /
 * `prepareFilesForNewSession` (file-side bookkeeping only) — `ConversationPane` pairs the
 * "new session" choice with its EXISTING `handleNewSession` seam (clear + remount) so no new
 * client-side session/fork logic is introduced here (project constraint).
 */

import * as React from "react";
import { ChoiceDialog, type IChoiceDialogOption } from "@spaarke/ui-components";
import { ChatAddRegular, ArrowRightRegular } from "@fluentui/react-icons";
import type { PendingFileSessionChoice } from "./useAttachments";

export interface FileAttachSessionPromptProps {
  /** From `useAttachments().pendingFileSessionChoice` — null hides the dialog. */
  pending: PendingFileSessionChoice | null;
  /** "New session" chosen — start a fresh session, carrying the file(s) over. */
  onChooseNewSession: () => void;
  /** "Add to current session" chosen — keep the file(s) in this conversation. */
  onChooseAddToCurrent: () => void;
  /** Dismissed without deciding — no default is applied (see file header). */
  onDismiss: () => void;
}

function describeFiles(filenames: string[]): string {
  if (filenames.length === 1) return `"${filenames[0]}"`;
  if (filenames.length === 2) return `"${filenames[0]}" and "${filenames[1]}"`;
  return `${filenames.length} files`;
}

/**
 * FileAttachSessionPrompt — the two-choice dialog for attaching a file to a live chat.
 *
 * @example
 * ```tsx
 * <FileAttachSessionPrompt
 *   pending={attachments.pendingFileSessionChoice}
 *   onChooseNewSession={() => { attachments.prepareFilesForNewSession(); handleNewSession(); }}
 *   onChooseAddToCurrent={attachments.chooseAddToCurrentSession}
 *   onDismiss={attachments.dismissFileSessionChoice}
 * />
 * ```
 */
export const FileAttachSessionPrompt: React.FC<FileAttachSessionPromptProps> = ({
  pending,
  onChooseNewSession,
  onChooseAddToCurrent,
  onDismiss,
}) => {
  const filenames = pending?.filenames ?? [];
  const fileDescription = describeFiles(filenames);

  const options: IChoiceDialogOption[] = [
    {
      id: "new-session",
      icon: <ChatAddRegular />,
      title: "Start a new session",
      description: `Archive this conversation (you can switch back to it from History) and start a fresh session with ${fileDescription}.`,
    },
    {
      id: "add-to-current",
      icon: <ArrowRightRegular />,
      title: "Add to current session",
      description: `Keep ${fileDescription} in this conversation.`,
    },
  ];

  const handleSelect = React.useCallback(
    (optionId: string): void => {
      if (optionId === "new-session") {
        onChooseNewSession();
        return;
      }
      if (optionId === "add-to-current") {
        onChooseAddToCurrent();
        return;
      }
    },
    [onChooseNewSession, onChooseAddToCurrent]
  );

  return (
    <ChoiceDialog
      open={pending !== null}
      title="Attaching a file to this conversation"
      message={`This conversation already has messages. What would you like to do with ${fileDescription}?`}
      options={options}
      onSelect={handleSelect}
      onDismiss={onDismiss}
    />
  );
};

export default FileAttachSessionPrompt;
