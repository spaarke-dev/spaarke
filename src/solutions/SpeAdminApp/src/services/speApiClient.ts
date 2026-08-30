/**
 * speApiClient - Typed API client for all /api/spe/* BFF endpoints.
 *
 * Uses authenticatedFetch() from ./authInit (wraps @spaarke/auth with lazy init).
 * authenticatedFetch handles:
 *   - Bearer token acquisition (MSAL or token bridge)
 *   - 401 retry with exponential backoff
 *   - RFC 7807 ProblemDetails parsing
 *   - Throwing ApiError / AuthError on failure
 *
 * All public methods are typed with request and response interfaces
 * from types/spe.ts. Callers receive typed results or a thrown
 * ApiError / AuthError which they can display to the user.
 */

import { ApiError, AuthError } from "@spaarke/auth";
import { authenticatedFetch } from "./authInit";
import type {
  BusinessUnit,
  SpeEnvironment,
  SpeEnvironmentUpsert,
  SpeContainerTypeConfig,
  SpeContainerTypeConfigUpsert,
  ContainerType,
  ContainerTypePermission,
  ContainerTypeOwner,
  Container,
  ArchivalActionAccepted,
  ContainerCustomProperty,
  ContainerPermission,
  ContainerPermissionUpsert,
  ColumnDefinition,
  ColumnDefinitionUpsert,
  DriveItem,
  DriveItemVersion,
  Thumbnail,
  SharingLink,
  SharingLinkType,
  SharingLinkScope,
  DashboardMetrics,
  AuditLogEntry,
  AuditCategory,
  SecurityAlert,
  SecureScore,
  SearchRequest,
  ContainerSearchResult,
  DriveItemSearchResult,
  DeletedContainer,
  BulkOperationAccepted,
  BulkOperationStatus,
  BulkDeleteRequest,
  BulkPermissionsRequest,
  ConsumingTenant,
  ConsumingTenantListResponse,
  RegisterConsumingTenantRequest,
  UpdateConsumingTenantRequest,
} from "../types/spe";

// Re-export error types for consumer convenience
export { ApiError, AuthError };

// ---------------------------------------------------------------------------
// Error description
// ---------------------------------------------------------------------------

/** Reads a ProblemDetails extension as a non-empty string, or undefined. */
function extension(problem: Record<string, unknown> | null | undefined, key: string): string | undefined {
  const value = problem?.[key];
  return typeof value === "string" && value.trim().length > 0 ? value : undefined;
}

/**
 * Describes a caught error for display, preserving everything the BFF sent.
 *
 * `ApiError.message` is already the RFC 7807 `detail` (authenticatedFetch puts it there), and since
 * task 001 that detail carries the real Graph/Dataverse error rather than a hardcoded guess. What was
 * still being dropped is the diagnostic set in `problemDetails` — the Graph error code and, most
 * importantly, the **request id**, which is the value an admin quotes to Microsoft support. This appends
 * them.
 *
 * @param err       The caught value. Anything — ApiError, Error, or a non-Error throw.
 * @param fallback  Used ONLY when nothing descriptive can be recovered. Never overrides a real message.
 *
 * Added by sdap-SPE-admin-app-r2 task 001 (spec FR-A01).
 */
export function describeApiError(err: unknown, fallback = ""): string {
  if (err instanceof ApiError) {
    const problem = err.problemDetails as Record<string, unknown> | null;
    const base = err.message || extension(problem, "title") || fallback;

    const graphCode = extension(problem, "graphErrorCode");
    const requestId = extension(problem, "graphRequestId");
    const traceId = extension(problem, "traceId");

    const diagnostics = [
      graphCode ? `Graph code ${graphCode}` : undefined,
      requestId ? `request id ${requestId}` : undefined,
      !requestId && traceId ? `trace id ${traceId}` : undefined,
    ].filter(Boolean);

    return diagnostics.length > 0 ? `${base} (${diagnostics.join(" · ")})` : base;
  }

  if (err instanceof Error && err.message) {
    return err.message;
  }

  const text = String(err ?? "");
  return text && text !== "[object Object]" ? text : fallback;
}

// ---------------------------------------------------------------------------
// Authorization prerequisites
// ---------------------------------------------------------------------------

/**
 * Stable codes the BFF uses to report an authorization prerequisite. SPE Admin has **two independent
 * authorization layers**, and telling them apart is the whole point — see
 * `SpeAdminAuthorizationFilter` for the full description.
 */
export const PERMISSION_CODES = {
  /** Not signed in / session expired. Nothing is known about this caller's permissions. */
  unauthenticated: "sdap.access.deny.unauthenticated",
  /** Layer 1 — signed in, but without the Spaarke admin app role. Granted by a Spaarke admin. */
  spaarkeAdmin: "sdap.access.deny.role_insufficient",
  /** Layer 2 — Microsoft Graph refused a container-type operation. Granted by an Entra admin. */
  entraDirectoryRole: "spe.containertypes.entra_role_required",
} as const;

/** How a screen should present an authorization prerequisite. */
export interface PermissionPrerequisite {
  /** Banner heading — states the nature of the problem, not a guess at its cause. */
  title: string;
  /** Fluent `MessageBar` intent. `warning` where the user can obtain access; `error` otherwise. */
  intent: "warning" | "error";
}

/**
 * Classifies a caught error as one of the authorization prerequisites the BFF reports.
 *
 * Screens use this to title the banner accurately. Without it every prerequisite renders under
 * "Failed to load container types", which reads as a malfunction and sends the admin looking for a
 * bug instead of a permission.
 *
 * The **body text always comes from {@link describeApiError}** — the BFF is the only party that knows
 * which layer denied the request and what grants it, so the client must not compose its own
 * explanation here. This function chooses a heading and nothing more.
 *
 * @returns The presentation, or `null` when the error is not an authorization prerequisite.
 *
 * Added by sdap-SPE-admin-app-r2 task 012 (spec FR-B03).
 */
export function describePermissionPrerequisite(err: unknown): PermissionPrerequisite | null {
  if (!(err instanceof ApiError)) return null;

  const problem = err.problemDetails as Record<string, unknown> | null;
  const code = extension(problem, "errorCode") ?? extension(problem, "reasonCode");

  switch (code) {
    case PERMISSION_CODES.entraDirectoryRole:
      // Graph refused. The role is the prerequisite — but the user may already hold it and be
      // blocked by something else, so this is a "warning", not a verdict.
      return { title: "Additional permission required", intent: "warning" };

    case PERMISSION_CODES.spaarkeAdmin:
      return { title: "Spaarke administrator permission required", intent: "warning" };

    case PERMISSION_CODES.unauthenticated:
      return { title: "Sign in to continue", intent: "warning" };

    default:
      return null;
  }
}

// ---------------------------------------------------------------------------
// Typed HTTP helpers
// ---------------------------------------------------------------------------

/**
 * GET request - returns parsed JSON body.
 * Throws ApiError for non-2xx responses.
 */
async function get<T>(url: string): Promise<T> {
  const response = await authenticatedFetch(url, { method: "GET" });
  return response.json() as Promise<T>;
}

/**
 * POST request - sends JSON body, returns parsed JSON body.
 * Throws ApiError for non-2xx responses.
 */
async function post<TBody, TResult>(url: string, body: TBody): Promise<TResult> {
  const response = await authenticatedFetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as TResult;
}

/**
 * POST with no request body - for action endpoints (activate, lock, unlock, refresh).
 * Throws ApiError for non-2xx responses.
 */
async function postAction<TResult>(url: string): Promise<TResult> {
  const response = await authenticatedFetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
  });
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as TResult;
}

/**
 * PUT request - sends JSON body, returns parsed JSON body.
 * Throws ApiError for non-2xx responses.
 */
async function put<TBody, TResult>(url: string, body: TBody): Promise<TResult> {
  const response = await authenticatedFetch(url, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as TResult;
}

/**
 * PATCH request - sends JSON body, returns parsed JSON body.
 * Throws ApiError for non-2xx responses.
 */
async function patch<TBody, TResult>(url: string, body: TBody): Promise<TResult> {
  const response = await authenticatedFetch(url, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as TResult;
}

/**
 * DELETE request - expects no response body on success.
 * Throws ApiError for non-2xx responses.
 */
async function del(url: string): Promise<void> {
  await authenticatedFetch(url, { method: "DELETE" });
}

/**
 * POST multipart/form-data for file uploads.
 * Does NOT set Content-Type header — browser sets it with the boundary string automatically.
 */
async function postFormData<TResult>(url: string, formData: FormData): Promise<TResult> {
  const response = await authenticatedFetch(url, {
    method: "POST",
    body: formData,
  });
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as TResult;
}

// ---------------------------------------------------------------------------
// Helper: build URL query string from a params object, omitting undefined/null
// ---------------------------------------------------------------------------

function qs(params: Record<string, string | number | boolean | undefined | null>): string {
  const parts: string[] = [];
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null) {
      parts.push(encodeURIComponent(key) + "=" + encodeURIComponent(String(value)));
    }
  }
  return parts.length > 0 ? "?" + parts.join("&") : "";
}

// ---------------------------------------------------------------------------
// speApiClient - one object containing all endpoint groups
// ---------------------------------------------------------------------------

/**
 * A container type exactly as the BFF sends it.
 *
 * The wire calls the identifier `id`; the client model calls it `containerTypeId`. Nothing mapped
 * between them until 2026-08-26, so `ct.containerTypeId` was `undefined` on every container type in
 * the app. Display survived (`displayName` and the billing fields happen to match), which is why the
 * list LOOKED correct — but anything keyed on the identifier silently failed. The Register wizard was
 * the visible casualty: every `<Option value={ct.containerTypeId}>` carried `undefined`, so the
 * dropdown could not resolve a selection and the operator "could not select a container type".
 *
 * Mapping in this layer keeps the wire name where it belongs and lets component code go on using the
 * domain name.
 */
interface WireContainerType {
  id: string;
  displayName: string;
  description?: string;
  billingClassification?: string;
  billingStatus?: string;
  createdDateTime?: string;
  owningAppId?: string;
  expiryDateTime?: string;
  settings?: unknown;
  /** Present only if a future server revision starts sending the domain name too. */
  containerTypeId?: string;
}

/**
 * Projects the wire shape onto the client model.
 *
 * `azureTenantId` and `isRegistered` are NOT set here: the endpoint does not send them. They stay
 * undefined so the UI can say "Unknown" — which is what the Registered column already does, and is
 * the honest answer, unlike defaulting to `false` ("this type is not registered") on the strength of
 * a field the server never sent.
 */
function mapContainerType(w: WireContainerType): ContainerType {
  return {
    ...(w as unknown as ContainerType),
    containerTypeId: w.containerTypeId ?? w.id,
  };
}

/**
 * The wire shape of a drive item, as `SpeContainerItemSummary` actually serialises it.
 *
 * 🔴 It is FLAT. `DriveItem` — the type every file-browser component consumes — is Graph-shaped and
 * NESTED. Nothing converted between them, and because the response was cast rather than parsed,
 * TypeScript reported nothing.
 *
 * The damage was total and silent, and it is the third instance of this exact defect in this
 * project (`id`→`containerTypeId`, the five `{items,count}` envelopes, now this):
 *
 *   - `isFolder` (flat) vs `folder` (nested) — `isFolder(item)` checks `!!item.folder`, so it was
 *     false for EVERY item. Every folder rendered as a File, with a File icon, sorted among the
 *     files, and — since only folders are links — could not be opened at all. That is the operator's
 *     "File Browser folders are not click-openable", and it is also why the `Communications` /
 *     `Emails` / `Exports` investigation stalled: the app could not open them because it did not
 *     believe they were folders.
 *   - `mimeType` (flat) vs `file.mimeType` (nested) — the Type column had nothing to report.
 *   - `createdByDisplayName` (flat) vs `lastModifiedBy.user.displayName` (nested) — "Modified By"
 *     rendered an em-dash on every row, which reads as "nobody" rather than "not mapped".
 *
 * Note the last one is not a pure rename: the server sends CREATED-by and the grid asks for
 * MODIFIED-by. They are different facts, so `createdBy` is populated here and `lastModifiedBy` is
 * deliberately left undefined rather than being filled with the wrong person's name.
 */
interface WireDriveItem {
  id: string;
  name: string;
  size?: number;
  createdDateTime?: string;
  lastModifiedDateTime?: string;
  createdByDisplayName?: string;
  isFolder?: boolean;
  mimeType?: string;
  webUrl?: string;
  /** Present only if a future server revision starts sending the nested Graph shape directly. */
  folder?: { childCount?: number };
  file?: { mimeType?: string };
}

/** Projects the flat wire item onto the nested `DriveItem` the components expect. */
function mapDriveItem(w: WireDriveItem): DriveItem {
  const isFolder = w.isFolder ?? w.folder !== undefined;

  return {
    id: w.id,
    name: w.name,
    size: w.size,
    createdDateTime: w.createdDateTime ?? "",
    lastModifiedDateTime: w.lastModifiedDateTime ?? "",
    webUrl: w.webUrl,
    // Exactly one of these is present, which is how Graph itself distinguishes the two and what
    // every consumer here tests against.
    folder: isFolder ? (w.folder ?? {}) : undefined,
    file: isFolder ? undefined : { mimeType: w.mimeType ?? w.file?.mimeType },
    createdBy: w.createdByDisplayName
      ? { user: { displayName: w.createdByDisplayName } }
      : undefined,
    // lastModifiedBy is NOT set — see the note above. The server does not send it.
  };
}

export const speApiClient = {
  // =========================================================================
  // Configuration - Business Units
  // =========================================================================

  businessUnits: {
    /**
     * GET /api/spe/businessunits
     * List all Dataverse Business Units available for scoping.
     */
    list(): Promise<BusinessUnit[]> {
      return get<BusinessUnit[]>("/spe/businessunits");
    },
  },

  // =========================================================================
  // Configuration - Environments (sprk_speenvironment)
  // =========================================================================

  environments: {
    /**
     * GET /api/spe/environments
     * List all SPE environment configurations.
     */
    list(): Promise<SpeEnvironment[]> {
      return get<SpeEnvironment[]>("/spe/environments");
    },

    /**
     * POST /api/spe/environments
     * Create a new SPE environment configuration.
     */
    create(body: SpeEnvironmentUpsert): Promise<SpeEnvironment> {
      return post<SpeEnvironmentUpsert, SpeEnvironment>("/spe/environments", body);
    },

    /**
     * PUT /api/spe/environments/{id}
     * Update an existing SPE environment configuration.
     */
    update(id: string, body: SpeEnvironmentUpsert): Promise<SpeEnvironment> {
      return put<SpeEnvironmentUpsert, SpeEnvironment>("/spe/environments/" + id, body);
    },

    /**
     * DELETE /api/spe/environments/{id}
     * Delete an SPE environment configuration.
     */
    delete(id: string): Promise<void> {
      return del("/spe/environments/" + id);
    },
  },

  // =========================================================================
  // Configuration - Container Type Configs (sprk_specontainertypeconfig)
  // =========================================================================

  configs: {
    /**
     * GET /api/spe/configs
     * List container type configs, optionally filtered by Business Unit or environment.
     */
    list(options?: { businessUnitId?: string; environmentId?: string }): Promise<SpeContainerTypeConfig[]> {
      const query = qs({
        businessUnitId: options?.businessUnitId,
        environmentId: options?.environmentId,
      });
      return get<SpeContainerTypeConfig[]>("/spe/configs" + query);
    },

    /**
     * GET /api/spe/configs/{id}
     * Get full detail for a single container type config.
     */
    get(id: string): Promise<SpeContainerTypeConfig> {
      return get<SpeContainerTypeConfig>("/spe/configs/" + id);
    },

    /**
     * POST /api/spe/configs
     * Create a new container type config.
     */
    create(body: SpeContainerTypeConfigUpsert): Promise<SpeContainerTypeConfig> {
      return post<SpeContainerTypeConfigUpsert, SpeContainerTypeConfig>("/spe/configs", body);
    },

    /**
     * PUT /api/spe/configs/{id}
     * Update an existing container type config.
     */
    update(id: string, body: SpeContainerTypeConfigUpsert): Promise<SpeContainerTypeConfig> {
      return put<SpeContainerTypeConfigUpsert, SpeContainerTypeConfig>("/spe/configs/" + id, body);
    },

    /**
     * DELETE /api/spe/configs/{id}
     * Delete a container type config.
     */
    delete(id: string): Promise<void> {
      return del("/spe/configs/" + id);
    },
  },

  // =========================================================================
  // Container Types (Graph API, proxied through BFF)
  // =========================================================================

  containerTypes: {
    /**
     * GET /api/spe/containertypes?configId={id}
     * List all container types for the given config.
     */
    list(configId: string): Promise<ContainerType[]> {
      return get<{ items: WireContainerType[]; count: number }>(
        "/spe/containertypes" + qs({ configId }),
      ).then((r) => r.items.map(mapContainerType));
    },

    /**
     * GET /api/spe/containertypes/{typeId}?configId={id}
     * Get details for a single container type.
     */
    get(typeId: string, configId: string): Promise<ContainerType> {
      return get<WireContainerType>(
        "/spe/containertypes/" + typeId + qs({ configId }),
      ).then(mapContainerType);
    },

    /**
     * POST /api/spe/containertypes?configId={id}
     * Create a new container type.
     */
    create(
      configId: string,
      body: { displayName: string; billingClassification: string },
    ): Promise<ContainerType> {
      return post<typeof body, WireContainerType>(
        "/spe/containertypes" + qs({ configId }),
        body,
      ).then(mapContainerType);
    },

    /**
     * PUT /api/spe/containertypes/{typeId}/settings?configId={id}
     * Update settings on an existing container type.
     */
    updateSettings(
      typeId: string,
      configId: string,
      body: Record<string, unknown>,
    ): Promise<ContainerType> {
      return put<Record<string, unknown>, WireContainerType>(
        "/spe/containertypes/" + typeId + "/settings" + qs({ configId }),
        body,
      ).then(mapContainerType);
    },

    /**
     * POST /api/spe/containertypes/{typeId}/register?configId={id}
     * Register the container type on the consuming tenant with the specified permissions.
     */
    register(
      typeId: string,
      configId: string,
      body: { delegatedPermissions: string[]; applicationPermissions: string[] },
    ): Promise<void> {
      return post<typeof body, void>(
        "/spe/containertypes/" + typeId + "/register" + qs({ configId }),
        body,
      );
    },

    /**
     * GET /api/spe/containertypes/{typeId}/permissions?configId={id}
     * List application permissions registered for a container type.
     */
    listPermissions(typeId: string, configId: string): Promise<ContainerTypePermission[]> {
      return get<{ items: ContainerTypePermission[]; count: number }>(
        "/spe/containertypes/" + typeId + "/permissions" + qs({ configId }),
      ).then(r => r.items);
    },

    /*
     * ── Container-type OWNERS (spec FR-C09, task 027) ──
     *
     * 🔑 `/owners`, NOT `/permissions`. `listPermissions` above returns which APPLICATIONS may access
     * containers of this type; these return which PEOPLE administer the type. Orthogonal surfaces
     * that happen to share a Graph word — task 027's own POML conflated them, and the separate route
     * is what keeps that mistake from being easy to repeat.
     *
     * Server-side these run delegated against Graph BETA: container types reject app-only auth (403),
     * and the `permissions` relationship does not exist on v1.0 (400 "Resource not found for the
     * segment 'permissions'"). No `configId` — the delegated path derives the tenant from the caller.
     */

    /** GET /api/spe/containertypes/{typeId}/owners — the people who administer this container type. */
    listOwners(typeId: string): Promise<ContainerTypeOwner[]> {
      return get<{ items: ContainerTypeOwner[] }>(
        "/spe/containertypes/" + encodeURIComponent(typeId) + "/owners",
      ).then(r => r.items ?? []);
    },

    /**
     * POST /api/spe/containertypes/{typeId}/owners — grant ownership.
     *
     * `userIdentifier` is an email/UPN or a directory object id, passed to Graph as given. An unknown
     * user surfaces Graph's own error rather than appearing to succeed.
     */
    addOwner(typeId: string, userIdentifier: string): Promise<ContainerTypeOwner> {
      return post<{ userIdentifier: string }, ContainerTypeOwner>(
        "/spe/containertypes/" + encodeURIComponent(typeId) + "/owners",
        { userIdentifier },
      );
    },

    /** DELETE /api/spe/containertypes/{typeId}/owners/{permissionId} — revoke an ownership grant. */
    removeOwner(typeId: string, permissionId: string): Promise<void> {
      return del(
        "/spe/containertypes/" + encodeURIComponent(typeId) +
        "/owners/" + encodeURIComponent(permissionId),
      );
    },

    /**
     * GET /api/spe/containertypes/{typeId}/consumers?configId={id}
     * List consuming application registrations for a container type (SPE-082).
     */
    listConsumers(typeId: string, configId: string): Promise<ConsumingTenantListResponse> {
      return get<ConsumingTenantListResponse>(
        "/spe/containertypes/" + typeId + "/consumers" + qs({ configId }),
      );
    },

    /**
     * POST /api/spe/containertypes/{typeId}/consumers?configId={id}
     * Register a new consuming application for a container type (SPE-082).
     */
    registerConsumer(
      typeId: string,
      configId: string,
      body: RegisterConsumingTenantRequest,
    ): Promise<ConsumingTenant> {
      return post<RegisterConsumingTenantRequest, ConsumingTenant>(
        "/spe/containertypes/" + typeId + "/consumers" + qs({ configId }),
        body,
      );
    },

    /**
     * PUT /api/spe/containertypes/{typeId}/consumers/{appId}?configId={id}
     * Update permissions for an existing consuming application registration (SPE-082).
     */
    updateConsumer(
      typeId: string,
      appId: string,
      configId: string,
      body: UpdateConsumingTenantRequest,
    ): Promise<ConsumingTenant> {
      return put<UpdateConsumingTenantRequest, ConsumingTenant>(
        "/spe/containertypes/" + typeId + "/consumers/" + encodeURIComponent(appId) + qs({ configId }),
        body,
      );
    },

    /**
     * DELETE /api/spe/containertypes/{typeId}/consumers/{appId}?configId={id}
     * Remove a consuming application registration from a container type (SPE-082).
     */
    removeConsumer(typeId: string, appId: string, configId: string): Promise<void> {
      return del(
        "/spe/containertypes/" + typeId + "/consumers/" + encodeURIComponent(appId) + qs({ configId }),
      );
    },
  },

  // =========================================================================
  // Containers (Graph API, proxied through BFF)
  // =========================================================================

  containers: {
    /**
     * GET /api/spe/containers?configId={id}
     * List all containers for the given config.
     */
    list(configId: string): Promise<Container[]> {
      return get<{ items: Container[]; count: number }>("/spe/containers" + qs({ configId }))
        .then(r => r.items);
    },

    /**
     * GET /api/spe/containers/{containerId}?configId={id}
     * Get a single container with full detail.
     */
    get(containerId: string, configId: string): Promise<Container> {
      return get<Container>("/spe/containers/" + containerId + qs({ configId }));
    },

    /**
     * POST /api/spe/containers?configId={id}
     * Create a new container.
     */
    create(
      configId: string,
      body: { displayName: string; description?: string },
    ): Promise<Container> {
      return post<typeof body, Container>("/spe/containers" + qs({ configId }), body);
    },

    /**
     * PATCH /api/spe/containers/{containerId}?configId={id}
     * Update container metadata (displayName, description).
     */
    update(
      containerId: string,
      configId: string,
      body: { displayName?: string; description?: string },
    ): Promise<Container> {
      return patch<typeof body, Container>(
        "/spe/containers/" + containerId + qs({ configId }),
        body,
      );
    },

    /**
     * POST /api/spe/containers/{containerId}/activate?configId={id}
     * Activate an inactive container.
     */
    activate(containerId: string, configId: string): Promise<Container> {
      return postAction<Container>(
        "/spe/containers/" + containerId + "/activate" + qs({ configId }),
      );
    },

    /**
     * POST /api/spe/containers/{containerId}/lock?configId={id}
     * Lock a container (read-only mode).
     */
    lock(containerId: string, configId: string): Promise<Container> {
      return postAction<Container>(
        "/spe/containers/" + containerId + "/lock" + qs({ configId }),
      );
    },

    /**
     * POST /api/spe/containers/{containerId}/unlock?configId={id}
     * Unlock a container (remove read-only restriction).
     */
    unlock(containerId: string, configId: string): Promise<Container> {
      return postAction<Container>(
        "/spe/containers/" + containerId + "/unlock" + qs({ configId }),
      );
    },

    /**
     * POST /api/spe/containers/{containerId}/archive?configId={id}
     * Archive a container (FR-E01) — up to 75% storage cost reduction.
     *
     * ⚠️ Returns **202 Accepted**, not 200. Graph performs archival asynchronously: the container
     * enters `recentlyArchived` and reaches `fullyArchived` later. Resolving does NOT mean the
     * container is archived — callers must not report completion, only acceptance.
     *
     * Returns `ArchivalActionAccepted`, not `Container`: the server has nothing newer to hand back
     * at this point, and returning a `Container` would imply the row was re-read post-change.
     *
     * Throws on 409 when the container TYPE has not opted into archival — an operator action, not a
     * caller-permission problem. The ProblemDetails carries a `remediation` field with the exact
     * PowerShell.
     */
    archive(
      containerId: string,
      configId: string,
    ): Promise<ArchivalActionAccepted> {
      return postAction<ArchivalActionAccepted>(
        "/spe/containers/" + containerId + "/archive" + qs({ configId }),
      );
    },

    /**
     * POST /api/spe/containers/{containerId}/unarchive?configId={id}
     * Return an archived container to active use (FR-E01).
     *
     * 🔑 NOT `recycleBin.restore` — that recovers a soft-DELETED container. This reverses ARCHIVAL
     * on a container that was never deleted. Graph models them as two distinct actions.
     *
     * ⚠️ Also asynchronous: the container enters `reactivating` and is not usable on resolve.
     */
    unarchive(
      containerId: string,
      configId: string,
    ): Promise<ArchivalActionAccepted> {
      return postAction<ArchivalActionAccepted>(
        "/spe/containers/" + containerId + "/unarchive" + qs({ configId }),
      );
    },

    /**
     * GET /api/spe/containers/{containerId}/customproperties?configId={id}
     * List custom properties on a container.
     */
    listCustomProperties(
      containerId: string,
      configId: string,
    ): Promise<Record<string, ContainerCustomProperty>> {
      return get<Record<string, ContainerCustomProperty>>(
        "/spe/containers/" + containerId + "/customproperties" + qs({ configId }),
      );
    },

    /**
     * PUT /api/spe/containers/{containerId}/customproperties?configId={id}
     * Set (replace) all custom properties on a container.
     */
    updateCustomProperties(
      containerId: string,
      configId: string,
      body: Record<string, ContainerCustomProperty>,
    ): Promise<Record<string, ContainerCustomProperty>> {
      return put<typeof body, Record<string, ContainerCustomProperty>>(
        "/spe/containers/" + containerId + "/customproperties" + qs({ configId }),
        body,
      );
    },
  },

  // =========================================================================
  // Container Permissions
  // =========================================================================

  permissions: {
    /**
     * GET /api/spe/containers/{containerId}/permissions?configId={id}
     * List all permission entries on a container.
     */
    async list(
      containerId: string,
      configId: string,
    ): Promise<ContainerPermission[]> {
      // Envelope: `{ items, count }` (ContainerPermissionListResponse), not a bare array. This one
      // had not surfaced in UAT yet only because nobody had opened Manage Permissions.
      const page = await get<{ items?: ContainerPermission[] }>(
        "/spe/containers/" + containerId + "/permissions" + qs({ configId }),
      );
      if (!page || !Array.isArray(page.items)) {
        throw new Error(
          "The permissions service returned an unrecognized response shape (expected an object " +
            "with an 'items' array). Permissions could not be read; this is NOT a report that the " +
            "container has none.",
        );
      }
      return page.items;
    },

    /**
     * POST /api/spe/containers/{containerId}/permissions?configId={id}
     * Add a new permission entry to a container.
     */
    add(
      containerId: string,
      configId: string,
      body: ContainerPermissionUpsert,
    ): Promise<ContainerPermission> {
      return post<ContainerPermissionUpsert, ContainerPermission>(
        "/spe/containers/" + containerId + "/permissions" + qs({ configId }),
        body,
      );
    },

    /**
     * PATCH /api/spe/containers/{containerId}/permissions/{permId}?configId={id}
     * Update the role on an existing permission entry.
     */
    update(
      containerId: string,
      permId: string,
      configId: string,
      body: Pick<ContainerPermissionUpsert, "role">,
    ): Promise<ContainerPermission> {
      return patch<typeof body, ContainerPermission>(
        "/spe/containers/" + containerId + "/permissions/" + permId + qs({ configId }),
        body,
      );
    },

    /**
     * DELETE /api/spe/containers/{containerId}/permissions/{permId}?configId={id}
     * Remove a permission entry from a container.
     */
    remove(containerId: string, permId: string, configId: string): Promise<void> {
      return del(
        "/spe/containers/" + containerId + "/permissions/" + permId + qs({ configId }),
      );
    },
  },

  // =========================================================================
  // Container Columns
  // =========================================================================

  columns: {
    /**
     * GET /api/spe/containers/{containerId}/columns?configId={id}
     * List column definitions on a container.
     */
    list(containerId: string, configId: string): Promise<ColumnDefinition[]> {
      return get<{ items: ColumnDefinition[]; count: number }>(
        "/spe/containers/" + containerId + "/columns" + qs({ configId }),
      ).then(r => r.items);
    },

    /**
     * POST /api/spe/containers/{containerId}/columns?configId={id}
     * Create a new column definition.
     */
    create(
      containerId: string,
      configId: string,
      body: ColumnDefinitionUpsert,
    ): Promise<ColumnDefinition> {
      return post<ColumnDefinitionUpsert, ColumnDefinition>(
        "/spe/containers/" + containerId + "/columns" + qs({ configId }),
        body,
      );
    },

    /**
     * PATCH /api/spe/containers/{containerId}/columns/{colId}?configId={id}
     * Update an existing column definition.
     */
    update(
      containerId: string,
      colId: string,
      configId: string,
      body: Partial<ColumnDefinitionUpsert>,
    ): Promise<ColumnDefinition> {
      return patch<typeof body, ColumnDefinition>(
        "/spe/containers/" + containerId + "/columns/" + colId + qs({ configId }),
        body,
      );
    },

    /**
     * DELETE /api/spe/containers/{containerId}/columns/{colId}?configId={id}
     * Delete a column definition from a container.
     */
    delete(containerId: string, colId: string, configId: string): Promise<void> {
      return del("/spe/containers/" + containerId + "/columns/" + colId + qs({ configId }));
    },
  },

  // =========================================================================
  // Drive Items (files and folders)
  // =========================================================================

  items: {
    /** @see mapDriveItem — the wire shape is FLAT; `DriveItem` is nested. */
    /**
     * GET /api/spe/containers/{containerId}/items?configId={id}&folderId={folderId}
     * List items (files and folders) in a container folder.
     * Omit folderId to list the root folder.
     */
    async list(
      containerId: string,
      configId: string,
      options?: { folderId?: string; top?: number; skip?: number },
    ): Promise<DriveItem[]> {
      const wire = await get<WireDriveItem[]>(
        "/spe/containers/" + containerId + "/items" + qs({
          configId,
          folderId: options?.folderId,
          top: options?.top,
          skip: options?.skip,
        }),
      );
      if (!Array.isArray(wire)) {
        throw new Error(
          "Unexpected shape from GET /spe/containers/{id}/items — expected an array.",
        );
      }
      return wire.map(mapDriveItem);
    },

    // `get(containerId, itemId, configId)` DELETED by task 092. It declared
    // GET /api/spe/containers/{id}/items/{itemId}, which the server has never served — the item
    // surface exposes /versions, /thumbnails, /content, /preview, POST /share, DELETE and upload, but
    // no single-item GET. It also had zero callers, so it was dead in both directions. Per this
    // project's standing disposition (task 083; precedent in 071 and 073) a dead path is DELETED
    // rather than converted — inventing a server route to satisfy an uncalled client method would be
    // building a feature to justify dead code. Callers needing item details take them from
    // `list(...)`, which is what every current caller already does.

    /**
     * POST /api/spe/containers/{containerId}/items/upload?configId={id}&folderId={folderId}
     * Upload a file to a container folder.
     * Caller must provide FormData with the file attached as the "file" field.
     */
    async upload(
      containerId: string,
      configId: string,
      formData: FormData,
      options?: { folderId?: string },
    ): Promise<DriveItem> {
      return mapDriveItem(
        await postFormData<WireDriveItem>(
          "/spe/containers/" + containerId + "/items/upload" + qs({
            configId,
            folderId: options?.folderId,
          }),
          formData,
        ),
      );
    },

    /**
     * GET /api/spe/containers/{containerId}/items/{itemId}/content?configId={id}
     * Download a file. Returns the raw Response so the caller can stream or create a blob URL.
     */
    download(containerId: string, itemId: string, configId: string): Promise<Response> {
      return authenticatedFetch(
        "/spe/containers/" + containerId + "/items/" + itemId + "/content" + qs({ configId }),
        { method: "GET" },
      );
    },

    /**
     * GET /api/spe/containers/{containerId}/items/{itemId}/preview?configId={id}
     * Get a preview URL for a file (e.g. for the Office Online viewer).
     */
    getPreviewUrl(containerId: string, itemId: string, configId: string): Promise<{ previewUrl: string }> {
      return get<{ previewUrl: string }>(
        "/spe/containers/" + containerId + "/items/" + itemId + "/preview" + qs({ configId }),
      );
    },

    /**
     * DELETE /api/spe/containers/{containerId}/items/{itemId}?configId={id}
     * Delete a drive item (file or folder).
     */
    delete(containerId: string, itemId: string, configId: string): Promise<void> {
      return del("/spe/containers/" + containerId + "/items/" + itemId + qs({ configId }));
    },

    /**
     * POST /api/spe/containers/{containerId}/folders?configId={id}&parentId={parentId}
     * Create a new folder inside a container.
     */
    async createFolder(
      containerId: string,
      configId: string,
      body: { name: string },
      options?: { parentId?: string },
    ): Promise<DriveItem> {
      return mapDriveItem(
        await post<typeof body, WireDriveItem>(
          "/spe/containers/" + containerId + "/folders" + qs({
            configId,
            parentId: options?.parentId,
          }),
          body,
        ),
      );
    },
  },

  // =========================================================================
  // File Metadata (versions, thumbnails, sharing links)
  // =========================================================================

  metadata: {
    /**
     * GET /api/spe/containers/{containerId}/items/{itemId}/versions?configId={id}
     * List all versions of a drive item.
     */
    listVersions(containerId: string, itemId: string, configId: string): Promise<DriveItemVersion[]> {
      return get<DriveItemVersion[]>(
        "/spe/containers/" + containerId + "/items/" + itemId + "/versions" + qs({ configId }),
      );
    },

    /**
     * GET /api/spe/containers/{containerId}/items/{itemId}/thumbnails?configId={id}
     * Get thumbnail URLs for a drive item.
     */
    getThumbnails(containerId: string, itemId: string, configId: string): Promise<Thumbnail[]> {
      return get<Thumbnail[]>(
        "/spe/containers/" + containerId + "/items/" + itemId + "/thumbnails" + qs({ configId }),
      );
    },

    /**
     * POST /api/spe/containers/{containerId}/items/{itemId}/share?configId={id}
     * Create a sharing link for a drive item.
     *
     * The path is `/share`, NOT `/sharing`. It was `/sharing` here from the start while the server has
     * always served `/share` (ContainerItemEndpoints.cs, WithName("CreateSharingLink")), so this call
     * 404'd for the life of the feature — and because FileDetailPanel catches the failure and renders
     * "Failed to create sharing link.", the UI gave no hint that the cause was a wrong URL. Fixed by
     * task 092; kept honest by SpeAdminClientRouteAgreementTests, which fails the build if any URL in
     * this file has no matching server route.
     */
    createSharingLink(
      containerId: string,
      itemId: string,
      configId: string,
      body: { type: SharingLinkType; scope: SharingLinkScope; expirationDateTime?: string },
    ): Promise<SharingLink> {
      return post<typeof body, SharingLink>(
        "/spe/containers/" + containerId + "/items/" + itemId + "/share" + qs({ configId }),
        body,
      );
    },
  },

  // =========================================================================
  // Search
  // =========================================================================

  /**
   * Both search calls answer with a paged envelope of FLAT DTOs, while the results grids consume a
   * NESTED shape (`{ container }` / `{ item }`). Two mismatches at once, and both were silent:
   * TypeScript believed the old `Promise<…[]>` annotation, so the page stored an object where it
   * expected an array and died on `.filter` — the "i.filter is not a function" seen in UAT
   * 2026-08-25. Adapting here keeps the grids untouched and puts the wire-to-view translation in the
   * one layer whose job it is.
   */
  search: {
    /**
     * POST /api/spe/search/containers?configId={id}
     * Search for containers matching a query.
     */
    async containers(
      configId: string,
      body: SearchRequest,
    ): Promise<ContainerSearchResult[]> {
      const page = await post<
        SearchRequest,
        {
          items?: Array<{
            id: string;
            displayName: string;
            description?: string;
            containerTypeId?: string;
          }>;
        }
      >("/spe/search/containers" + qs({ configId }), body);

      if (!page || !Array.isArray(page.items)) {
        throw new Error(
          "The container search service returned an unrecognized response shape (expected an " +
            "object with an 'items' array). No results could be read, which is not the same as " +
            "there being no matches.",
        );
      }

      // status / createdDateTime / storageUsedInBytes are deliberately left undefined — search does
      // not report them, and defaulting them here would put invented values on an admin screen.
      return page.items.map((c) => ({
        container: {
          id: c.id,
          displayName: c.displayName,
          description: c.description,
          containerTypeId: c.containerTypeId,
        },
      }));
    },

    /**
     * POST /api/spe/search/items?configId={id}
     * Search for drive items matching a query.
     */
    async items(
      configId: string,
      body: SearchRequest,
    ): Promise<DriveItemSearchResult[]> {
      const page = await post<
        SearchRequest,
        {
          items?: Array<{
            id: string;
            name: string;
            size?: number;
            lastModifiedDateTime?: string;
            containerId?: string;
            containerName?: string;
            webUrl?: string;
            mimeType?: string;
          }>;
        }
      >("/spe/search/items" + qs({ configId }), body);

      if (!page || !Array.isArray(page.items)) {
        throw new Error(
          "The item search service returned an unrecognized response shape (expected an object " +
            "with an 'items' array). No results could be read, which is not the same as there " +
            "being no matches.",
        );
      }

      return page.items.map((i) => ({
        item: {
          id: i.id,
          name: i.name,
          size: i.size,
          lastModifiedDateTime: i.lastModifiedDateTime,
          webUrl: i.webUrl,
          // A drive item is a FILE when search reported a mime type, and a folder otherwise. This is
          // the only signal the search projection carries, and the grid uses it to pick the icon.
          ...(i.mimeType ? { file: { mimeType: i.mimeType } } : {}),
        },
        containerId: i.containerId ?? "",
        containerName: i.containerName,
      }));
    },
  },

  // =========================================================================
  // Recycle Bin
  // =========================================================================

  recycleBin: {
    /**
     * GET /api/spe/recyclebin?configId={id}
     * List all deleted containers in the recycle bin.
     * Returns DeletedContainer items (id, displayName, deletedDateTime, containerTypeId).
     */
    list(configId: string): Promise<DeletedContainer[]> {
      return get<{ items: DeletedContainer[]; count: number }>("/spe/recyclebin" + qs({ configId }))
        .then(r => r.items);
    },

    /**
     * POST /api/spe/recyclebin/{containerId}/restore?configId={id}
     * Restore a deleted container from the recycle bin.
     */
    restore(containerId: string, configId: string): Promise<void> {
      return postAction<void>(
        "/spe/recyclebin/" + containerId + "/restore" + qs({ configId }),
      );
    },

    /**
     * DELETE /api/spe/recyclebin/{containerId}?configId={id}
     * Permanently delete a container from the recycle bin. This is irreversible.
     */
    permanentDelete(containerId: string, configId: string): Promise<void> {
      return del("/spe/recyclebin/" + containerId + qs({ configId }));
    },
  },

  // =========================================================================
  // Security
  // =========================================================================

  security: {
    /**
     * GET /api/spe/security/alerts?configId={id}
     * List security alerts for the tenant.
     */
    async listAlerts(configId: string): Promise<SecurityAlert[]> {
      // Envelope: `{ items, count }` (SecurityAlertsResponse), not a bare array.
      const page = await get<{ items?: SecurityAlert[] }>(
        "/spe/security/alerts" + qs({ configId }),
      );
      if (!page || !Array.isArray(page.items)) {
        throw new Error(
          "The security service returned an unrecognized response shape (expected an object with " +
            "an 'items' array). Alerts could not be read — do not read this as 'no alerts'.",
        );
      }
      return page.items;
    },

    /**
     * GET /api/spe/security/score?configId={id}
     * Get the current secure score for the tenant.
     */
    getScore(configId: string): Promise<SecureScore> {
      return get<SecureScore>("/spe/security/score" + qs({ configId }));
    },
  },

  // =========================================================================
  // Dashboard
  // =========================================================================

  dashboard: {
    /**
     * GET /api/spe/dashboard/metrics?configId={id}
     * Get cached dashboard metrics for the selected container type config.
     * Data is served from the BackgroundService cache (SpeDashboardSyncService).
     */
    getMetrics(configId: string): Promise<DashboardMetrics> {
      return get<DashboardMetrics>("/spe/dashboard/metrics" + qs({ configId }));
    },

    /**
     * POST /api/spe/dashboard/refresh?configId={id}
     * Trigger a manual cache refresh for dashboard metrics.
     * Returns the newly refreshed metrics.
     */
    refresh(configId: string): Promise<DashboardMetrics> {
      return postAction<DashboardMetrics>("/spe/dashboard/refresh" + qs({ configId }));
    },
  },

  // =========================================================================
  // Audit Log
  // =========================================================================

  audit: {
    /**
     * GET /api/spe/audit?configId={id}&from={date}&to={date}&category={cat}
     * Query the audit log with optional date/category filters.
     *
     * The endpoint answers with a paged ENVELOPE — `{ items, count, top, skip }` — not a bare array.
     * This call previously declared `Promise<AuditLogEntry[]>` and handed the envelope object straight
     * to the page, which stored it in an array-typed state and then called `.slice()` on it. That threw
     * `TypeError: entries.slice is not a function` during render, and with no error boundary above it
     * the whole app unmounted — the white screen reported in UAT on 2026-08-25. TypeScript could not
     * catch it: the declared return type was simply an assertion about JSON that nothing verified.
     */
    async query(options: {
      configId: string;
      from?: string;
      to?: string;
      category?: AuditCategory;
      top?: number;
      skip?: number;
    }): Promise<AuditLogEntry[]> {
      const page = await get<{ items?: AuditLogEntry[] }>(
        "/spe/audit" + qs({
          configId: options.configId,
          from: options.from,
          to: options.to,
          category: options.category,
          top: options.top,
          skip: options.skip,
        }),
      );

      // Verify the shape rather than trusting the type parameter. An unexpected body must surface as a
      // visible error, NOT as an empty array — "no audit entries" is a claim about the tenant's history
      // that this client is in no position to make just because it failed to understand the response.
      if (!page || !Array.isArray(page.items)) {
        throw new Error(
          "The audit log service returned an unrecognized response shape (expected an object with an " +
            "'items' array). The entries could not be read, and this is not the same as there being none.",
        );
      }

      return page.items;
    },
  },

  // =========================================================================
  // Bulk Operations (SPE-083)
  // =========================================================================

  bulk: {
    /**
     * POST /api/spe/bulk/delete
     * Enqueue a bulk soft-delete (recycle bin) operation for multiple containers.
     * Returns immediately with operation ID — poll status to track progress.
     */
    enqueuDelete(request: BulkDeleteRequest): Promise<BulkOperationAccepted> {
      return post<BulkDeleteRequest, BulkOperationAccepted>("/spe/bulk/delete", request);
    },

    /**
     * POST /api/spe/bulk/permissions
     * Enqueue a bulk permission assignment operation for multiple containers.
     * Returns immediately with operation ID — poll status to track progress.
     */
    enqueuePermissions(request: BulkPermissionsRequest): Promise<BulkOperationAccepted> {
      return post<BulkPermissionsRequest, BulkOperationAccepted>("/spe/bulk/permissions", request);
    },

    /**
     * GET /api/spe/bulk/{operationId}/status
     * Poll the progress of a bulk operation.
     * Continue polling until isFinished is true.
     */
    getStatus(operationId: string): Promise<BulkOperationStatus> {
      return get<BulkOperationStatus>(`/spe/bulk/${encodeURIComponent(operationId)}/status`);
    },
  },
};
