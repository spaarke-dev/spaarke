/**
 * @spaarke/ai-context — Providers
 *
 * React context providers for AI state.
 * Extracted from AnalysisWorkspace in Wave 1 (tasks 010-012).
 */

// Entity resolver hook (URL params + Xrm frame-walk)
export { useEntityResolver } from './useEntityResolver';

// The R1 standalone AI provider + consumer hook were deleted (Track-B batch 3,
// ai-architecture-redesign-r1) — superseded by AiSessionProvider / useAiSession
// in @spaarke/ai-widgets.
