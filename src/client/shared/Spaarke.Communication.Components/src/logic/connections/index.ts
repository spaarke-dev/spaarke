/**
 * `@spaarke/communication-components/logic/connections` — React-agnostic (Layer 1)
 * logic for the multi-association review surface, extracted verbatim from the
 * production `CommunicationConnections` PCF (email-communication-solution-r5 task
 * 020, per spec FR-13/FR-18 two-layer split + design Lens 4).
 *
 * Two pure-TS modules, NO React import (NFR-05):
 *   - `provenance.ts`            — parse/derive/group helpers + the provenance/
 *                                  connection type contracts (zero imports).
 *   - `ConnectionsWriteHandler.ts` — the ADDITIVE multi-lookup regarding write path
 *                                  (`applyRegardingSelection`, never clear-and-set;
 *                                  ADR-045). Delegates the SET to the shared
 *                                  `applyResolverFields` behind an injected
 *                                  `IResolverWriteContext` — host-agnostic.
 *
 * Consumed by BOTH the OOB-form PCF (React 16 view) and the future Phase-3 code-page
 * review view (React 19) — one copy of the logic, no React component shared across
 * the PCF boundary (ADR-022 slim-first).
 */

export * from './provenance';
export * from './ConnectionsWriteHandler';
