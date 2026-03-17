/**
 * PCF-Safe Barrel Export
 *
 * This entry point exports ONLY components, hooks, services, and types that are
 * verified compatible with React 16/17 (the PCF platform-provided version).
 *
 * PCF controls MUST import from this entry point:
 *   import { FindSimilarDialog } from '@spaarke/ui-components/src/pcf-safe';
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
export { RelationshipCountCard } from './components/RelationshipCountCard';
export { FindSimilarDialog } from './components/FindSimilar/FindSimilarDialog';
export { MiniGraph } from './components/MiniGraph';
export { SendEmailDialog } from './components/SendEmailDialog';
export type { ISendEmailPayload } from './components/SendEmailDialog';
export { AiSummaryPopover } from './components/AiSummaryPopover';

// ─── Hooks (PCF-safe — React 16 compatible) ────────────────────────────────
export { useAiSummary } from './hooks';
export type { DocumentSummaryState, SummaryStatus, SummaryDocument, ExtractedEntities } from './hooks';
export { useSseStream } from './hooks';
export type { SseStreamStatus, UseSseStreamOptions } from './hooks';

// ─── Services (no React dependency) ────────────────────────────────────────
export {
  FileUploadService,
  DocumentRecordService,
  MultiFileUploadService,
  NavMapClient,
  SdapApiClient,
} from './services/document-upload';
export type { SdapApiClientOptions, OnUnauthorizedCallback } from './services/document-upload';

// ─── Types (no React dependency) ────────────────────────────────────────────
export type { MiniGraphNode, MiniGraphEdge } from './types/MiniGraphTypes';
export type { ILookupItem } from './types/LookupTypes';
export type { DrillInteraction } from './components/DatasetGrid/types';

// ─── Utilities (no React dependency) ────────────────────────────────────────
export { createLogger } from './utils/logger';
