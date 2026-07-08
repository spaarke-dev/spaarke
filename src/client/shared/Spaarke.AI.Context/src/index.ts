/**
 * @spaarke/ai-context - AI context providers, service clients, and hooks
 *
 * Standards: ADR-012 (shared library rules), ADR-020 (versioning)
 * Version: 1.0.0
 *
 * NOT PCF-safe — this library uses React 19 APIs.
 * Consumers: SpaarkeAi Code Page, future AI-enabled Code Pages.
 */

// Types
export * from './types';

// Hooks
export * from './hooks';

// Services
export * from './services';

// Providers barrel removed 2026-07-07 (redesign-r1 task 050): the R1 standalone
// provider trio was deleted in Track-B batch 3 and the orphaned useEntityResolver
// hook was removed by the Track-B completion audit — the directory emptied out.
