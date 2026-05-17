/**
 * @spaarke/ai-context — Providers
 *
 * React context providers for AI state.
 * Extracted from AnalysisWorkspace in Wave 1 (tasks 010-012).
 */

// Entity resolver hook (URL params + Xrm frame-walk)
export { useEntityResolver } from './useEntityResolver';

// Standalone AI context provider + consumer hook
export { StandaloneAiProvider } from './StandaloneAiContext';
export { useStandaloneAi } from './useStandaloneAi';
