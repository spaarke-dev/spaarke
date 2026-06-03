/**
 * workspaceLayoutMutations.ts — Thin client-side helpers wrapping the BFF
 * workspace-layout mutation endpoints (PUT / DELETE).
 *
 * Created for task 093 (Manage Workspaces side pane). The existing BFF
 * endpoints (`WorkspaceLayoutEndpoints.cs`) already expose the full CRUD
 * surface — this file is a small typed wrapper that side-pane components
 * (rename + delete actions) call instead of inlining `authenticatedFetch`
 * boilerplate. No new BFF endpoints were added — this is purely a client-side
 * convenience layer, per CLAUDE.md §10 BFF Hygiene.
 *
 * Why this file exists (vs. inlining into the side pane):
 *   The side pane (`ManageWorkspacesPane.tsx`) has at least three mutation
 *   sites (rename via inline-edit Enter, delete via confirmation dialog,
 *   future: set-as-default toggle). Keeping the BFF URL + payload shape in
 *   one place avoids drift between call sites and makes future endpoint
 *   refactors a one-file change.
 *
 * Standards:
 *   - ADR-012: SpaarkeAi-local service (depends on `useAiSession` semantics).
 *   - ADR-028: BFF calls via `authenticatedFetch` only — no token snapshots,
 *     no module-level fetch.
 *   - BFF Hygiene (CLAUDE.md §10): zero new BFF endpoints. All four endpoints
 *     consumed below (GET list, GET by id, PUT update, DELETE) pre-existed
 *     `WorkspaceLayoutEndpoints.cs` shipped in earlier rounds.
 */

import { buildBffApiUrl } from "@spaarke/auth";
import type { WorkspaceLayoutDto } from "../hooks/useWorkspaceLayouts";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Auth surface required by every mutation helper. Sourced from `useAiSession()`
 * at the call site — passed in explicitly so this file has no React dep and
 * stays pure-TS testable.
 */
export interface MutationAuth {
  bffBaseUrl: string;
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
}

/**
 * Request payload accepted by the BFF `PUT /api/workspace/layouts/{id}`
 * endpoint. Shape mirrors `UpdateWorkspaceLayoutRequest` in
 * `WorkspaceLayoutDtos.cs` — name + layoutTemplateId + sectionsJson +
 * isDefault are all required by the BFF (it does a full overwrite, not a
 * patch). Callers that only want to RENAME the layout pass the layout's
 * existing values for the other three fields.
 */
export interface UpdateLayoutPayload {
  name: string;
  layoutTemplateId: string;
  sectionsJson: string;
  isDefault: boolean;
}

// ---------------------------------------------------------------------------
// Rename — convenience wrapper around the full PUT update.
// ---------------------------------------------------------------------------

/**
 * Convert an ISO-8601 `modifiedOn` string to a weak HTTP ETag value per
 * RFC 7232 §2.3, matching the BFF's <c>FormatWeakETag</c> helper. Returns
 * <c>W/"&lt;ticks&gt;"</c> — UTC ticks (Int64) inside double quotes, prefixed
 * by <c>W/</c>.
 *
 * Ticks calculation mirrors C#: `(unixMs * 10_000) + 621_355_968_000_000_000`
 * (the .NET epoch offset from 0001-01-01 to Unix epoch). We keep this
 * client-side rather than relying on a server-emitted ETag header because:
 *   1. The cached `WorkspaceLayoutDto` already carries `modifiedOn` —
 *      reading it from there is cheaper than tracking ETag headers per row.
 *   2. The BFF DOES emit an ETag header on single-layout GETs, but
 *      cached layouts (loaded via the list endpoint) need a per-row value.
 *      Deriving from `modifiedOn` is consistent for both code paths.
 *
 * R4 task 054 (B-5 / FR-08).
 */
export function buildIfMatchFromModifiedOn(modifiedOn: string): string | null {
  if (!modifiedOn) return null;
  // Sentinel from hard-coded system layouts — no ETag (those return 403
  // anyway on PUT, so the header is moot).
  if (modifiedOn.startsWith("1970-01-01")) return 'W/"0"';
  const ms = Date.parse(modifiedOn);
  if (Number.isNaN(ms)) return null;
  // .NET ticks: 10_000 ticks/ms + epoch offset.
  const ticks = BigInt(ms) * 10000n + 621355968000000000n;
  return `W/"${ticks.toString()}"`;
}

/**
 * Rename a workspace layout. The BFF endpoint is a full PUT (replaces all
 * mutable fields), so we send the layout's existing `layoutTemplateId` +
 * `sectionsJson` + `isDefault` along with the new `name`.
 *
 * R4 task 054 (B-5 / FR-08): sends an `If-Match` header derived from the
 * layout's cached `modifiedOn` for optimistic concurrency control. The BFF
 * returns 412 Precondition Failed if the layout was modified by another
 * session since this client loaded it.
 *
 * @returns The updated layout DTO returned by the BFF.
 * @throws Error with `status` property attached when the BFF returns non-2xx.
 *         403 = system layout (cannot be renamed);
 *         404 = layout not found or not owned by the user;
 *         412 = If-Match mismatch (concurrent edit — refresh and retry).
 */
export async function renameWorkspaceLayout(
  layout: WorkspaceLayoutDto,
  newName: string,
  auth: MutationAuth,
): Promise<WorkspaceLayoutDto> {
  const payload: UpdateLayoutPayload = {
    name: newName,
    layoutTemplateId: layout.layoutTemplateId,
    sectionsJson: layout.sectionsJson,
    isDefault: layout.isDefault,
  };
  const url = buildBffApiUrl(
    auth.bffBaseUrl,
    `/workspace/layouts/${encodeURIComponent(layout.id)}`,
  );
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "application/json",
  };
  // R4 task 054 (B-5): attach If-Match when available. Missing modifiedOn
  // (legacy cached records) → server runs in soft-mode (last-write-wins).
  const ifMatch = buildIfMatchFromModifiedOn(layout.modifiedOn ?? "");
  if (ifMatch) {
    headers["If-Match"] = ifMatch;
  }
  const res = await auth.authenticatedFetch(url, {
    method: "PUT",
    headers,
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const err = new Error(
      `Rename workspace layout failed: HTTP ${res.status}`,
    ) as Error & { status?: number };
    err.status = res.status;
    throw err;
  }
  return (await res.json()) as WorkspaceLayoutDto;
}

// ---------------------------------------------------------------------------
// Delete — DELETE /api/workspace/layouts/{id}
// ---------------------------------------------------------------------------

/**
 * Delete a workspace layout. The BFF performs a soft-delete (Dataverse
 * deactivation). System layouts return 403 Forbidden — callers must disable
 * the delete affordance for system layouts before calling this.
 *
 * @throws Error with `status` property attached when the BFF returns non-2xx.
 *         403 = system layout (refused); 404 = not found / not owned.
 */
export async function deleteWorkspaceLayout(
  layoutId: string,
  auth: MutationAuth,
): Promise<void> {
  const url = buildBffApiUrl(
    auth.bffBaseUrl,
    `/workspace/layouts/${encodeURIComponent(layoutId)}`,
  );
  const res = await auth.authenticatedFetch(url, {
    method: "DELETE",
    headers: {
      Accept: "application/json",
    },
  });
  // BFF returns 204 No Content on success.
  if (!res.ok) {
    const err = new Error(
      `Delete workspace layout failed: HTTP ${res.status}`,
    ) as Error & { status?: number };
    err.status = res.status;
    throw err;
  }
}
