/**
 * securityTrimService — read-time security trimming for Navigator rows
 * (spaarke-side-pane-navigation-history-r1 task 080, spec FR-12 / NFR-04).
 *
 * The Recent/Pinned tabs render CACHED display names off `sprk_navitem`
 * (a per-user, previously-captured/pinned label). If the signed-in user
 * subsequently loses access to the underlying record (ethical-wall
 * revocation, matter unassignment, etc.), that cached name must never render
 * again — this is a legal-context confidentiality requirement, not a UX nicety.
 *
 * This module re-validates each row's target with a lightweight
 * `Xrm.WebApi.retrieveRecord` call, host-context, under the SIGNED-IN USER
 * (never a BFF/plugin/service-account call — project HARD constraint) and
 * classifies the result into one of three buckets:
 *
 *   - `accessible` — the retrieve succeeded (or the row has no checkable
 *     record identity — see "Exemptions" below). Render normally.
 *   - `denied`      — the retrieve failed with a clear 403 (insufficient
 *     privilege) or 404 (record no longer exists) signal. The row's cached
 *     name is a confidentiality leak and MUST be trimmed (dropped) by the
 *     caller.
 *   - `transient`   — the retrieve failed for any OTHER reason (network
 *     error, timeout, 5xx, unrecognized/ambiguous error shape, or `Xrm` not
 *     being available at all). This is NOT authoritative evidence of lost
 *     access — the caller MUST keep the row rather than permanently
 *     dropping an otherwise-accessible record on a transient blip (root
 *     constraint, task 080).
 *
 * `classifyTargetAccess`/`classifyTargets` NEVER throw — every failure mode
 * (including an unexpected exception inside the classifier itself) resolves
 * to a classification, defaulting to `transient` when the evidence is
 * ambiguous. This mirrors `navItemRepository.ts`'s "never throw out of a
 * capture-path helper" convention, but for a read/render path instead of a
 * write path.
 *
 * ── Exemptions (accessible without a retrieve — no cached RECORD name at risk) ──
 *
 * Only `sprk_pagetype = EntityRecord` rows (a specific Dataverse record
 * instance, `sprk_targetlogicalname` + `sprk_targetid` together identifying
 * one row) carry a cached label that could leak a confidential record's
 * existence/name. Every other pagetype is classified `accessible` WITHOUT
 * issuing a retrieve:
 *
 *   - `WebLink` (100000003) — a raw non-Dataverse URL (or a pasted/bookmarked
 *     external link). No Dataverse target at all; nothing to leak.
 *   - `EntityList` (100000001) — a saved-view bookmark. `sprk_targetid` on
 *     these rows holds the VIEW's id (`savedqueryid`), NOT a record id in
 *     `sprk_targetlogicalname`'s entity set (see `PinnedTab.tsx`'s
 *     `navigateToRow` `viewId` handling, task 051). Calling
 *     `retrieveRecord(targetlogicalname, targetid)` on a view-bookmark row
 *     would look up a NON-EXISTENT record (wrong id space) and reliably
 *     404 — that would incorrectly trim every view bookmark as "denied"
 *     regardless of the user's actual access. The cached label here is a
 *     VIEW name, not a specific confidential record's name, so there is no
 *     leak risk to begin with.
 *   - `Custom` (100000002) — an unresolvable custom page (OQ-6). Never has a
 *     `sprk_targetlogicalname`/`sprk_targetid` pair (see `RecentTab.tsx`'s
 *     `chipForRow` OQ-6 note) — nothing to check.
 *
 * This exemption logic lives HERE (not duplicated per-tab) so both
 * `RecentTab.tsx` and `PinnedTab.tsx` get the same correct behavior for
 * free, and so the false-404-on-view-bookmark bug above can't be
 * reintroduced by a future caller.
 *
 * @see RecentTab.tsx (task 041/080) — Viewed rows, batched via {@link classifyTargets} before `setRows`
 * @see PinnedTab.tsx (task 050/080) — Records + EntityRecord-pagetype Bookmarks, same batched call
 * @see navItemRepository.ts — `NavItemRecord` shape + `NavItemPageType` option-set constants (reused, not re-declared)
 * @see EventDetailSidePane/src/services/eventService.ts — the `retrieveRecord` + error-parse pattern this mirrors
 * @see projects/spaarke-side-pane-navigation-history-r1/notes/task-080-security-trimming.md — anti-flash approach, classification rules, Monitored-group exemption rationale
 */

import { NavItemPageType, type NavItemRecord } from '@spaarke/ui-components/services/navigator/navItemRepository';
import type { XrmContext } from '@spaarke/ui-components';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export type TrimClassification = 'accessible' | 'denied' | 'transient';

/**
 * A single re-check target. `key` is the caller's stable id for looking the
 * result up afterward (Navigator rows use `sprk_navitemid`; Monitored-style
 * live-query rows — not trimmed here, see module docblock — would use their
 * own id shape if a future caller ever needed to).
 */
export interface TrimTarget {
  key: string;
  logicalName: string | null;
  id: string | null;
  /** One of {@link NavItemPageType}. */
  pageType: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Row -> target adapter
// ─────────────────────────────────────────────────────────────────────────────

/** Build a {@link TrimTarget} from a `sprk_navitem` row (Recent/Pinned tabs' shared row shape). */
export function trimTargetFromRow(row: NavItemRecord): TrimTarget {
  return {
    key: row.sprk_navitemid,
    logicalName: row.sprk_targetlogicalname,
    id: row.sprk_targetid,
    pageType: row.sprk_pagetype,
  };
}

/**
 * Whether a target identifies a single, checkable Dataverse record instance
 * (only `EntityRecord` pagetype rows do — see module docblock "Exemptions").
 */
export function isRecordTarget(target: Pick<TrimTarget, 'pageType' | 'logicalName' | 'id'>): boolean {
  return target.pageType === NavItemPageType.EntityRecord && !!target.logicalName && !!target.id;
}

// ─────────────────────────────────────────────────────────────────────────────
// Error classification
// ─────────────────────────────────────────────────────────────────────────────

/** Best-effort extraction of an HTTP-shaped status code from a WebApi error, if present. */
function extractStatus(err: unknown): number | undefined {
  if (err && typeof err === 'object') {
    const anyErr = err as Record<string, unknown>;
    const direct = anyErr.status ?? anyErr.statusCode;
    if (typeof direct === 'number') return direct;
    const raw = anyErr.raw as Record<string, unknown> | undefined;
    if (raw && typeof raw.status === 'number') return raw.status;
  }
  return undefined;
}

function extractMessage(err: unknown): string {
  if (err instanceof Error) return err.message;
  if (typeof err === 'string') return err;
  try {
    return JSON.stringify(err) ?? String(err);
  } catch {
    return String(err);
  }
}

/**
 * Signals that reliably indicate the signed-in user has LOST ACCESS (403) or
 * the record NO LONGER EXISTS (404) — the two cases that MUST trim the row.
 * Sourced from the real Dataverse Web API / Xrm.WebApi client SDK error
 * message shapes (mirrors `navItemRepository.ts`'s `parseWebApiError` +
 * `eventService.ts`'s `parseWebApiError`, both of which already key on
 * "privilege" / "does not exist" / "could not be found" for the same
 * underlying error family):
 *   - 403 / "insufficient privilege(s)" / "does not have ... permission" /
 *     "access is denied" / "forbidden"
 *   - 404 / "does not exist" / "could not be found" / "was not found" /
 *     "not found" (Dataverse's `retrieveRecord` 404 message is literally
 *     "<entity> With Id = <id> Does Not Exist")
 */
const DENIED_SIGNALS: RegExp[] = [
  /\b403\b/,
  /\b404\b/,
  /forbidden/,
  /privileg/, // "privilege"/"privileges"/"insufficient privileges"
  /does not have (adequate )?permission/,
  /access is denied/,
  /does not exist/,
  /could not be found/,
  /was not found/,
  /\bnot found\b/,
];

/**
 * Classify a WebApi error as `denied` (403/404-equivalent — trim the row) or
 * `transient` (network/timeout/5xx/unrecognized — keep the row, not
 * authoritative). Defaults to `transient` whenever the evidence is
 * ambiguous — the 403/404-vs-transient distinction is a MUST-trim-only-on-
 * clear-signal rule, never the reverse (root constraint, task 080).
 */
export function classifyWebApiError(err: unknown): 'denied' | 'transient' {
  try {
    const status = extractStatus(err);
    if (status === 403 || status === 404) return 'denied';
    if (status !== undefined && status >= 500) return 'transient';

    const message = extractMessage(err).toLowerCase();
    if (DENIED_SIGNALS.some(re => re.test(message))) return 'denied';

    // Network/timeout/fetch failures, 5xx, and anything else unrecognized —
    // no clear denial signal, so treat as transient (keep the row).
    return 'transient';
  } catch {
    // classifyWebApiError must NEVER throw (task constraint).
    return 'transient';
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Classification
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Classify a single target's access. NEVER throws — every failure path
 * (including `xrm`/`xrm.WebApi` being unavailable, or an unexpected
 * exception inside this function) resolves to a classification.
 *
 * Uses a minimal `$select` of the entity's own primary-key attribute
 * (`<logicalName>id` — the standard Dataverse convention for every
 * Spaarke `sprk_*` custom entity, matching the id-field convention already
 * relied on by this project's test fixtures/services) rather than fetching
 * the full record, to keep the re-check lightweight (task constraint).
 */
export async function classifyTargetAccess(
  xrm: XrmContext | undefined,
  target: TrimTarget
): Promise<TrimClassification> {
  try {
    if (!isRecordTarget(target)) return 'accessible'; // see module docblock "Exemptions"

    const logicalName = target.logicalName as string;
    const id = target.id as string;

    if (!xrm?.WebApi) {
      // Can't verify at all — this is an environment/availability problem,
      // not evidence of lost access. Never assume denial without a signal.
      return 'transient';
    }

    try {
      await xrm.WebApi.retrieveRecord(logicalName, id, `?$select=${logicalName}id`);
      return 'accessible';
    } catch (err) {
      return classifyWebApiError(err);
    }
  } catch {
    // Defensive outer guard — this function must NEVER throw (task constraint).
    return 'transient';
  }
}

/**
 * Batch-classify multiple targets concurrently (`Promise.all` — task's
 * "batch efficiently" note; correctness over micro-optimizing round-trip
 * count). Returns a `Map` keyed by each target's `key` so callers can look
 * up a row's classification by its own id.
 */
export async function classifyTargets(
  xrm: XrmContext | undefined,
  targets: TrimTarget[]
): Promise<Map<string, TrimClassification>> {
  const entries = await Promise.all(
    targets.map(async target => [target.key, await classifyTargetAccess(xrm, target)] as const)
  );
  return new Map(entries);
}
