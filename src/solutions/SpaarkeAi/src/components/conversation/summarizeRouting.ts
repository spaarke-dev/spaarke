/**
 * summarizeRouting.ts — pure summarize-intent routing + local-message helpers.
 *
 * Extracted verbatim from ConversationPane.tsx by ai-architecture-redesign-r1
 * task 045 (FR-P3-06 — ConversationPane decomposes to a thin host ≤300 lines).
 * ConversationPane re-exports this module's public surface so existing test
 * imports (`from '../ConversationPane'`) keep resolving.
 *
 * Everything here is PURE (no React, no IO, no state) — trivially testable
 * with plain inputs. See ConversationPane.r5.test.tsx for the contract tests.
 */

import type { IChatMessage } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// /summarize tri-mode intent routing (R5 task 019 / D2-10)
// ---------------------------------------------------------------------------
//
// R5 turned the `/summarize` slash command into a tri-mode dispatcher per
// FR-03. The routing decision is a PURE function of the input message + the
// host context the ConversationPane already knows about:
//   1. Has the active chat session received uploaded files this session?
//   2. Does the host have an active workspace document context (R3 wizard)?
//
// Spec wording (NFR-12 + plan D2-10):
//   - description: "Summarize uploaded files or the active document"
//   - interjection: "Upload the file(s) you'd like me to summarize"
// Both strings are spec-driven; do NOT change them without updating spec.md.

/**
 * Trigger prefix for the /summarize slash command. Lowercased; the slash
 * command menu writes the canonical trigger verbatim into the textarea so
 * a case-sensitive prefix match is safe.
 */
export const SUMMARIZE_SLASH_PREFIX = '/summarize';

/**
 * Deterministic Assistant interjection emitted on branch (c) — the FR-03
 * prompt-first ordering. Rendered locally as an Assistant message; NO
 * playbook invocation, NO BFF round-trip.
 */
export const SUMMARIZE_PROMPT_FIRST_INTERJECTION =
  "Upload the file(s) you'd like me to summarize";

/**
 * Discriminated routing decision returned by {@link routeSummarizeIntent}.
 *
 * - `session-files`   → branch (a). The active session has uploaded files;
 *   the message falls through to the default SprkChat send funnel (the
 *   multi-file combined-summary interjection is emitted before send).
 * - `active-document` → branch (b). No uploaded files but the host has an
 *   active workspace document context; falls through to the default send.
 * - `prompt-first`    → branch (c). Neither uploads nor document — the
 *   deterministic interjection tells the user what to do next.
 * - `not-summarize`   → not a /summarize invocation; pass through unchanged.
 */
export type SummarizeRouteDecision =
  | { kind: 'session-files'; messageText: string }
  | { kind: 'active-document'; messageText: string }
  | { kind: 'prompt-first'; messageText: string; interjection: string }
  | { kind: 'not-summarize'; messageText: string };

/**
 * Minimal host-context inputs for {@link routeSummarizeIntent}. Decoupled
 * from `IChatSession` / `entityContext` so the helper is trivially testable.
 */
export interface SummarizeIntentInputs {
  /** Count of files uploaded into THIS chat session (composer chip mirror). */
  uploadedFileCount: number;
  /** Whether the host has an active workspace document context. */
  hasActiveWorkspaceDocument: boolean;
}

/**
 * Pure tri-mode routing decision for `/summarize` per FR-03.
 *
 * Deterministic and total — every combination of inputs yields exactly one
 * of the four decision kinds. No IO, no state mutation.
 */
export function routeSummarizeIntent(
  messageText: string,
  inputs: SummarizeIntentInputs
): SummarizeRouteDecision {
  // Accept the canonical lowercase form OR a case-insensitive variant
  // ("/Summarize", "/SUMMARIZE"). Re-trim so the helper is robust on its own.
  const trimmed = messageText.trim();
  const isSummarize =
    trimmed.length >= SUMMARIZE_SLASH_PREFIX.length &&
    trimmed.slice(0, SUMMARIZE_SLASH_PREFIX.length).toLowerCase() ===
      SUMMARIZE_SLASH_PREFIX;

  if (!isSummarize) {
    return { kind: 'not-summarize', messageText };
  }

  // Branch (a): uploaded files take precedence — the user expressed intent
  // by uploading files into the chat session before invoking the command.
  if (inputs.uploadedFileCount > 0) {
    return { kind: 'session-files', messageText };
  }

  // Branch (b): no uploaded files but an active workspace document is the
  // R3 wizard's natural input. Fall through to the existing flow unchanged.
  if (inputs.hasActiveWorkspaceDocument) {
    return { kind: 'active-document', messageText };
  }

  // Branch (c) — FR-03 prompt-first path.
  return {
    kind: 'prompt-first',
    messageText,
    interjection: SUMMARIZE_PROMPT_FIRST_INTERJECTION,
  };
}

// ---------------------------------------------------------------------------
// Inline confirmation + interjection helpers (R5 task 020 / D2-11)
// ---------------------------------------------------------------------------

/**
 * Maximum number of filenames listed inline in the file-confirmation message.
 * Names beyond this cap collapse into an "...and N more" suffix.
 */
export const FILE_CONFIRMATION_MAX_NAMES = 3;

/**
 * Build the deterministic inline file-confirmation message body emitted when
 * one or more files transition to `status === 'ready'`:
 *
 *   1 file   → "I have your file: a.pdf"
 *   2+ files → "I have your N files: a.pdf, b.docx, c.md"
 *   >FILE_CONFIRMATION_MAX_NAMES files → ", and N more" suffix
 *
 * Pure / total. Returns `null` when the filenames array is empty.
 */
export function buildFileConfirmationMessage(filenames: readonly string[]): string | null {
  if (filenames.length === 0) return null;
  if (filenames.length === 1) {
    return `I have your file: ${filenames[0]}`;
  }
  const visible = filenames.slice(0, FILE_CONFIRMATION_MAX_NAMES);
  const remaining = filenames.length - visible.length;
  const list = visible.join(", ");
  if (remaining > 0) {
    return `I have your ${filenames.length} files: ${list}, and ${remaining} more`;
  }
  return `I have your ${filenames.length} files: ${list}`;
}

/**
 * Build the deterministic Assistant interjection emitted on a multi-file
 * combined-summary turn (R5 task 020; FR-03 session-files branch):
 *
 *   N>=2 → "I'll combine all N files into a single summary."
 *
 * Does NOT fire for N=1 (single-file Summarize uses the per-file affordance).
 * Returns `null` when fileCount < 2. Pure / total / deterministic so a
 * `useRef`-based once-per-turn guard at the call site yields exactly-once.
 */
export function buildMultiFileSummarizeInterjection(fileCount: number): string | null {
  if (fileCount < 2) return null;
  return `I'll combine all ${fileCount} files into a single summary.`;
}

/**
 * Wrap a plain text body in an `IChatMessage` shape suitable for the
 * `injectLocalMessage` prop on SprkChat (R5 task 020 / D2-11).
 *
 * Per R5 spec FR-03 + ADR-012 these messages are CLIENT-RENDERED only — the
 * BFF chat history does NOT contain them.
 */
export function makeLocalAssistantMessage(content: string): IChatMessage {
  return {
    role: "Assistant",
    content,
    timestamp: new Date().toISOString(),
    metadata: { responseType: "markdown" },
  };
}
