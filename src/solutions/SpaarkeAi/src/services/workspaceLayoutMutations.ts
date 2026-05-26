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
 * Rename a workspace layout. The BFF endpoint is a full PUT (replaces all
 * mutable fields), so we send the layout's existing `layoutTemplateId` +
 * `sectionsJson` + `isDefault` along with the new `name`.
 *
 * @returns The updated layout DTO returned by the BFF.
 * @throws Error with `status` property attached when the BFF returns non-2xx.
 *         403 indicates a system layout (cannot be renamed); 404 indicates
 *         the layout was not found or not owned by the user.
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
  const res = await auth.authenticatedFetch(url, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
      Accept: "application/json",
    },
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
