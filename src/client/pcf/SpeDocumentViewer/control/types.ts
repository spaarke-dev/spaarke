/**
 * Type definitions for SPE Document Viewer PCF Control
 * Supports check-out/check-in workflow with Office Online editing
 */

/**
 * PCF Control state machine
 */
export enum DocumentViewerState {
    /** Initial state: authenticating, acquiring token */
    Loading = "loading",
    /** Ready state: React component can render */
    Ready = "ready",
    /** Error state: initialization failed */
    Error = "error"
}

/**
 * Document view mode
 */
export enum ViewMode {
    /** Read-only preview using embed.aspx */
    Preview = "preview",
    /** Full editing using Office Online embedview */
    Edit = "edit",
    /** Processing (check-in in progress) */
    Processing = "processing"
}

/**
 * User info for checkout operations
 */
export interface CheckoutUserInfo {
    /** User's Dataverse system user ID */
    id: string;
    /** User's display name */
    name: string;
    /** User's email address */
    email: string | null;
}

/**
 * Checkout status from BFF API
 */
export interface CheckoutStatus {
    /** Whether the document is currently checked out */
    isCheckedOut: boolean;
    /** User who has the document checked out (null if not checked out) */
    checkedOutBy: CheckoutUserInfo | null;
    /** When the document was checked out (ISO 8601) */
    checkedOutAt: string | null;
    /** Whether the current user is the one who has it checked out */
    isCurrentUser: boolean;
}

/**
 * Document metadata info
 */
export interface DocumentInfo {
    /** Document file name */
    name: string;
    /** File extension (e.g., ".docx") */
    fileExtension?: string;
    /** File size in bytes */
    size?: number;
    /** Last modified date (ISO 8601) */
    lastModified?: string;
}

/**
 * Response from BFF /api/documents/{id}/preview-url endpoint
 */
export interface FilePreviewResponse {
    /** SharePoint preview URL with embedded viewer */
    previewUrl: string;
    /** Document metadata */
    documentInfo?: DocumentInfo;
    /** Checkout status (if available) */
    checkoutStatus?: CheckoutStatus | null;
    /** Correlation ID for distributed tracing */
    correlationId: string;
}

/**
 * Response from BFF /api/documents/{id}/checkout endpoint
 */
export interface CheckoutResponse {
    /** Whether checkout was successful */
    success: boolean;
    /** Office Online edit URL (embedview) */
    editUrl: string;
    /** When the checkout occurred */
    checkedOutAt: string;
    /** Version number at checkout */
    versionNumber: number;
    /** Correlation ID */
    correlationId: string;
}

/**
 * Response from BFF /api/documents/{id}/checkin endpoint
 */
export interface CheckInResponse {
    /** Whether check-in was successful */
    success: boolean;
    /** New version number after check-in */
    newVersionNumber: number;
    /** FileVersion record ID */
    fileVersionId: string;
    /** Updated preview URL */
    previewUrl: string;
    /** Correlation ID */
    correlationId: string;
}

/**
 * Response from BFF /api/documents/{id}/discard endpoint
 */
export interface DiscardResponse {
    /** Whether discard was successful */
    success: boolean;
    /** Message */
    message: string;
    /** Correlation ID */
    correlationId: string;
}

/**
 * Response from BFF DELETE /api/documents/{id} endpoint
 */
export interface DeleteDocumentResponse {
    /** Whether delete was successful */
    success: boolean;
    /** Message */
    message: string;
    /** Correlation ID */
    correlationId: string;
}

/**
 * Document locked error (409 Conflict)
 */
export interface DocumentLockedError {
    /** Error code */
    error: string;
    /** Error detail message */
    detail: string;
    /** User who has it locked */
    checkedOutBy: CheckoutUserInfo;
    /** When it was locked */
    checkedOutAt: string | null;
}

/**
 * Response from BFF /api/documents/{id}/open-links endpoint
 */
export interface OpenLinksResponse {
    /** Desktop protocol URL (ms-word:, ms-excel:, etc.) */
    desktopUrl: string | null;
    /** SharePoint web URL */
    webUrl: string;
    /** MIME type */
    mimeType: string;
    /** File name */
    fileName: string;
}

/**
 * Error response from BFF API (RFC 7807 Problem Details)
 */
export interface BffErrorResponse {
    status: number;
    title: string;
    detail?: string;
    correlationId?: string;
    errors?: Record<string, string[]>;
    extensions?: {
        code?: string;
        [key: string]: unknown;
    };
}

/**
 * Component state for SpeDocumentViewer React component
 */
export interface DocumentViewerComponentState {
    /** Current view mode */
    viewMode: ViewMode;
    /** SharePoint preview URL (embed.aspx) */
    previewUrl: string | null;
    /** Office Online edit URL (embedview) */
    editUrl: string | null;
    /** Loading state for API fetch */
    isLoading: boolean;
    /** Loading state for iframe content */
    isIframeLoading: boolean;
    /** Loading state for checkout operation */
    isCheckoutLoading: boolean;
    /** Loading state for check-in operation */
    isCheckInLoading: boolean;
    /** Error message to display */
    error: string | null;
    /** Document metadata */
    documentInfo: DocumentInfo | null;
    /** Current checkout status */
    checkoutStatus: CheckoutStatus | null;
}

/**
 * Props for SpeDocumentViewer React component
 */
export interface DocumentViewerProps {
    /** Document GUID to view/edit */
    documentId: string;
    /** BFF base URL */
    bffApiUrl: string;
    /** Access token from MSAL */
    accessToken: string;
    /** Correlation ID for request tracking */
    correlationId: string;
    /** Dark mode state */
    isDarkTheme: boolean;
    /** Feature flag: enable edit workflow */
    enableEdit: boolean;
    /** Feature flag: enable delete button */
    enableDelete: boolean;
    /** Feature flag: enable download button */
    enableDownload: boolean;
    /** Callback when refresh is requested */
    onRefresh?: () => void;
    /** Callback when document is deleted */
    onDeleted?: () => void;
}

/**
 * PCF Control input properties (from manifest)
 */
export interface ControlInputs {
    value: ComponentFramework.PropertyTypes.StringProperty;
    documentId: ComponentFramework.PropertyTypes.StringProperty;
    bffApiUrl: ComponentFramework.PropertyTypes.StringProperty;
    clientAppId: ComponentFramework.PropertyTypes.StringProperty;
    bffAppId: ComponentFramework.PropertyTypes.StringProperty;
    tenantId: ComponentFramework.PropertyTypes.StringProperty;
    enableEdit: ComponentFramework.PropertyTypes.TwoOptionsProperty;
    enableDelete: ComponentFramework.PropertyTypes.TwoOptionsProperty;
    enableDownload: ComponentFramework.PropertyTypes.TwoOptionsProperty;
    controlHeight: ComponentFramework.PropertyTypes.WholeNumberProperty;
}

/**
 * PCF Control outputs
 */
// eslint-disable-next-line @typescript-eslint/no-empty-object-type
export interface ControlOutputs {
    // No outputs for this control
}
