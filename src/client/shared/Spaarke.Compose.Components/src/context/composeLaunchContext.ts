/**
 * ComposeLaunchContext — cross-solution React context for the Compose modal
 * launch flow (Path A entry via the ribbon "Open in Compose" command).
 *
 * Project: spaarkeai-compose-r1
 * Phase:   Phase 7 three-pane pivot (task 092 introduced; task 093 hoisted
 *          to this shared lib so LegalWorkspace's `composeEditor.registration.ts`
 *          section factory can consume the same context that SpaarkeAi's
 *          `ThreePaneShell` provides).
 *
 * Why it lives in `@spaarke/compose-components`:
 *   The compose section factory in LegalWorkspace mounts `<ComposeWorkspace>`
 *   (from this same package) and needs to thread the document pointer that
 *   the ribbon launcher supplied via URL params. LegalWorkspace MUST NOT
 *   import from `src/solutions/SpaarkeAi/*` (unidirectional dependency graph
 *   per task 091 / POML acceptance #4). Therefore the context type + hook
 *   live here — the shared home both solutions depend on. SpaarkeAi's
 *   ThreePaneShell provides the value; LegalWorkspace's compose section
 *   factory consumes it via `useComposeLaunch()`.
 *
 * A null value means "no compose launch active" — consumers should treat
 * `null` as "not applicable" and fall through to default behaviour
 * (workspace picker + empty state / user-selected document).
 *
 * @see src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx
 *      — provides the value via <ComposeLaunchContext.Provider>
 * @see src/solutions/LegalWorkspace/src/sections/composeEditor.registration.ts
 *      — consumes the value inside the section factory mount
 */

import * as React from 'react';
import type { ComposeDocumentRef, ComposeUploadRef, ComposeDraftSeedRef } from '../types/compose-contracts';

export interface ComposeLaunchContextValue {
  /** Set to `'editor'` when the app was launched via ribbon Open-in-Compose. */
  composeMode: 'editor';
  /** Document pointer forwarded from the ribbon URL params (Path A entry). */
  document: ComposeDocumentRef | null;
  /** SPE container/drive id (may be empty; resolved at runtime if absent). */
  driveId: string;
  /**
   * FR-03 (task 012): transient upload-mount pointer. Present when the launch came from
   * a chat "open in Compose" on an Assistant-UPLOADED file (via the `workspace_open_tab`
   * frame's `widgetData.compose.upload`). Mutually exclusive with {@link document} — an
   * upload has no SPE pointer; Compose fetches its retained bytes and mounts them
   * transiently (create-on-save). Absent/undefined for the stored-document + picker paths.
   */
  upload?: ComposeUploadRef | null;
  /**
   * DEF-08 (spaarkeai-compose-r2): AI-drafted full-document SEED. Present when the launch opened a
   * Compose tab to materialize a chat-drafted document (a `compose-draft-document` output, Part A —
   * via the `workspace_open_tab` frame's `widgetData.compose.draft`; or the "Open in Compose"
   * per-message affordance, Part B — inline html). Mutually exclusive with {@link document} and
   * {@link upload}; the seed mounts as a TRANSIENT working draft (create-on-save). Absent/undefined
   * for the stored-document + upload + picker paths.
   */
  draft?: ComposeDraftSeedRef | null;
  /**
   * FR-05 create-on-save (task 100, gap 1.8): invoked once a transient draft is persisted as a
   * NEW `sprk_document` on first Save, with the server-minted `sprk_documentid`. SpaarkeAi's
   * ThreePaneShell (the only surface that can host the SpaarkeAi-owned
   * `useCreateOnSaveAssociation` hook) provides this; the LegalWorkspace compose section factory
   * forwards it to `<ComposeWorkspace onCreateOnSaveComplete>`. Absent on standalone /
   * picker-only mounts (association simply not offered). Kept on this shared context because the
   * section factory lives in LegalWorkspace and cannot import from `src/solutions/SpaarkeAi/*`.
   */
  onCreateOnSaveComplete?: (newSprkDocumentId: string) => void | Promise<void>;
}

export const ComposeLaunchContext = React.createContext<ComposeLaunchContextValue | null>(null);
ComposeLaunchContext.displayName = 'ComposeLaunchContext';

/**
 * Consume the Compose launch context from within any pane / section factory.
 *
 * Returns `null` if the app was NOT launched in Compose mode (standard three-
 * pane rendering, or standalone LegalWorkspace mount). Consumers should treat
 * `null` as "not applicable" and fall through to their default behaviour.
 */
export function useComposeLaunch(): ComposeLaunchContextValue | null {
  return React.useContext(ComposeLaunchContext);
}
