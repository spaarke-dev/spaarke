/**
 * surfaceHandoff — the Assistant → launched-surface entry-payload hand-off.
 *
 * spaarkeai-assistant-enhancements-r1 task 012 (spec FR-A1 / FR-A3). The client
 * transport that carries a draft-in-chat create flow into a pre-seeded create
 * surface (custom wizard OR OOB form modal). Barrel for the module.
 *
 * Consumers:
 *  - Assistant host (SpaarkeAi) — `launchSurface(...)` on a `surface_launch`
 *    dispatch result (the launch/return path).
 *  - Launched Create*Wizard Code Pages — `readHandoffFromUrl` / `handoffSeed` /
 *    `completeHandoff` (the read/pre-seed/return side; task 013 does the
 *    per-wizard field mapping).
 */

export type {
  SurfaceKind,
  ResolvedLookup,
  SurfaceHandoffSource,
  SurfaceHandoffTarget,
  SurfaceHandoffProvenance,
  SurfaceHandoffEnvelope,
  SurfaceHandoffResult,
} from './types';

export { SURFACE_LAUNCH_REGISTRY, EVENT_SUBTYPE_TASK_GUID, resolveSurfaceLaunch } from './surfaceLaunchRegistry';
export type { SurfaceLaunchRegistryEntry } from './surfaceLaunchRegistry';

export {
  HANDOFF_KEY_PREFIX,
  DEFAULT_HANDOFF_TTL_SECONDS,
  handoffEnvelopeKey,
  handoffResultKey,
  mintHandoffId,
  writeHandoffEnvelope,
  readHandoffEnvelope,
  isHandoffExpired,
  writeHandoffResult,
  readHandoffResult,
  clearHandoff,
} from './handoffStorage';

export { launchSurface, buildHandoffEnvelope, normalizeOobFormDefaults } from './launchSurface';
export type { LaunchSurfaceInput, LaunchSurfaceOutcome } from './launchSurface';

export { readHandoffFromUrl, handoffSeed, completeHandoff, completeOrClose, cancelHandoff } from './readHandoff';
export type { HandoffContext, HandoffSeed } from './readHandoff';
