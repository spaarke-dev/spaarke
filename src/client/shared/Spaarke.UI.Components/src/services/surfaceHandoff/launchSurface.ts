/**
 * surfaceHandoff/launchSurface.ts — the hand-off orchestrator (design §1 lifecycle).
 *
 * spaarkeai-assistant-enhancements-r1 task 012.
 *
 * ONE entry point — `launchSurface(input)` — that runs the full Class-2b
 * lifecycle for a `surface_launch`-dispatched create capability:
 *
 *   1. resolve the consumertype → surface via the client registry
 *   2. mint `handoffId = "h-<guid>"`
 *   3. build the entry-payload envelope (files by reference; preset merged
 *      authoritatively into draftValues) and write it to sessionStorage
 *   4. launch the surface (wizard web resource OR OOB entity form) via the
 *      REUSED `wizardLaunchers` primitives — the URL carries only the id, the
 *      payload rides sessionStorage
 *   5. await the `navigateTo` promise (resolves on modal close)
 *   6. read the outcome — wizards write `sprk.handoff.<id>.result`; OOB forms
 *      return a `savedEntityReference` on the navigate resolve
 *   7. clean up both sessionStorage keys
 *
 * The Assistant gates its honest "✅ Created …" claim on the returned
 * `result.committed` (P5 / task 020) — it structurally cannot fabricate success
 * because the outcome was written by the surface under the id the Assistant
 * minted (design §5).
 *
 * NO new BFF dispatch endpoint (compose invariant) — this is a pure CLIENT
 * action on a capability's already-stored `surface_launch` output.
 */

import type {
  ResolvedLookup,
  SurfaceHandoffEnvelope,
  SurfaceHandoffProvenance,
  SurfaceHandoffResult,
  SurfaceHandoffSource,
} from './types';
import { resolveSurfaceLaunch, type SurfaceLaunchRegistryEntry } from './surfaceLaunchRegistry';
import {
  DEFAULT_HANDOFF_TTL_SECONDS,
  clearHandoff,
  mintHandoffId,
  readHandoffResult,
  writeHandoffEnvelope,
} from './handoffStorage';
import {
  navigateToEntityRecordSurfaceAsync,
  navigateToWebResourceSurfaceAsync,
} from '../../components/WorkspaceShell/wizardLaunchers';

/** Input to a single surface launch. */
export interface LaunchSurfaceInput {
  /** The dispatched capability's `sprk_consumertype` (registry key). */
  readonly consumerType: string;
  /** Spaarke BFF base URL (forwarded to the wizard on the launch URL; ADR-028 — base only). */
  readonly bffBaseUrl: string;
  /** The Assistant-drafted free-text field values (logical-name keyed). */
  readonly draftValues?: Record<string, unknown>;
  /** Session-held SPE file references (NEVER binary). */
  readonly fileIds?: ReadonlyArray<string>;
  /**
   * Constrained-field resolver output (task 010). DEFINED here so callers may
   * pass it; POPULATED end-to-end by task 013. `{}` until then.
   */
  readonly resolvedLookups?: Record<string, ResolvedLookup>;
  /** Origin metadata (session / binding / turn). `consumerType` is stamped for you. */
  readonly source?: Omit<SurfaceHandoffSource, 'consumerType'>;
  /** Grounding provenance (source files / ledger keys). */
  readonly provenance?: SurfaceHandoffProvenance;
  /** Envelope TTL override (seconds). Defaults to {@link DEFAULT_HANDOFF_TTL_SECONDS}. */
  readonly ttlSeconds?: number;
}

/** The outcome of a launch attempt. */
export interface LaunchSurfaceOutcome {
  /** The minted hand-off id (empty when the consumertype was unmapped). */
  readonly handoffId: string;
  /**
   * `false` when the consumertype had no registry entry OR no Xrm host was
   * reachable (Vite dev / jsdom) — the caller degrades to "draft shown, nothing
   * opened" (surface-launch-mechanism §7), NOT a committed launch.
   */
  readonly launched: boolean;
  /** The resolved registry entry (undefined when unmapped). */
  readonly entry?: SurfaceLaunchRegistryEntry;
  /**
   * The surface outcome, once the modal closed. Undefined when `launched` is
   * false. `{committed:false, cancelled:true}` when the user dismissed.
   */
  readonly result?: SurfaceHandoffResult;
}

/**
 * Build the entry-payload envelope for a launch. Exported for unit testing +
 * for callers that want the envelope without launching (e.g. a dry-run preview).
 * Merges the registry preset into `draftValues` AUTHORITATIVELY (preset wins).
 */
export function buildHandoffEnvelope(
  handoffId: string,
  entry: SurfaceLaunchRegistryEntry,
  input: LaunchSurfaceInput
): SurfaceHandoffEnvelope {
  const mergedDraft: Record<string, unknown> = {
    ...(input.draftValues ?? {}),
    ...(entry.preset ?? {}), // preset is authoritative (e.g. the Task subtype GUID)
  };
  return {
    handoffId,
    source: { ...(input.source ?? {}), consumerType: input.consumerType },
    target: { surface: entry.surface, kind: entry.kind },
    fileIds: input.fileIds ?? [],
    draftValues: mergedDraft,
    resolvedLookups: input.resolvedLookups ?? {},
    provenance: input.provenance ?? {},
    createdAt: new Date().toISOString(),
    ttlSeconds: input.ttlSeconds ?? DEFAULT_HANDOFF_TTL_SECONDS,
  };
}

/**
 * Run the full hand-off lifecycle. Never throws — every failure mode degrades
 * to `launched:false` or a `cancelled` result so a create hand-off can never
 * crash the Assistant surface.
 */
export async function launchSurface(input: LaunchSurfaceInput): Promise<LaunchSurfaceOutcome> {
  const entry = resolveSurfaceLaunch(input.consumerType);
  if (!entry) {
    // Unmapped consumertype — draft shown, nothing opens (surface-launch §7).
    return { handoffId: '', launched: false };
  }

  const handoffId = mintHandoffId();
  const envelope = buildHandoffEnvelope(handoffId, entry, input);
  // Store BEFORE launch so the surface can read it the instant it boots.
  writeHandoffEnvelope(envelope);

  try {
    if (entry.kind === 'wizard') {
      // The URL carries only the id + bffBaseUrl; the payload rides sessionStorage.
      const data = `handoffId=${encodeURIComponent(handoffId)}&bffBaseUrl=${encodeURIComponent(input.bffBaseUrl)}`;
      const outcome = await navigateToWebResourceSurfaceAsync({
        webresourceName: entry.surface,
        data,
        title: entry.title,
      });
      if (!outcome.launched) {
        clearHandoff(handoffId);
        return { handoffId, launched: false, entry };
      }
      // Wizard wrote its outcome to sessionStorage (or nothing → treat as cancel).
      const written = readHandoffResult(handoffId);
      const result: SurfaceHandoffResult = written ?? { committed: false, cancelled: true };
      clearHandoff(handoffId);
      return { handoffId, launched: true, entry, result };
    }

    if (entry.kind === 'oob-form') {
      // OOB form: pre-seed rides the navigate `data` (thinner); the outcome rides
      // the navigate resolve (savedEntityReference) — an OOB form has no custom
      // code to write the sessionStorage result.
      const outcome = await navigateToEntityRecordSurfaceAsync({
        entityName: entry.surface,
        title: entry.title,
        defaultValues: envelope.draftValues,
      });
      if (!outcome.launched) {
        clearHandoff(handoffId);
        return { handoffId, launched: false, entry };
      }
      const result: SurfaceHandoffResult = outcome.savedEntityReference
        ? { committed: true, recordId: outcome.savedEntityReference.id }
        : { committed: false, cancelled: true };
      clearHandoff(handoffId);
      return { handoffId, launched: true, entry, result };
    }

    // workspace-tab / layout are Class-2a (in-app event bus), not this transport.
    clearHandoff(handoffId);
    return { handoffId, launched: false, entry };
  } catch {
    // Defensive: any unexpected failure cleans up and reports a non-committed launch.
    clearHandoff(handoffId);
    return { handoffId, launched: true, entry, result: { committed: false, cancelled: true } };
  }
}
