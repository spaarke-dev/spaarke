/**
 * @spaarke/ai-widgets — Pinned Memory wire contracts (PART A handoff)
 *
 * Wire-format DTOs + shared constants for the Pinned Memory UI surface
 * (R6 task 070 PART B). Mirror the contracts the PART A BFF endpoint pair
 * (`PinnedMemoryEndpoints.cs` at `/api/memory/pins`) exposes — see the PART
 * A evidence note `projects/spaarke-ai-platform-unification-r6/notes/task-070-partA-evidence.md`
 * for the canonical contract definition.
 *
 * Single source of truth for the shape — every UI component in `memory/`
 * (the list widget + the edit dialog + the delete confirmation) imports
 * from this file. If the BFF contract drifts (e.g. a `source` field lands
 * per the PART A follow-up), update this file FIRST.
 *
 * Standards:
 *   - ADR-013: UI consumes BFF endpoints via standard HTTP; this file
 *     defines the wire shape only and contains zero behaviour.
 *
 * Task: R6-070 (D-C-24 / D-C-25, Pillar 7, Q7 scope expansion) — PART B.
 */

// ---------------------------------------------------------------------------
// Length caps (mirror PART A backend constants)
// ---------------------------------------------------------------------------

/** Backend cap on `title` length (PinnedContextRepository.MaxTitleLength). */
export const MAX_PIN_TITLE_LENGTH = 200;

/** Backend cap on `content` length (PinnedContextRepository.MaxContentLength). */
export const MAX_PIN_CONTENT_LENGTH = 1000;

// ---------------------------------------------------------------------------
// Pin types
// ---------------------------------------------------------------------------

/**
 * The three pinType discriminator values. Mirrors the
 * `PinnedContextItem.PinType` field and the backend validation in
 * `PinnedMemoryEndpoints.MapPinType`.
 */
export const PIN_TYPE_VALUES = ['user-preference', 'system-rule', 'matter-fact'] as const;

export type PinType = (typeof PIN_TYPE_VALUES)[number];

/**
 * Display metadata for each pin type — used by the edit dialog's RadioGroup
 * to render label + hint per option. Order matches the natural reading order
 * (most common first).
 */
export const PIN_TYPES: ReadonlyArray<{ value: PinType; label: string; hint: string }> = [
  {
    value: 'user-preference',
    label: 'User preference',
    hint: 'Personal style / formatting / phrasing preferences the assistant should follow.',
  },
  {
    value: 'system-rule',
    label: 'System rule',
    hint: 'Cross-cutting rule the assistant must always observe in this account.',
  },
  {
    value: 'matter-fact',
    label: 'Matter fact',
    hint: 'A fact about a specific matter. Requires a matter selection below.',
  },
] as const;

// ---------------------------------------------------------------------------
// Wire DTOs (server contract — PART A)
// ---------------------------------------------------------------------------

/**
 * PinDto — the canonical UI display contract returned by the BFF.
 *
 * Source: PART A evidence note — `PinnedMemoryEndpoints.PinDto`. See that
 * file for the rationale on each field. Authoritative names + casing live
 * on the server.
 */
export interface PinDto {
  pinId: string;
  pinType: PinType;
  title: string;
  content: string;
  matterId?: string | null;
  createdAt: string;
  updatedAt: string;
  createdBy: string;
}

/**
 * Request body for POST /api/memory/pins (Create) AND PUT /api/memory/pins/{pinId}
 * (Edit). The shape is identical — PUT is a full replacement model per PART A.
 */
export interface PinUpsertRequest {
  title: string;
  content: string;
  pinType: PinType;
  /** Required when `pinType === "matter-fact"`; omitted otherwise. */
  matterId?: string;
}

/** Response body for GET /api/memory/pins. */
export interface PinListResponse {
  items: PinDto[];
  count: number;
}

/** Response body for POST /api/memory/pins (201) and PUT /api/memory/pins/{pinId} (200). */
export interface PinUpsertResponse {
  item: PinDto;
}

/**
 * ProblemDetails error envelope (RFC 7807) — mirrors what the BFF returns on
 * 400 / 401 / 403 / 404 / 429 / 500. Defined loosely here because the UI only
 * reads `title` + `detail` for inline display; richer extensions are ignored.
 */
export interface ProblemDetailsLike {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  // Allow unknown additional fields without widening to `any`.
  [extra: string]: unknown;
}

// ---------------------------------------------------------------------------
// URL helpers
// ---------------------------------------------------------------------------

/**
 * Base path for the Pinned Memory endpoint pair, per PART A. NOT the full URL
 * — callers compose this with `buildBffApiUrl(bffBaseUrl, …)` from
 * `@spaarke/auth`.
 */
export const PINNED_MEMORY_BASE_PATH = '/api/memory/pins';

/** Build the path for the GET list endpoint with optional matterId filter. */
export function buildListPath(matterId?: string): string {
  if (matterId && matterId.length > 0) {
    return `${PINNED_MEMORY_BASE_PATH}?matterId=${encodeURIComponent(matterId)}`;
  }
  return PINNED_MEMORY_BASE_PATH;
}

/** Build the path for the PUT / DELETE per-pin endpoint. */
export function buildPinPath(pinId: string): string {
  return `${PINNED_MEMORY_BASE_PATH}/${encodeURIComponent(pinId)}`;
}
