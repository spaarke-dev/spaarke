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

import * as React from "react";
import type { ComposeDocumentRef } from "../types/compose-contracts";

export interface ComposeLaunchContextValue {
  /** Set to `'editor'` when the app was launched via ribbon Open-in-Compose. */
  composeMode: "editor";
  /** Document pointer forwarded from the ribbon URL params (Path A entry). */
  document: ComposeDocumentRef | null;
  /** SPE container/drive id (may be empty; resolved at runtime if absent). */
  driveId: string;
}

export const ComposeLaunchContext = React.createContext<ComposeLaunchContextValue | null>(
  null,
);
ComposeLaunchContext.displayName = "ComposeLaunchContext";

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
