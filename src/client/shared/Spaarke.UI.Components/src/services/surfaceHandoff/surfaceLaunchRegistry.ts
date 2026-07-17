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

/** One registry entry: how to launch a given consumertype's create surface. */
export interface SurfaceLaunchRegistryEntry {
  /** Launch-adapter discriminator (design §3). */
  readonly kind: SurfaceKind;
  /**
   * The concrete surface id — a web-resource name (`wizard`) or an entity
   * logical name (`oob-form`).
   */
  readonly surface: string;
  /** Modal dialog / form title. */
  readonly title: string;
  /**
   * Static preset values merged into `draftValues` AUTHORITATIVELY (they win
   * over drafted values on key conflict) — e.g. the Event Task-subtype GUID.
   * Absent when the surface needs no preset.
   */
  readonly preset?: Record<string, unknown>;
}

/**
 * The R1 create-intent routing table (surface-launch-mechanism §3). Keyed by
 * `sprk_consumertype`. Adding a new create intent = one entry here + (for a new
 * wizard kind) one launcher in `wizardLaunchers.ts`.
 */
export const SURFACE_LAUNCH_REGISTRY: Readonly<Record<string, SurfaceLaunchRegistryEntry>> = {
  'create-matter': {
    kind: 'wizard',
    surface: 'sprk_creatematterwizard',
    title: 'Create New Matter',
  },
  'create-task': {
    kind: 'wizard',
    surface: 'sprk_createeventwizard',
    title: 'Create New Event',
    // Task-flavored Event subtype preset (authoritative — the capability is
    // specifically "create a task", so the Event wizard opens on the Task subtype).
    preset: { sprk_eventtype_ref: EVENT_SUBTYPE_TASK_GUID },
  },
  'create-todo': {
    kind: 'oob-form',
    surface: 'sprk_todo',
    title: 'New To Do',
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
