/**
 * Common types for the Secure Project Workspace SPA.
 *
 * Access level values match Dataverse option set values on sprk_externalrecordaccess.
 */

/**
 * Access level for an external user on a secure project.
 * Values match the Dataverse option set (sprk_accesslevel).
 */
export enum AccessLevel {
  /** Read-only access: can view documents, events, contacts. Cannot upload, edit, trigger AI, or download. */
  ViewOnly = 100000000,
  /** Collaboration access: can upload documents, comment, trigger AI summaries. Cannot manage access. */
  Collaborate = 100000001,
  /** Full access: all Collaborate permissions plus can invite other external users. */
  FullAccess = 100000002,
}

/** Portal user context populated from the Power Pages portal shell */
export interface PortalUser {
  /** The authenticated user's email address (username in portal context) */
  userName: string;
  /** First name from the linked Contact record */
  firstName: string;
  /** Last name from the linked Contact record */
  lastName: string;
  /** Display name (first + last) */
  displayName: string;
  /** Entra External ID tenant identifier */
  tenantId?: string;
}

/**
 * External user context returned by the BFF API's /external/context endpoint.
 * Combines portal identity with the user's access level for a specific project.
 */
export interface ExternalUserContext {
  /** The Contact record ID in Dataverse */
  contactId: string;
  /** The authenticated user's email */
  email: string;
  /** Display name */
  displayName: string;
  /** The Project record ID the user has access to */
  projectId: string;
  /** The user's access level for this project */
  accessLevel: AccessLevel;
  /** Whether the user's access has been revoked */
  isRevoked: boolean;
}

/** A Secure Project record as returned from the Power Pages Web API or BFF API */
export interface Project {
  /** Dataverse record GUID */
  sprk_projectid: string;
  /** Project reference number */
  sprk_referencenumber: string;
  /** Project display name */
  sprk_name: string;
  /** Project description */
  sprk_description?: string;
  /** Whether this project is a secure project (external access enabled) */
  sprk_issecure: boolean;
  /** Project status (active, closed, etc.) */
  sprk_status?: number;
  /** ISO date string when project was created */
  createdon?: string;
  /** ISO date string when project was last modified */
  modifiedon?: string;
}

/** A document record as returned from the BFF API */
export interface Document {
  /** Dataverse record GUID */
  sprk_documentid: string;
  /** Document display name */
  sprk_name: string;
  /** Document type (e.g., "brief", "exhibit", "order") */
  sprk_documenttype?: string;
  /** AI-generated summary */
  sprk_summary?: string;
  /** SPE file URL (for BFF-mediated download) */
  fileUrl?: string;
  /** File size in bytes */
  fileSizeBytes?: number;
  /** ISO date string */
  createdon?: string;
  /** Creator display name */
  createdByName?: string;
}

/** API error with status code and message */
export class ApiError extends Error {
  constructor(
    public readonly statusCode: number,
    message: string
  ) {
    super(message);
    this.name = "ApiError";
  }
}
