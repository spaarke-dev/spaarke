/**
 * Type definitions for SPE File Viewer PCF Control
 * Aligned with BFF API contracts and MSAL authentication
 */

/**
 * PCF Control state machine
 *
 * Tracks component lifecycle from initialization through ready/error states.
 * Used by index.ts to manage UI feedback before React component renders.
 *
 * Transitions:
 * - init() → Loading (immediately, within 200ms)
 * - Loading → Ready (after auth + token acquisition complete)
 * - Loading → Error (if init fails)
 * - Error → Loading (on retry)
 */
export enum FileViewerState {
    /** Initial state: authenticating, acquiring token */
    Loading = "loading",
    /** Ready state: React component can render */
    Ready = "ready",
    /** Error state: initialization failed */
    Error = "error"
}

/**
 * Checkout status information
 */
export interface CheckoutStatus {
    /** Whether the document is currently checked out */
    isCheckedOut: boolean;
    /** Information about who checked out the document */
    checkedOutBy?: {
        id: string;
        name: string;
        email?: string;
    };
    /** When the document was checked out (ISO 8601) */
    checkedOutAt?: string;
    /** Whether the current user is the one who checked out the document */
    isCurrentUser: boolean;
}

/**
 * Response from BFF /api/documents/{id}/preview-url or /api/documents/{id}/view-url endpoint
 */
export interface FilePreviewResponse {
    /**
     * SharePoint preview/view URL with embedded viewer
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
     * Checkout status of the document (optional, included in view-url response)
     */
    checkoutStatus?: CheckoutStatus;

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
 * Response from BFF /api/documents/{id}/open-links endpoint
 *
 * Returns URLs for opening the document in different modes:
 * - desktopUrl: Protocol URL for desktop Office apps (ms-word:, ms-excel:, ms-powerpoint:)
 * - webUrl: SharePoint web URL for browser access
 */
export interface OpenLinksResponse {
    /**
     * Desktop protocol URL for launching native Office app
     *
     * Format: ms-word:ofe|u|{encoded-webUrl}
     * Null if file type is not supported for desktop editing.
     *
     * Supported: .docx, .xlsx, .pptx (and legacy .doc, .xls, .ppt)
     */
    desktopUrl: string | null;

    /**
     * SharePoint web URL for the document
     *
     * Can be used for browser-based access or as fallback.
     */
    webUrl: string;

    /**
     * MIME type of the document
     *
     * Example: "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
     */
    mimeType: string;

    /**
     * Document file name
     *
     * Example: "Report.docx"
     */
    fileName: string;
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
 *
 * Preview-only mode (embedded editor removed per Task 013).
 * Desktop editing is handled via "Open in Desktop" button (Task 014).
 */
export interface FilePreviewState {
    /** SharePoint preview URL (embed.aspx with nb=true) */
    previewUrl: string | null;

    /** Loading state for API fetch */
    isLoading: boolean;

    /**
     * Loading state for iframe content
     *
     * True while waiting for iframe onload event.
     * Used to keep spinner visible until content actually displays.
     */
    isIframeLoading: boolean;

    /**
     * Loading state for Open in Desktop button
     *
     * True while calling /open-links API to get desktop URL.
     */
    isEditLoading: boolean;

    /**
     * Loading state for Open in Web button
     *
     * True while calling /open-links API to get web URL.
     */
    isWebLoading: boolean;

    /** Error message to display to user */
    error: string | null;

    /** Document metadata from BFF API */
    documentInfo: {
        name: string;
        fileExtension?: string;
        size?: number;
    } | null;

    /** Checkout status from BFF API */
    checkoutStatus?: CheckoutStatus;
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
    /**
     * Dark mode state from Power Platform context
     *
     * When true, the control should render with dark theme colors.
     * Detected via context.fluentDesignLanguage?.isDarkTheme
     */
    isDarkTheme: boolean;
    /**
     * Callback to refresh the preview
     *
     * Called when user clicks the Refresh button to reload the document.
     */
    onRefresh?: () => void;
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
