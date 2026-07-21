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
 */

export * from './widgets';
