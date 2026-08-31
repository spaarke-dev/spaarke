/**
 * versionHistory.ts — Documents surface version-history client (task 051).
 *
 * Thin client for the USER-CONTEXT (OBO) version-history endpoint pair
 * (src/server/api/Sprk.Bff.Api/Api/DocumentVersionEndpoints.cs):
 *
 *   GET /api/documents/{documentId}/versions
 *       → VersionInfoDto[] (id, eTag, lastModifiedDateTime, size), newest first
 *   GET /api/documents/{documentId}/versions/{versionId}/content
 *       → the EXACT bytes of the named prior version (read-only)
 *
 * ⚠️ unified-access-control-r2 task 079 — these routes were RE-KEYED from
 * `(driveId, itemId)` to `{documentId}`. The old pair
 *
 *   GET /api/obo/drives/{driveId}/items/{itemId}/versions[/{versionId}/content]
 *
 * is DELETED, not deprecated. It let any caller holding a container ACL read
 * the version history and PRIOR-VERSION BYTES of an arbitrary SPE item with no
 * per-document check. Pass the `sprk_document` row id; the server reads the
 * drive/item off that row AFTER authorizing the caller for it. Do not
 * reintroduce a drive-keyed call here.
 *
 * Auth model (ADR-028): every call goes through `@spaarke/auth`
 * `authenticatedFetch` under the CALLING USER's OBO token. Authorization is a
 * per-document Dataverse decision on the server (Read on the document, the same
 * gate as the current-version download) — NOT, as this comment previously
 * claimed, the SPE layer's container-scoped answer. This module still never
 * calls the admin/app-only version surface (`ContainerItemEndpoints.cs`).
 *
 * SCOPE (binding, per task 051): view/open read-only ONLY. No restore, no
 * branch-from, no version-state mutation of any kind is exposed here.
 */

import { resolveRuntimeConfig, initAuth, authenticatedFetch } from "@spaarke/auth";

// ---------------------------------------------------------------------------
// Types — camelCase projection of the BFF's VersionInfoDto
// ---------------------------------------------------------------------------

export interface IVersionInfo {
  /** SPE version id (e.g. "3.0"). Doubles as the display label. */
  id: string;
  eTag?: string | null;
  /** ISO-8601 timestamp of the version's last modification. */
  lastModifiedDateTime: string;
  /** Version size in bytes. */
  size: number;
}

// ---------------------------------------------------------------------------
// Auth bootstrap — canonical Code Page pattern (ADR-028)
// ---------------------------------------------------------------------------

let _initPromise: Promise<void> | null = null;

/**
 * Initialize `@spaarke/auth` once, on first use (the Documents list itself
 * stays Xrm.WebApi-only; auth is only needed for the version-history calls).
 *
 * `requireSilentOnly: true` — this dialog is MDA-embedded and must NEVER pop
 * an involuntary sign-in window (ADR-028 INV-5; precedent: WorkspaceLayoutWizard).
 * `proactiveRefresh: false` — short-lived dialog (precedent: DailyBriefing).
 */
export function ensureAuthInitialized(): Promise<void> {
  if (!_initPromise) {
    _initPromise = (async () => {
      try {
        const config = await resolveRuntimeConfig();
        await initAuth({
          clientId: config.msalClientId,
          bffBaseUrl: config.bffBaseUrl,
          bffApiScope: config.bffOAuthScope,
          tenantId: config.tenantId || undefined,
          proactiveRefresh: false,
          requireSilentOnly: true,
        });
        console.info("[AllDocuments] @spaarke/auth initialized successfully");
      } catch (err) {
        console.warn("[AllDocuments] @spaarke/auth initialization failed", err);
        _initPromise = null; // allow retry
        throw err;
      }
    })();
  }
  return _initPromise;
}

// ---------------------------------------------------------------------------
// OBO endpoint calls (task-050 contract — the ONLY fetch paths in this module)
// ---------------------------------------------------------------------------

function versionsPath(documentId: string): string {
  return `/api/documents/${encodeURIComponent(documentId)}/versions`;
}

/** List a document's SPE version history (newest first) as the calling user. */
export async function listVersions(documentId: string): Promise<IVersionInfo[]> {
  await ensureAuthInitialized();
  const response = await authenticatedFetch(versionsPath(documentId));
  return (await response.json()) as IVersionInfo[];
}

/** Fetch the exact bytes of a specific prior version, read-only, as the calling user. */
export async function fetchVersionBytes(
  documentId: string,
  versionId: string
): Promise<Blob> {
  await ensureAuthInitialized();
  const response = await authenticatedFetch(
    `${versionsPath(documentId)}/${encodeURIComponent(versionId)}/content`
  );
  return await response.blob();
}

// ---------------------------------------------------------------------------
// Open helpers — read-only view of the exact bytes
// ---------------------------------------------------------------------------

/** MIME types a browser can render inline in a new tab. */
const INLINE_VIEWABLE: Record<string, string> = {
  pdf: "application/pdf",
  png: "image/png",
  jpg: "image/jpeg",
  jpeg: "image/jpeg",
  gif: "image/gif",
  txt: "text/plain",
};

/**
 * Open a prior version READ-ONLY: browser-viewable types (pdf, images, txt)
 * open in a new tab from a blob URL; everything else (docx, xlsx, …) is saved
 * as a clearly-labeled read-only copy. Either way the user gets the EXACT
 * bytes of the prior version — never a restore, never a branch.
 */
export async function openPriorVersionReadOnly(
  documentId: string,
  versionId: string,
  documentName: string,
  fileType?: string
): Promise<void> {
  const rawBlob = await fetchVersionBytes(documentId, versionId);

  const ext = (fileType ?? "").toLowerCase().replace(/^\./, "");
  const inlineMime = INLINE_VIEWABLE[ext];

  if (inlineMime) {
    const viewBlob = rawBlob.slice(0, rawBlob.size, inlineMime);
    const url = URL.createObjectURL(viewBlob);
    window.open(url, "_blank", "noopener");
    // Give the new tab time to load the blob before revoking.
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
    return;
  }

  // Non-inline types: download the exact bytes as a read-only-labeled copy.
  const base = documentName.replace(/\.[A-Za-z0-9]+$/, "");
  const suffix = ext ? `.${ext}` : "";
  const fileName = `${base} (version ${versionId}, read-only)${suffix}`;
  const url = URL.createObjectURL(rawBlob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

// ---------------------------------------------------------------------------
// Display formatting
// ---------------------------------------------------------------------------

/** Format a byte count for display (e.g. "1.2 MB"). */
export function formatSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return "";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Format a version timestamp for display (date + time). */
export function formatVersionTimestamp(iso: string): string {
  try {
    return new Date(iso).toLocaleString(undefined, {
      month: "short",
      day: "numeric",
      year: "numeric",
      hour: "numeric",
      minute: "2-digit",
    });
  } catch {
    return iso;
  }
}
