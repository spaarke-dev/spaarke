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
  Container,
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
      return get<{ items: ContainerType[]; count: number }>("/spe/containertypes" + qs({ configId }))
        .then(r => r.items);
    },

    /**
     * GET /api/spe/containertypes/{typeId}?configId={id}
     * Get details for a single container type.
     */
    get(typeId: string, configId: string): Promise<ContainerType> {
      return get<ContainerType>("/spe/containertypes/" + typeId + qs({ configId }));
    },

    /**
     * POST /api/spe/containertypes?configId={id}
     * Create a new container type.
     */
    create(
      configId: string,
      body: { displayName: string; billingClassification: string },
    ): Promise<ContainerType> {
      return post<typeof body, ContainerType>("/spe/containertypes" + qs({ configId }), body);
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
      return put<Record<string, unknown>, ContainerType>(
        "/spe/containertypes/" + typeId + "/settings" + qs({ configId }),
        body,
      );
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
    list(containerId: string, configId: string): Promise<ContainerPermission[]> {
      return get<ContainerPermission[]>(
        "/spe/containers/" + containerId + "/permissions" + qs({ configId }),
      );
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
    /**
     * GET /api/spe/containers/{containerId}/items?configId={id}&folderId={folderId}
     * List items (files and folders) in a container folder.
     * Omit folderId to list the root folder.
     */
    list(
      containerId: string,
      configId: string,
      options?: { folderId?: string; top?: number; skip?: number },
    ): Promise<DriveItem[]> {
      return get<DriveItem[]>(
        "/spe/containers/" + containerId + "/items" + qs({
          configId,
          folderId: options?.folderId,
          top: options?.top,
          skip: options?.skip,
        }),
      );
    },

    /**
     * GET /api/spe/containers/{containerId}/items/{itemId}?configId={id}
     * Get details for a single drive item.
     */
    get(containerId: string, itemId: string, configId: string): Promise<DriveItem> {
      return get<DriveItem>(
        "/spe/containers/" + containerId + "/items/" + itemId + qs({ configId }),
      );
    },

    /**
     * POST /api/spe/containers/{containerId}/items/upload?configId={id}&folderId={folderId}
     * Upload a file to a container folder.
     * Caller must provide FormData with the file attached as the "file" field.
     */
    upload(
      containerId: string,
      configId: string,
      formData: FormData,
      options?: { folderId?: string },
    ): Promise<DriveItem> {
      return postFormData<DriveItem>(
        "/spe/containers/" + containerId + "/items/upload" + qs({
          configId,
          folderId: options?.folderId,
        }),
        formData,
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
    createFolder(
      containerId: string,
      configId: string,
      body: { name: string },
      options?: { parentId?: string },
    ): Promise<DriveItem> {
      return post<typeof body, DriveItem>(
        "/spe/containers/" + containerId + "/folders" + qs({
          configId,
          parentId: options?.parentId,
        }),
        body,
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
     * POST /api/spe/containers/{containerId}/items/{itemId}/sharing?configId={id}
     * Create a sharing link for a drive item.
     */
    createSharingLink(
      containerId: string,
      itemId: string,
      configId: string,
      body: { type: SharingLinkType; scope: SharingLinkScope; expirationDateTime?: string },
    ): Promise<SharingLink> {
      return post<typeof body, SharingLink>(
        "/spe/containers/" + containerId + "/items/" + itemId + "/sharing" + qs({ configId }),
        body,
      );
    },
  },

  // =========================================================================
  // Search
  // =========================================================================

  search: {
    /**
     * POST /api/spe/search/containers?configId={id}
     * Search for containers matching a query.
     */
    containers(configId: string, body: SearchRequest): Promise<ContainerSearchResult[]> {
      return post<SearchRequest, ContainerSearchResult[]>(
        "/spe/search/containers" + qs({ configId }),
        body,
      );
    },

    /**
     * POST /api/spe/search/items?configId={id}
     * Search for drive items matching a query.
     */
    items(configId: string, body: SearchRequest): Promise<DriveItemSearchResult[]> {
      return post<SearchRequest, DriveItemSearchResult[]>(
        "/spe/search/items" + qs({ configId }),
        body,
      );
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
    listAlerts(configId: string): Promise<SecurityAlert[]> {
      return get<SecurityAlert[]>("/spe/security/alerts" + qs({ configId }));
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
     */
    query(options: {
      configId: string;
      from?: string;
      to?: string;
      category?: AuditCategory;
      top?: number;
      skip?: number;
    }): Promise<AuditLogEntry[]> {
      return get<AuditLogEntry[]>(
        "/spe/audit" + qs({
          configId: options.configId,
          from: options.from,
          to: options.to,
          category: options.category,
          top: options.top,
          skip: options.skip,
        }),
      );
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
