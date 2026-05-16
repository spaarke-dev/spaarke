/**
 * useEntityResolver — React hook for resolving entity context from URL parameters
 *
 * Resolves entity identity (entityType, entityId) from:
 *   1. URL search parameters (?matterId=, ?projectId=, ?documentId=) via navigateTo data envelope
 *   2. Raw URL params (Dataverse form pass-through: ?id=, ?typename=)
 *   3. Xrm frame-walk fallback (parent/top window Xrm form context)
 *
 * Priority chain per entity type:
 *   matterId:    data.matterId → raw "id" when typename=sprk_matter → Xrm form entity id
 *   projectId:   data.projectId → raw "id" when typename=sprk_project → Xrm form entity id
 *   documentId:  data.documentId → raw "id" when typename=sprk_document → Xrm form entity id
 *
 * Pattern: mirrors resolveAnalysisId/resolveDocumentId/resolveTenantId in
 *   src/client/code-pages/AnalysisWorkspace/src/index.tsx (lines 57-203)
 *   but generalised for arbitrary entity types.
 *
 * Standards: ADR-012 (abstracted interfaces), NOT PCF-safe (uses window.location)
 */

import { useState, useEffect } from 'react';
import type { EntityContext, EntityResolutionResult, EntityType } from '../types/entity-context';

// ---------------------------------------------------------------------------
// Xrm frame-walk helpers
// ---------------------------------------------------------------------------

/**
 * Build an ordered list of candidate frames to walk for Xrm context.
 * Returns [window, window.parent, window.top] with duplicates and
 * cross-origin failures removed.
 */
function buildFrameList(): Window[] {
  const frames: Window[] = [window];
  try {
    if (window.parent && window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window && window.top !== window.parent) {
      frames.push(window.top!);
    }
  } catch {
    /* cross-origin */
  }
  return frames;
}

/**
 * Walk the frame hierarchy and return the entity ID from the Xrm form context.
 * Returns an empty string if Xrm is unavailable in all frames.
 */
function resolveEntityIdFromXrm(): string {
  /* eslint-disable @typescript-eslint/no-explicit-any */
  for (const frame of buildFrameList()) {
    try {
      const xrm = (frame as any).Xrm;
      if (xrm?.Page?.data?.entity) {
        const id = xrm.Page.data.entity.getId();
        if (id) return id.replace(/[{}]/g, '').toLowerCase();
      }
    } catch {
      /* unavailable */
    }
  }
  /* eslint-enable @typescript-eslint/no-explicit-any */
  return '';
}

/**
 * Walk the frame hierarchy and return the logical name of the current form entity.
 * Used to disambiguate the Dataverse "id" param when no typename URL param is present.
 */
function resolveEntityTypeNameFromXrm(): string {
  /* eslint-disable @typescript-eslint/no-explicit-any */
  for (const frame of buildFrameList()) {
    try {
      const xrm = (frame as any).Xrm;
      if (xrm?.Page?.data?.entity) {
        const name = xrm.Page.data.entity.getEntityName?.();
        if (name) return name.toLowerCase();
      }
    } catch {
      /* unavailable */
    }
  }
  /* eslint-enable @typescript-eslint/no-explicit-any */
  return '';
}

// ---------------------------------------------------------------------------
// URL param resolution
// ---------------------------------------------------------------------------

/** Dataverse logical name → EntityType mapping */
const TYPENAME_TO_ENTITY_TYPE: Record<string, EntityType> = {
  sprk_matter: 'matter',
  sprk_project: 'project',
  sprk_document: 'document',
  account: 'account',
  contact: 'contact',
};

/**
 * Parse URL parameters from both the raw search string and the navigateTo
 * data envelope (?data=<urlencoded>).
 *
 * Returns [rawParams, appParams] where appParams prefers the decoded data envelope.
 */
function parseUrlParams(): [URLSearchParams, URLSearchParams] {
  const rawParams = new URLSearchParams(window.location.search);
  const dataEnvelope = rawParams.get('data');
  const appParams = dataEnvelope
    ? new URLSearchParams(decodeURIComponent(dataEnvelope))
    : rawParams;
  return [rawParams, appParams];
}

/**
 * Resolve entity context from URL parameters and Xrm fallback.
 *
 * Resolution priority:
 *   1. Explicit typed params in data envelope: ?matterId=, ?projectId=, ?documentId=
 *   2. Dataverse form pass-through: ?id=&typename=sprk_matter|sprk_project|...
 *   3. Xrm frame-walk: form entity id + entity logical name
 *
 * Returns null when no entity can be resolved.
 */
function resolveEntityContextSync(): EntityContext | null {
  const [rawParams, appParams] = parseUrlParams();

  // ── Priority 1: Explicit typed URL params ──────────────────────────────
  const matterId = appParams.get('matterId');
  if (matterId) {
    return {
      entityType: 'matter',
      entityId: matterId,
      matterId,
    };
  }

  const projectId = appParams.get('projectId');
  if (projectId) {
    return {
      entityType: 'project',
      entityId: projectId,
      projectId,
    };
  }

  const documentId = appParams.get('documentId');
  if (documentId) {
    return {
      entityType: 'document',
      entityId: documentId,
      documentId,
    };
  }

  // ── Priority 2: Dataverse form pass-through (?id=&typename=) ──────────
  const dvId = rawParams.get('id');
  const typename = rawParams.get('typename')?.toLowerCase();
  if (dvId && typename) {
    const cleanId = dvId.replace(/[{}]/g, '').toLowerCase();
    const entityType: EntityType = TYPENAME_TO_ENTITY_TYPE[typename] ?? 'unknown';
    return buildEntityContext(entityType, cleanId);
  }

  // ── Priority 3: Xrm frame-walk ─────────────────────────────────────────
  const xrmId = resolveEntityIdFromXrm();
  if (xrmId) {
    const xrmTypename = resolveEntityTypeNameFromXrm();
    const entityType: EntityType = TYPENAME_TO_ENTITY_TYPE[xrmTypename] ?? 'unknown';
    return buildEntityContext(entityType, xrmId);
  }

  return null;
}

/**
 * Build an EntityContext from an entity type and ID, populating the typed
 * accessor fields (matterId, projectId, documentId) based on the entity type.
 */
function buildEntityContext(entityType: EntityType, entityId: string): EntityContext {
  return {
    entityType,
    entityId,
    matterId: entityType === 'matter' ? entityId : undefined,
    projectId: entityType === 'project' ? entityId : undefined,
    documentId: entityType === 'document' ? entityId : undefined,
  };
}

// ---------------------------------------------------------------------------
// useEntityResolver hook
// ---------------------------------------------------------------------------

/**
 * useEntityResolver — resolves entity context from URL params + Xrm frame-walk.
 *
 * Resolution is synchronous (URL params are available immediately), so
 * isResolving will briefly be true only while the hook mounts. For Xrm-based
 * resolution the frame-walk may take one tick on mount.
 *
 * @returns EntityResolutionResult with entityContext, isResolving, and error
 *
 * @example
 * const { entityContext, isResolving } = useEntityResolver();
 * if (entityContext?.entityType === 'matter') {
 *   console.log('Working on matter:', entityContext.matterId);
 * }
 */
export function useEntityResolver(): EntityResolutionResult {
  const [result, setResult] = useState<EntityResolutionResult>({
    entityContext: null,
    isResolving: true,
    error: null,
  });

  useEffect(() => {
    try {
      const entityContext = resolveEntityContextSync();
      if (entityContext) {
        setResult({ entityContext, isResolving: false, error: null });
      } else {
        // No entity resolved — standalone chat mode (no entity context)
        setResult({ entityContext: null, isResolving: false, error: null });
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      console.warn('[useEntityResolver] Resolution failed:', message);
      setResult({ entityContext: null, isResolving: false, error: message });
    }
  }, []); // run once on mount — URL params don't change

  return result;
}
