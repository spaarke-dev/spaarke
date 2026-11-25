/**
 * Visualization API Service — fetches document relationship data from the BFF
 * (GET /api/ai/visualization/related/{documentId}) and maps it to graph nodes and edges.
 *
 * Shared by the DocumentRelationshipViewer code page and PCF (#3093). No React dependency.
 *
 * Auth: `authenticatedFetch` from `@spaarke/auth` (ADR-028). No token strings are accepted;
 * the fetch wrapper adds the Bearer header at request time and retries on 401.
 */

import { authenticatedFetch, isApiError, isAuthFailure, problemOf } from '@spaarke/auth';
import { getRelationshipLabel } from './types';
import type {
  ApiDocumentEdge,
  ApiDocumentNode,
  DocumentGraphEdge,
  DocumentGraphNode,
  DocumentGraphResponse,
  DocumentNodeData,
  DocumentEdgeData,
  DocumentRelationship,
  GraphMetadata,
  VisualizationQueryParams,
} from './types';

/** Result of {@link VisualizationApiService.getRelatedDocuments}. */
export interface DocumentGraphData {
  nodes: DocumentGraphNode[];
  edges: DocumentGraphEdge[];
  metadata: GraphMetadata;
}

export class VisualizationApiService {
  private apiBaseUrl: string;

  constructor(apiBaseUrl: string) {
    this.apiBaseUrl = apiBaseUrl.replace(/\/$/, '');
  }

  /** Fetch related documents for a source document and map them to graph nodes and edges. */
  async getRelatedDocuments(documentId: string, params: VisualizationQueryParams): Promise<DocumentGraphData> {
    const url = this.buildUrl(documentId, params);

    let response: Response;
    try {
      // Bearer auth only: the BFF reads no cookie, so no `credentials: 'include'`.
      response = await authenticatedFetch(url, {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      });
    } catch (err) {
      // authenticatedFetch THROWS for a non-OK response — the `!response.ok` branch below never runs
      // under it. Build the same VisualizationApiError so 404 (empty graph) and 401/403 (permission
      // text) keep their handling.
      throw toVisualizationApiError(err);
    }

    if (!response.ok) {
      const errorBody = await this.parseErrorResponse(response);
      throw new VisualizationApiError(errorBody.detail ?? `API error: ${response.status}`, response.status, errorBody);
    }

    const apiResponse = (await response.json()) as DocumentGraphResponse;
    return mapToGraphData(apiResponse);
  }

  private buildUrl(documentId: string, params: VisualizationQueryParams): string {
    const url = new URL(`${this.apiBaseUrl}/api/ai/visualization/related/${documentId}`);
    // No tenantId param: the BFF ignores it and resolves the tenant from the token's `tid` (#1453).
    if (params.threshold !== undefined) url.searchParams.set('threshold', params.threshold.toString());
    if (params.limit !== undefined) url.searchParams.set('limit', params.limit.toString());
    if (params.depth !== undefined) url.searchParams.set('depth', params.depth.toString());
    if (params.includeKeywords !== undefined)
      url.searchParams.set('includeKeywords', params.includeKeywords.toString());
    if (params.includeParentEntity !== undefined)
      url.searchParams.set('includeParentEntity', params.includeParentEntity.toString());
    if (params.documentTypes && params.documentTypes.length > 0) {
      params.documentTypes.forEach(type => url.searchParams.append('documentTypes', type));
    }
    if (params.relationshipTypes && params.relationshipTypes.length > 0) {
      params.relationshipTypes.forEach(type => url.searchParams.append('relationshipTypes', type));
    }
    return url.toString();
  }

  private async parseErrorResponse(response: Response): Promise<{ title?: string; detail?: string; status?: number }> {
    try {
      return (await response.json()) as { title?: string; detail?: string; status?: number };
    } catch {
      return {
        title: 'Unknown Error',
        detail: `HTTP ${response.status}: ${response.statusText}`,
        status: response.status,
      };
    }
  }
}

/**
 * Map a BFF graph response to graph nodes and edges. Every relationship type touching a node is
 * kept on `relationshipTypes`; the primary one (`relationshipType` / `relationshipLabel` /
 * `similarity`) is "semantic" when the node has it, otherwise its first relationship.
 *
 * Exported for tests.
 */
export function mapToGraphData(apiResponse: DocumentGraphResponse): DocumentGraphData {
  const edges = apiResponse.edges.map(mapApiEdge);
  const nodeRelationships = buildNodeRelationshipMap(apiResponse.edges);
  const nodes = apiResponse.nodes.map(apiNode => mapApiNode(apiNode, nodeRelationships.get(apiNode.id)));
  return { nodes, edges, metadata: apiResponse.metadata };
}

/**
 * Node id → each distinct relationship type on the edges touching it. Both ends of an edge get it,
 * because edge direction varies by relationship type (semantic edges run from the source outward,
 * same_matter edges run from a document to its parent hub).
 */
function buildNodeRelationshipMap(edges: ApiDocumentEdge[]): Map<string, DocumentRelationship[]> {
  const nodeRelationships = new Map<string, DocumentRelationship[]>();

  for (const edge of edges) {
    const relType = edge.data.relationshipType;
    const entry: DocumentRelationship = {
      type: relType,
      label: getRelationshipLabel(relType, edge.data.relationshipLabel),
      similarity: edge.data.similarity,
    };

    for (const nodeId of [edge.source, edge.target]) {
      const existing = nodeRelationships.get(nodeId);
      if (existing) {
        if (!existing.some(r => r.type === relType)) {
          existing.push(entry);
        }
      } else {
        nodeRelationships.set(nodeId, [entry]);
      }
    }
  }

  return nodeRelationships;
}

function mapApiNode(apiNode: ApiDocumentNode, relationships?: DocumentRelationship[]): DocumentGraphNode {
  const isOrphanFile = apiNode.data.isOrphanFile ?? apiNode.type === 'orphan';
  const isParentHub =
    apiNode.type === 'matter' || apiNode.type === 'project' || apiNode.type === 'invoice' || apiNode.type === 'email';
  // Hub nodes use their document type (Matter, Project, ...) as the "file type" for the icon.
  // The BFF sends documentType as a non-null string that defaults to "".
  const hubType = apiNode.data.documentType.toLowerCase();
  const fileType = isParentHub
    ? hubType !== ''
      ? hubType
      : apiNode.type
    : (apiNode.data.fileType ?? extractFileType(apiNode.data.label, apiNode.data.documentType));

  // Prefer semantic as the primary relationship for this viewer
  const primary = relationships?.find(r => r.type === 'semantic') ?? relationships?.[0];

  const data: DocumentNodeData = {
    // Orphan files have no Dataverse record (use speFileId); hub nodes are not documents.
    documentId: isOrphanFile || isParentHub ? undefined : apiNode.id,
    speFileId: apiNode.data.speFileId,
    name: apiNode.data.label,
    fileType,
    documentType: apiNode.data.documentType,
    similarity: primary?.similarity ?? apiNode.data.similarity,
    isSource: apiNode.type === 'source',
    isOrphanFile,
    nodeType: apiNode.type,
    relationshipType: primary?.type,
    relationshipLabel: primary?.label,
    relationshipTypes: relationships,
    parentEntityName: apiNode.data.parentEntityName,
    fileUrl: apiNode.data.fileUrl,
    recordUrl: apiNode.data.recordUrl,
    sharedKeywords: apiNode.data.extractedKeywords,
    createdOn: apiNode.data.createdOn,
    modifiedOn: apiNode.data.modifiedOn,
  };

  return {
    id: apiNode.id,
    type: 'document',
    position: apiNode.position ?? { x: 0, y: 0 },
    data,
  };
}

function mapApiEdge(apiEdge: ApiDocumentEdge): DocumentGraphEdge {
  const data: DocumentEdgeData = {
    similarity: apiEdge.data.similarity,
    sharedKeywords: apiEdge.data.sharedKeywords,
    relationshipType: apiEdge.data.relationshipType,
    relationshipLabel: getRelationshipLabel(apiEdge.data.relationshipType, apiEdge.data.relationshipLabel),
  };

  return {
    id: apiEdge.id,
    source: apiEdge.source,
    target: apiEdge.target,
    data,
  };
}

/** File extension from the document name, else a guess from the document type. */
function extractFileType(documentName: string, documentType: string): string {
  const extensionMatch = /\.([a-zA-Z0-9]+)$/.exec(documentName);
  if (extensionMatch) return extensionMatch[1].toLowerCase();

  const typeMapping: Record<string, string> = {
    Contract: 'pdf',
    Invoice: 'pdf',
    Agreement: 'pdf',
    Report: 'pdf',
    Spreadsheet: 'xlsx',
    Presentation: 'pptx',
    Document: 'docx',
    Email: 'msg',
    Image: 'png',
  };
  return typeMapping[documentType] || 'pdf';
}

/** Error from the visualization API, with the HTTP status. */
export class VisualizationApiError extends Error {
  constructor(
    message: string,
    public readonly statusCode: number,
    public readonly errorBody?: {
      title?: string;
      detail?: string;
      status?: number;
    }
  ) {
    super(message);
    this.name = 'VisualizationApiError';
  }

  isNotFound(): boolean {
    return this.statusCode === 404;
  }

  isUnauthorized(): boolean {
    return this.statusCode === 401 || this.statusCode === 403;
  }
}

/**
 * The {@link VisualizationApiError} for an error `authenticatedFetch` threw: its `ApiError` (status +
 * ProblemDetails, same message rule as a returned response), or 401 for an `AuthError` (retries spent).
 * Anything else (a network failure) is returned unchanged.
 */
function toVisualizationApiError(err: unknown): unknown {
  if (isApiError(err)) {
    const problem = problemOf(err);
    return new VisualizationApiError(problem?.detail ?? `API error: ${err.status}`, err.status, problem ?? undefined);
  }
  if (isAuthFailure(err)) {
    return new VisualizationApiError(err instanceof Error ? err.message : 'Authentication failed', 401);
  }
  return err;
}
