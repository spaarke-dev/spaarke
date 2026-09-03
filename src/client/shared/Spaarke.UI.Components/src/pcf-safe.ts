/**
 * PCF-Safe Barrel Export
 *
 * This entry point exports ONLY components, hooks, services, and types that are
 * verified compatible with React 16/17 (the PCF platform-provided version).
 *
 * PCF controls MUST import from this entry point:
 *   import { RelationshipCountCard } from '@spaarke/ui-components/src/pcf-safe';
 *
 * Code pages should import from the main barrel:
 *   import { SprkChat, WizardShell } from '@spaarke/ui-components';
 *
 * RULES FOR THIS FILE:
 * - NEVER export components that use React 18+ APIs (useId, useDeferredValue,
 *   useSyncExternalStore, use(), createRoot, etc.)
 * - NEVER export components that depend on Lexical (requires React 18+)
 * - NEVER export components that use Fluent UI v9 Portals with React 18 features
 * - All exports here must work with React.createElement / ReactDOM.render patterns
 *
 * @see ADR-022 — PCF controls use platform-provided React 16/17
 */

// ─── Components (PCF-safe) ─────────────────────────────────────────────────
// SprkModal + ModalWindowControls (spaarke-modal-system P5, task 070): the
// canonical modal shell is structurally React-16/17-safe (useState/useEffect/
// useCallback only — NO useId; the aria-labelledby id comes from a module
// counter for exactly this reason) and is added here per the P0 decision that
// "a specific preset can be added to pcf-safe later at low cost if a PCF ever
// needs one" — CommunicationConversationPanel's ConversationModal is that
// consumer. ModalWindowControls was already PCF-consumed via the MAIN barrel
// (a pre-existing deviation this export retires).
export { SprkModal } from './components/SprkModal/SprkModal';
export type {
  SprkModalProps,
  SprkModalDismiss,
  SprkModalBodyScroll,
  SprkModalNav,
} from './components/SprkModal/SprkModal';
export { ModalWindowControls } from './components/ModalWindowControls/ModalWindowControls';
export type { IModalWindowControlsProps } from './components/ModalWindowControls/ModalWindowControls';
export { RelationshipCountCard } from './components/RelationshipCountCard';
export { MiniGraph } from './components/MiniGraph';
export { AiSummaryPopover } from './components/AiSummaryPopover';

// ─── Hooks (PCF-safe — React 16 compatible) ────────────────────────────────
export { useAiSummary } from './hooks';
export type { DocumentSummaryState, SummaryStatus, SummaryDocument, ExtractedEntities } from './hooks';
// Note: useSseStream is React 18+ only (uses SprkChat internals) — NOT pcf-safe

// ─── Services (no React dependency) ────────────────────────────────────────
export {
  FileUploadService,
  DocumentRecordService,
  MultiFileUploadService,
  NavMapClient,
} from './services/document-upload';
// `SdapApiClient` + `SdapApiClientOptions` + `OnUnauthorizedCallback` were REMOVED from this
// surface 2026-09-03. They belonged to this package's parallel upload client, which is deleted —
// `FileUploadService` now takes the one from `@spaarke/sdap-client`, so consumers construct it as:
//
//     import { SdapApiClient } from '@spaarke/sdap-client';
//     new FileUploadService(new SdapApiClient({ baseUrl, authenticatedFetch }), logger);

// ─── Types (no React dependency) ────────────────────────────────────────────
export type { MiniGraphNode, MiniGraphEdge } from './types/MiniGraphTypes';
export type { ILookupItem } from './types/LookupTypes';
// DrillInteraction moved to @spaarke/visuals (VHVU-042). Import it from there:
//   import type { DrillInteraction } from '@spaarke/visuals/types';

// ─── Utilities (no React dependency) ────────────────────────────────────────
export { createLogger } from './utils/logger';
