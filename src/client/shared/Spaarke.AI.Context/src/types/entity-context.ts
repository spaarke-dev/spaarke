/**
 * @spaarke/ai-context — EntityContext type
 *
 * Describes the host entity context for an AI surface: which entity record the
 * user is currently working on. Drives entity-scoped search, playbook discovery,
 * and context mapping via GET /api/ai/chat/context-mappings/standalone.
 *
 * Standards: ADR-012 (shared library — abstracted interfaces, not platform APIs)
 */

// ---------------------------------------------------------------------------
// Entity Type Enum
// ---------------------------------------------------------------------------

/**
 * Discriminated union of supported entity types.
 * Add new types here as the platform expands beyond legal/project use cases.
 */
export type EntityType = 'matter' | 'project' | 'document' | 'account' | 'contact' | 'unknown';

// ---------------------------------------------------------------------------
// EntityContext
// ---------------------------------------------------------------------------

/**
 * The host entity context describing WHERE an AI surface is embedded.
 * Enables entity-scoped search, playbook discovery, and context mapping
 * without coupling AI components to any specific workspace.
 *
 * Resolved from URL parameters by useEntityResolver (standalone mode) or
 * from the Xrm form context by the host workspace.
 *
 * @see IHostContext in @spaarke/ui-components SprkChat/types.ts
 * @see useEntityResolver — resolves this from URL params + Xrm frame walk
 */
export interface EntityContext {
  /** Discriminated entity type (e.g., "matter", "project", "document") */
  entityType: EntityType;
  /** GUID of the parent entity record */
  entityId: string;
  /** Display name of the parent entity (for logging/UI label) */
  entityName?: string;
  /** Workspace hosting the AI surface (e.g., "LegalWorkspace", "SpaarkeAi") */
  workspaceType?: string;
  /** Page type where the AI surface is embedded (e.g., "form", "workspace", "dashboard") */
  pageType?: string;

  // ── Typed accessors for common entity ID fields ──────────────────────────
  // These mirror the URL params resolved by useEntityResolver.
  // When entityType is "matter", matterId === entityId (and projectId/documentId are undefined).

  /** Matter GUID (populated when entityType === "matter") */
  matterId?: string;
  /** Project GUID (populated when entityType === "project") */
  projectId?: string;
  /** Document GUID (populated when entityType === "document") */
  documentId?: string;
}

// ---------------------------------------------------------------------------
// EntityResolutionResult
// ---------------------------------------------------------------------------

/**
 * Result returned by useEntityResolver.
 * Wraps EntityContext with loading/error state for async resolution scenarios.
 */
export interface EntityResolutionResult {
  /** Resolved entity context (null while loading or when resolution failed) */
  entityContext: EntityContext | null;
  /** Whether resolution is in progress */
  isResolving: boolean;
  /** Resolution error message (null on success) */
  error: string | null;
}
