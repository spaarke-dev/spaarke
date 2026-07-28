/**
 * @spaarke/communication-components — shared React components for the
 * Communications workspace surface.
 *
 * Home of the rich Pattern D `CommunicationsWorkspaceWidget` consumed by:
 *   - `src/solutions/LegalWorkspace/src/sections/communications.registration.ts`
 *     (Dashboard-wrapper section shim, `id: "communications"`)
 *   - `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts`
 *     (Direct-wrapper registration, type string `communications-list`)
 *
 * §11 justification (messaging-communication-app-r2 task 030): neither
 * `@spaarke/events-components` (entity-coupled to calendar/event semantics)
 * nor `@spaarke/ai-widgets` (thin-generic widget-registry layer) is the right
 * home for rich communication-widget content — this package is the cohesive
 * home, mirroring the Calendar Pattern D precedent
 * (`@spaarke/events-components`).
 *
 * ADR-012 (shared component library — context-agnostic, no LegalWorkspace
 * coupling), ADR-021 (Fluent v9 tokens only).
 *
 * `./logic` (added email-communication-solution-r5 task 020) is the React-agnostic
 * Layer-1 logic shared across the PCF ↔ code-page boundary (ADR-022 slim-first).
 * PCF consumers deep-import the specific sub-barrel (e.g.
 * `@spaarke/communication-components/logic/connections`,
 * `@spaarke/communication-components/logic/attachments`) to avoid pulling the
 * React-19 widget graph; the barrel re-export below is for React-19 code-page /
 * Vite consumers.
 *
 * `./components` (added task 021) holds Layer-2 presentational components that
 * are React-version-agnostic (no React-18/19-only runtime API, no
 * `as React.ComponentType` cast) — e.g. `AttachmentList`, reused by the
 * OOB-form PCF (deep-imported source) and the Phase-3 reading-pane view.
 */

export * from './widgets';
export * from './logic';
export * from './components';
