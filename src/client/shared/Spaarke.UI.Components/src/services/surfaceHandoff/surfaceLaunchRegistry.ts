/**
 * surfaceHandoff/surfaceLaunchRegistry.ts — the CLIENT launch registry.
 *
 * spaarkeai-assistant-enhancements-r1 task 012. Source-of-truth for the
 * consumertype → surface routing, per `surface-launch-mechanism.md` §3 (the
 * catalog-side contract authored by task 002).
 *
 * WHY CLIENT-SIDE (not the server catalog): per BFF hygiene §10 the server
 * catalog names the *capability* (`sprk_consumertype`); the concrete
 * web-resource / entity name is a CLIENT deployment concern and stays here.
 * The server ledger carries the consumertype through the `surface_launch`
 * SessionOutput (`UcId = binding.Ucid ?? binding.ConsumerType`, OutputRouter.cs);
 * the client maps it to a surface via THIS registry (ADR-039 — one decider; the
 * client carries zero intent detection, only a static lookup).
 *
 * This registry EXTENDS the shipped `wizardLaunchers.ts` family — it does not
 * fork it (CLAUDE.md §11). The launch adapters in `launchSurface.ts` delegate
 * to those launchers for the `wizard` kind.
 */

import type { SurfaceKind } from './types';

/**
 * The Event subtype GUID for a "Task"-flavored `sprk_event` (the `create-task`
 * consumer's preset). Sourced from task 012 instructions / surface-launch §3.
 * Carried into the envelope as an authoritative `draftValues` preset so the
 * Event wizard opens on the Task subtype.
 */
export const EVENT_SUBTYPE_TASK_GUID = '124f5fc9-98ff-f011-8406-7c1e525abd8b';

/** One registry entry: how to launch a given consumertype's surface. */
export interface SurfaceLaunchRegistryEntry {
  /** Launch-adapter discriminator (design §3). */
  readonly kind: SurfaceKind;
  /**
   * The concrete surface id, interpreted per `kind`:
   *  - `wizard`        → a web-resource name (e.g. `sprk_creatematterwizard`).
   *  - `oob-form`      → an entity logical name (e.g. `sprk_todo`).
   *  - `workspace-tab` → a registered workspace **widget type**
   *                      (e.g. `my-tasks-list`) opened via the PaneEventBus.
   *  - `layout`        → a workspace layout id.
   * Client-side only per BFF hygiene §10 — the server catalog names the
   * capability (`sprk_consumertype`); the concrete surface is a deployment
   * concern that lives HERE, in code (not maker-authored data).
   */
  readonly surface: string;
  /** Modal dialog / form title, or the workspace-tab display name. */
  readonly title: string;
  /**
   * Static preset values merged into `draftValues` AUTHORITATIVELY (they win
   * over drafted values on key conflict) — e.g. the Event Task-subtype GUID.
   * Applies to the sessionStorage-hand-off kinds (`wizard` / `oob-form`).
   * Absent when the surface needs no preset.
   */
  readonly preset?: Record<string, unknown>;
  /**
   * Optional `widgetData` for the event-bus kinds (`workspace-tab` / `layout`):
   * the payload forwarded to the workspace widget on `widget_load` (e.g. a
   * `configId` for a generic grid widget). Absent = `{}` (the widget carries its
   * own defaults, e.g. `my-tasks-list` bakes its `sprk_gridconfiguration` id).
   */
  readonly widgetData?: Record<string, unknown>;
}

/**
 * The consumertype → surface routing table (surface-launch-mechanism §3). Keyed
 * by `sprk_consumertype`. This is the SINGLE, traceable source of truth for
 * "which surface does this capability's Binding push to" — a developer reading
 * this table sees exactly what every `surface_launch` bundle opens.
 *
 * Adding a new surface-bearing capability = ONE entry here:
 *  - `wizard`        → also add a launcher in `wizardLaunchers.ts` for a genuinely
 *                      new web-resource transport (existing wizards reuse the shared one).
 *  - `oob-form`      → set `surface` to the entity logical name.
 *  - `workspace-tab` → set `surface` to a registered workspace widget type; the
 *                      client dispatches it on the PaneEventBus `widget_load` channel
 *                      (no sessionStorage hand-off). This is how grids/widgets open.
 *
 * Surface identity stays in CODE by design (BFF hygiene §10 / ADR-039): the
 * server catalog carries only the capability (`consumerType`); makers enhance
 * Actions/tool-descriptions in data, never surfaces. `handleSurfaceLaunch`
 * branches purely on the resolved entry's `kind` — no per-consumerType literals.
 */
export const SURFACE_LAUNCH_REGISTRY: Readonly<Record<string, SurfaceLaunchRegistryEntry>> = {
  'create-matter': {
    kind: 'wizard',
    surface: 'sprk_creatematterwizard',
    title: 'Create New Matter',
  },
  // spaarkeai-assistant-enhancements-r1 UAT #1 (2026-07-18): create-project mirrors
  // create-matter — the Assistant drafts the project (name/description/type/area) and
  // launches the pre-seeded Create Project wizard. Backed by the create-project Binding
  // (Surface Launch) + CREATE-PROJECT@v1 Action authored in spaarkedev1.
  'create-project': {
    kind: 'wizard',
    surface: 'sprk_createprojectwizard',
    title: 'Create New Project',
  },
  'create-task': {
    kind: 'wizard',
    surface: 'sprk_createeventwizard',
    title: 'Create New Event',
    // Task-flavored Event subtype preset (authoritative — the capability is
    // specifically "create a task", so the Event wizard opens on the Task subtype).
    // D-013-03: carry the fixed display NAME alongside the GUID so the wizard's
    // event-type LOOKUP renders "Task" instead of a blank label (the lookup shows
    // `eventTypeName`, not a value resolved from the id). "Task" is the system
    // subtype record's `sprk_name` (verified in dev).
    preset: { sprk_eventtype_ref: EVENT_SUBTYPE_TASK_GUID, sprk_eventtype_ref_name: 'Task' },
  },
  'create-todo': {
    kind: 'oob-form',
    surface: 'sprk_todo',
    title: 'New To Do',
  },
  // UAT R5-8 (2026-07-20): Quick Start "Assign Work" launches the Create Work Assignment wizard
  // through the hand-off envelope so the session's attached file(s) ride along (parity with
  // create-matter/-project/-event). The wizard's Add Files step is pre-seeded via initialFileRefs.
  'create-work-assignment': {
    kind: 'wizard',
    surface: 'sprk_createworkassignmentwizard',
    title: 'Create Work Assignment',
  },
  // UAT R5-8 (2026-07-20): Quick Start "Summarize Files" launches through the hand-off envelope so
  // the session's attached file(s) pre-seed the upload step (fetched from session bytes, not the
  // old file-less documentIds URL param).
  'summarize-files': {
    kind: 'wizard',
    surface: 'sprk_summarizefileswizard',
    title: 'Summarize Files',
  },
  // UAT R5-8 (2026-07-20): Quick Start "Find Similar" launches through the hand-off envelope so the
  // session's first attached file pre-selects on the content input (Find Similar is single-document).
  'find-similar': {
    kind: 'wizard',
    surface: 'sprk_findsimilar',
    title: 'Find Similar Documents',
  },
  // task 050 (2026-07-22): the `list-tasks` capability opens a WORKSPACE GRID TAB
  // (the shared DataverseEntityViewWidget "My Tasks" — membership-scoped via the
  // grid config's behavior.membershipFilter). A grid tab is a Class-2a event-bus
  // open (PaneEventBus `widget_load`), NOT a wizard/OOB sessionStorage hand-off —
  // hence `kind: 'workspace-tab'`, `surface` = the registered widget type. This
  // registry entry REPLACES the former hardcoded `if consumerType === 'list-tasks'`
  // branch in ConversationPane.handleSurfaceLaunch, so any future grid/widget
  // surface is one entry here — no code branch.
  'list-tasks': {
    kind: 'workspace-tab',
    surface: 'my-tasks-list',
    title: 'My Tasks',
  },
};

/**
 * Resolve a consumertype to its launch entry. Returns `undefined` for an
 * unmapped consumertype (the caller degrades to "draft shown, nothing opens"
 * per surface-launch-mechanism §7 — never throws).
 */
export function resolveSurfaceLaunch(consumerType: string | null | undefined): SurfaceLaunchRegistryEntry | undefined {
  if (typeof consumerType !== 'string' || consumerType.length === 0) return undefined;
  return SURFACE_LAUNCH_REGISTRY[consumerType];
}
