/**
 * @spaarke/ai-context - shared AI context TYPES (EntityContext, streaming + pane-event
 * shapes, IChatSession). Type-only since 2026-10-03 (reuse audit C-14).
 *
 * Standards: ADR-012 (shared library rules), ADR-020 (versioning)
 * Version: 1.0.0
 *
 * NOT PCF-safe — this library uses React 19 APIs.
 * Consumers: SpaarkeAi Code Page, future AI-enabled Code Pages.
 */

// Types
export * from './types';

// Hooks (useChatSession / useChatContextMapping / useChatPlaybooks) and the
// ChatApiClient service were DELETED 2026-10-03 (reuse audit C-14): the
// "extracted for reuse" hooks were never adopted — zero consumers.

// Providers barrel removed 2026-07-07 (redesign-r1 task 050): the R1 standalone
// provider trio was deleted in Track-B batch 3 and the orphaned useEntityResolver
// hook was removed by the Track-B completion audit — the directory emptied out.
