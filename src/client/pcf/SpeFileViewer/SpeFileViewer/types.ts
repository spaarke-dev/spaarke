/**
 * Type definitions for SPE File Viewer PCF Control
 * Aligned with BFF API contracts and MSAL authentication
 */

/**
 * Response from BFF /api/documents/{id}/preview-url endpoint
 */
export interface FilePreviewResponse {
    /**
     * SharePoint preview URL with embedded viewer
     * Valid for ~15 minutes, includes access_token parameter
     */
    previewUrl: string;

    /**
     * Document metadata from SharePoint
     */
    documentInfo: {
        /** Document display name */
        name: string;
        /** File extension (e.g., "pdf", "docx") */
        fileExtension?: string;
        /** File size in bytes */
        size?: number;
        /** Last modified timestamp */
        lastModified?: string;
    };

    /**
     * Correlation ID for distributed tracing
     * Should match the X-Correlation-Id sent in request
     */
    correlationId: string;
}

/**
 * Response from BFF API /api/documents/{id}/office endpoint
 *
 * This interface defines the structure returned when requesting
 * an Office Online editor URL for a document.
 */
export interface OfficeUrlResponse {
    /**
     * Office Online editor URL (webUrl from Microsoft Graph API)
     *
     * This URL opens the document in Office Online with full
     * editor capabilities (subject to user permissions).
     *
     * Example: "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?..."
     */
    officeUrl: string;

    /**
     * User's permissions on the file
     *
     * Indicates what level of access the authenticated user has.
     * Note: Office Online will ultimately enforce these permissions.
     */
    permissions: {
        /**
         * Can the user edit the file?
         *
         * If false, Office Online will load in read-only mode.
         */
        canEdit: boolean;

        /**
         * Can the user view the file?
         *
         * Should always be true if this endpoint returns successfully.
         */
        canView: boolean;

        /**
         * User's role on the file
         *
         * Possible values: 'owner' | 'editor' | 'reader' | 'unknown'
         */
        role: string;
    };

    /**
     * Correlation ID for distributed tracing
     *
     * Should match the X-Correlation-Id sent in the request.
     * Used for debugging and log correlation.
     */
    correlationId: string;
}

/**
 * Error response from BFF API (RFC 7807 Problem Details)
 *
 * Supports stable error codes in extensions for precise error handling.
 * Error codes as per senior dev spec:
 * - invalid_id: Document ID is not a valid GUID
 * - document_not_found: Document doesn't exist in Dataverse
 * - mapping_missing_drive: DriveId not populated or invalid
 * - mapping_missing_item: ItemId not populated or invalid
 * - storage_not_found: File deleted from SharePoint
 * - throttled_retry: Graph API throttling
 */
export interface BffErrorResponse {
    /** HTTP status code */
    status: number;
    /** Error title/summary */
    title: string;
    /** Detailed error message */
    detail?: string;
    /** Correlation ID for troubleshooting */
    correlationId?: string;
    /** Additional error details */
    errors?: Record<string, string[]>;
    /** Extensions for stable error codes */
    extensions?: {
        /** Stable error code for precise handling */
        code?: string;
        [key: string]: unknown;
    };
}

/**
 * Component state for FilePreview React component
 */
export interface FilePreviewState {
    /** SharePoint preview URL (embed.aspx with nb=true) */
    previewUrl: string | null;

    /** Office Online editor URL (webUrl from Graph API) */
    officeUrl: string | null;

    /** Loading state for async operations */
    isLoading: boolean;

    /** Error message to display to user */
    error: string | null;

    /** Document metadata from BFF API */
    documentInfo: {
        name: string;
        fileExtension?: string;
        size?: number;
    } | null;

    /**
     * Current display mode
     *
     * 'preview': Read-only preview mode (default)
     * 'editor': Office Online editor mode
     */
    mode: 'preview' | 'editor';

    /**
     * Whether to show read-only permission dialog
     *
     * Set to true when user opens editor but lacks edit permissions.
     */
    showReadOnlyDialog: boolean;
}

/**
 * Props for FilePreview React component
 */
export interface FilePreviewProps {
    /** Document GUID to preview */
    documentId: string;
    /** BFF base URL (e.g., "https://spe-api-dev-67e2xz.azurewebsites.net") */
    bffApiUrl: string;
    /** Access token from MSAL (Bearer token for BFF API) */
    accessToken: string;
    /** Correlation ID for request tracking */
    correlationId: string;
}

/**
 * MSAL configuration for BFF authentication
 */
export interface MsalConfig {
    /** Azure AD tenant ID */
    tenantId: string;
    /** PCF Client Application ID (for MSAL clientId) */
    clientAppId: string;
    /** BFF Application ID (for scope construction) */
    bffAppId: string;
    /**
     * Named scope for BFF access
     * Format: api://<BFF_APP_ID>/SDAP.Access
     */
    scope: string;
}

/**
 * PCF Control input properties (from manifest)
 */
export interface ControlInputs {
    /** Document ID (bound to form field) */
    documentId: ComponentFramework.PropertyTypes.StringProperty;
    /** BFF API base URL */
    bffApiUrl: ComponentFramework.PropertyTypes.StringProperty;
    /** PCF Client Application ID (for MSAL clientId) */
    clientAppId: ComponentFramework.PropertyTypes.StringProperty;
    /** BFF Application ID (for MSAL scope construction) */
    bffAppId: ComponentFramework.PropertyTypes.StringProperty;
    /** Azure AD Tenant ID */
    tenantId: ComponentFramework.PropertyTypes.StringProperty;
    /** Control minimum height in pixels (responsive - expands to fill available space) */
    controlHeight: ComponentFramework.PropertyTypes.WholeNumberProperty;
}

/**
 * PCF Control outputs (none for this control)
 */
// eslint-disable-next-line @typescript-eslint/no-empty-object-type
export interface ControlOutputs {
    // No outputs for this read-only preview control
}
