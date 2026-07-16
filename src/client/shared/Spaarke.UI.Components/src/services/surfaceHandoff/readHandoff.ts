/**
 * surfaceHandoff/readHandoff.ts — the LAUNCHED-SURFACE read side (design §1 steps 4–6, 8–9).
 *
 * spaarkeai-assistant-enhancements-r1 task 012.
 *
 * A launched Create*Wizard Code Page (or any surface opened by
 * `launchSurface`) uses these to:
 *   - read its OWN `handoffId` from its launch URL (`data=handoffId=…`),
 *   - hydrate the entry-payload envelope from sessionStorage,
 *   - write its outcome back (`committed` / `cancelled`) on close.
 *
 * The per-wizard MAPPING of `envelope.draftValues` / `resolvedLookups` into a
 * given wizard's form-state shape (e.g. CreateMatterWizard's `initialFormValues`)
 * is the "smart pre-seed" of TASK 013 — this module deliberately exposes the
 * hydrated envelope + a typed seed accessor and STOPS there. See the `handoffSeed`
 * accessor JSDoc for the 013 seam.
 *
 * ⚠️ FALLBACK SEAM (design §2 build-verify #1): this reads `sessionStorage`,
 * which is shared with the launcher ONLY if the modal iframe is same-origin +
 * non-sandboxed. If a deployment sandboxes the wizard iframe, `readHandoff`
 * returns `null` (no envelope) and the wizard opens un-seeded (the pre-existing
 * behavior — no regression). The documented cross-context fallbacks (localStorage
 * by id, or a BFF fetch-by-id) would slot in HERE without changing the caller.
 */

import { parseDataParams } from '../../utils/parseDataParams';
import type { ResolvedLookup, SurfaceHandoffEnvelope, SurfaceHandoffResult } from './types';
import { clearHandoff, readHandoffEnvelope, writeHandoffResult } from './handoffStorage';

/** The hydrated hand-off context a launched surface sees. */
export interface HandoffContext {
  /** The hand-off id read from the surface's own launch URL. */
  readonly handoffId: string;
  /**
   * The hydrated envelope, or `null` when absent/expired/unreadable (sandboxed
   * iframe fallback). A `null` envelope with a non-empty `handoffId` still lets
   * the surface write a result (the id round-trips even if storage wasn't shared
   * for the payload — a defensive, not-expected case).
   */
  readonly envelope: SurfaceHandoffEnvelope | null;
}

/**
 * Read the hand-off id from a launch URL (defaults to `window.location.search`)
 * and hydrate its envelope. Returns `null` when the URL carries no `handoffId`
 * (the surface was opened directly, not via a hand-off) so callers can branch
 * cleanly on "was I launched from the Assistant?".
 *
 * @param search optional query string for test injection (mirrors parseDataParams).
 */
export function readHandoffFromUrl(search?: string): HandoffContext | null {
  const params = parseDataParams(search);
  const handoffId = params.handoffId;
  if (typeof handoffId !== 'string' || handoffId.length === 0) return null;
  return { handoffId, envelope: readHandoffEnvelope(handoffId) };
}

/**
 * The typed seed a wizard pre-fills from. TASK 013 maps these onto each wizard's
 * form-state (this is intentionally generic — the field-name translation per
 * wizard is 013 scope, not 012). Returns `null` when there is nothing to seed.
 */
export interface HandoffSeed {
  readonly draftValues: Record<string, unknown>;
  readonly resolvedLookups: Record<string, ResolvedLookup>;
  readonly fileIds: ReadonlyArray<string>;
}

/**
 * Extract the pre-seed payload from a hand-off context. The 013 "smart pre-seed"
 * task consumes THIS to hydrate a specific wizard's initial form values +
 * pre-select its resolved dropdowns. Returns `null` when there is no usable
 * envelope (nothing to seed).
 */
export function handoffSeed(ctx: HandoffContext | null): HandoffSeed | null {
  if (!ctx?.envelope) return null;
  const { draftValues, resolvedLookups, fileIds } = ctx.envelope;
  const hasSeed =
    Object.keys(draftValues ?? {}).length > 0 ||
    Object.keys(resolvedLookups ?? {}).length > 0 ||
    (fileIds?.length ?? 0) > 0;
  if (!hasSeed) return null;
  return {
    draftValues: draftValues ?? {},
    resolvedLookups: resolvedLookups ?? {},
    fileIds: fileIds ?? [],
  };
}

/**
 * Write a COMMITTED outcome (a real record was created) for a hand-off id. The
 * Assistant reads this when the `navigateTo` promise resolves and gates its
 * honest "✅ Created …" claim on it (P5 / task 020).
 */
export function completeHandoff(handoffId: string, recordId?: string): void {
  const result: SurfaceHandoffResult = { committed: true, recordId };
  writeHandoffResult(handoffId, result);
}

/**
 * Write a CANCELLED outcome for a hand-off id (user dismissed without committing).
 * The orchestrator also infers cancellation when no result was written, so this
 * is belt-and-suspenders — but writing it explicitly makes the intent unambiguous.
 */
export function cancelHandoff(handoffId: string): void {
  const result: SurfaceHandoffResult = { committed: false, cancelled: true };
  writeHandoffResult(handoffId, result);
}

/** Re-export cleanup so a surface can self-clean if it owns the full lifecycle. */
export { clearHandoff };
