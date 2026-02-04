/**
 * Type definitions for Universal Dataset Grid PCF Control
 * Version 2.0.0 - With SDAP Integration and Fluent UI v9
 */

/**
 * Field mappings configuration.
 * Maps logical field names to actual Dataverse field names.
 *
 * Note: All field names use the 'sprk_' publisher prefix.
 */
export interface FieldMappings {
    /** Boolean field indicating if document has an attached file */
    hasFile: string;

    /** File name (e.g., "document.pdf") */
    fileName: string;

    /** File size in bytes */
    fileSize: string;

    /** MIME type (e.g., "application/pdf") */
    mimeType: string;

    /** SharePoint Graph API item ID */
    graphItemId: string;

    /** SharePoint Graph API drive ID */
    graphDriveId: string;
}

/**
 * Custom command button configuration.
 */
export interface CustomCommand {
    /** Unique command identifier */
    id: string;

    /** Display label for the button */
    label: string;

    /** Fluent UI icon name (e.g., "Add24Regular") */
    icon: string;

    /** Enable rule expression (evaluated at runtime) */
    enableRule: string;

    /** Error message to show when command cannot be executed */
    errorMessage: string;

    /** Button appearance (primary, secondary, subtle) */
    appearance?: 'primary' | 'secondary' | 'subtle';
}

/**
 * SDAP client configuration.
 */
export interface SdapConfig {
    /** Base URL of SDAP BFF API */
    baseUrl: string;

    /** Request timeout in milliseconds */
    timeout: number;
}

/**
 * Overall grid configuration.
 */
export interface GridConfiguration {
    /** Field name mappings */
    fieldMappings: FieldMappings;

    /** Custom command buttons */
    customCommands: CustomCommand[];

    /** SDAP client configuration */
    sdapConfig: SdapConfig;

    /**
     * Enable checkbox selection column (Task 014)
     * When true, adds a checkbox column as the first column for bulk selection.
     * Header checkbox enables select all/deselect all.
     * @default true
     */
    enableCheckboxSelection?: boolean;
}

/**
 * Default grid configuration with sprk_ field prefix.
 */
export const DEFAULT_GRID_CONFIG: GridConfiguration = {
    fieldMappings: {
        hasFile: 'sprk_hasfile',
        fileName: 'sprk_filename',
        fileSize: 'sprk_filesize',
        mimeType: 'sprk_mimetype',
        graphItemId: 'sprk_graphitemid',
        graphDriveId: 'sprk_graphdriveid'
    },
    /** Enable checkbox selection by default (Task 014) */
    enableCheckboxSelection: true,
    customCommands: [
        {
            id: 'addFile',
            label: 'Add File',
            icon: 'Add24Regular',
            enableRule: 'selectedCount === 1 && !hasFile',
            errorMessage: 'Select a single document without a file',
            appearance: 'primary'
        },
        {
            id: 'removeFile',
            label: 'Remove File',
            icon: 'Delete24Regular',
            enableRule: 'selectedCount === 1 && hasFile',
            errorMessage: 'Select a single document with a file',
            appearance: 'secondary'
        },
        {
            id: 'updateFile',
            label: 'Update File',
            icon: 'ArrowUpload24Regular',
            enableRule: 'selectedCount === 1 && hasFile',
            errorMessage: 'Select a single document with a file',
            appearance: 'secondary'
        },
        {
            id: 'downloadFile',
            label: 'Download',
            icon: 'ArrowDownload24Regular',
            enableRule: 'selectedCount > 0 && (selectedCount > 1 || hasFile)',
            errorMessage: 'Select at least one document with a file',
            appearance: 'secondary'
        }
    ],
    sdapConfig: {
        baseUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net',
        timeout: 300000 // 5 minutes
    }
};

/**
 * Command context for evaluating enable rules.
 */
export interface CommandContext {
    /** Number of selected records */
    selectedCount: number;

    /** Whether the single selected record has a file */
    hasFile: boolean;

    /** Selected record IDs */
    selectedRecordIds: string[];
}

/**
 * SDAP-specific type definitions for SharePoint Embedded operations
 */

/**
 * SPE File Metadata returned from SDAP API (matches FileHandleDto from Spe.Bff.Api)
 *
 * Maps to Dataverse fields:
 * - id → sprk_graphitemid
 * - name → sprk_filename
 * - size → sprk_filesize
 * - createdDateTime → sprk_createddatetime
 * - lastModifiedDateTime → sprk_lastmodifieddatetime
 * - eTag → sprk_etag
 * - parentId → sprk_parentfolderid
 * - webUrl → sprk_filepath (URL field)
 */
export interface SpeFileMetadata {
    /** Graph API Item ID */
    id: string;

    /** File name */
    name: string;

    /** Parent folder ID (optional) */
    parentId?: string;

    /** File size in bytes */
    size: number;

    /** Created date/time (ISO 8601) */
    createdDateTime: string;

    /** Last modified date/time (ISO 8601) */
    lastModifiedDateTime: string;

    /** Version identifier (ETag) */
    eTag?: string;

    /** Is this a folder */
    isFolder: boolean;

    /** SharePoint web URL (may not be available in all responses) */
    webUrl?: string;
}

/**
 * File upload request parameters
 * API: PUT /api/drives/{driveId}/upload?fileName={name}
 */
export interface FileUploadRequest {
    /** File to upload */
    file: File;

    /** Graph API Drive ID (from sprk_graphdriveid or Container) */
    driveId: string;

    /** File name */
    fileName: string;
}

/**
 * File download request parameters
 * API: GET /api/drives/{driveId}/items/{itemId}/content
 */
export interface FileDownloadRequest {
    /** Graph API Drive ID (from sprk_graphdriveid) */
    driveId: string;

    /** Graph API Item ID (from sprk_graphitemid) */
    itemId: string;
}

/**
 * File delete request parameters
 * API: DELETE /api/drives/{driveId}/items/{itemId}
 */
export interface FileDeleteRequest {
    /** Graph API Drive ID (from sprk_graphdriveid) */
    driveId: string;

    /** Graph API Item ID (from sprk_graphitemid) */
    itemId: string;
}

/**
 * File replace request parameters
 * Replace = Delete existing + Upload new
 */
export interface FileReplaceRequest {
    /** New file to upload */
    file: File;

    /** Graph API Drive ID (from sprk_graphdriveid) */
    driveId: string;

    /** Graph API Item ID of file to replace (from sprk_graphitemid) */
    itemId: string;

    /** New file name */
    fileName: string;
}

/**
 * API Response wrapper
 */
export interface ApiResponse<T> {
    success: boolean;
    data?: T;
    error?: string;
    details?: string;
}

/**
 * Service operation result
 */
export interface ServiceResult<T = void> {
    success: boolean;
    data?: T;
    error?: string;
}

// =============================================================================
// Calendar Filter Types (Task 010)
// =============================================================================

/**
 * Filter type discriminator for calendar filter input
 */
export type CalendarFilterType = "single" | "range" | "clear";

/**
 * Single date filter
 * Format: {"type":"single","date":"YYYY-MM-DD"}
 */
export interface ICalendarFilterSingle {
    type: "single";
    date: string;
}

/**
 * Date range filter
 * Format: {"type":"range","start":"YYYY-MM-DD","end":"YYYY-MM-DD"}
 */
export interface ICalendarFilterRange {
    type: "range";
    start: string;
    end: string;
}

/**
 * Clear filter (no date filter applied)
 * Format: {"type":"clear"}
 */
export interface ICalendarFilterClear {
    type: "clear";
}

/**
 * Union type for all calendar filter types
 */
export type CalendarFilter =
    | ICalendarFilterSingle
    | ICalendarFilterRange
    | ICalendarFilterClear;

/**
 * Parse calendar filter JSON string
 * Returns null if invalid/empty
 */
export function parseCalendarFilter(json: string | null | undefined): CalendarFilter | null {
    if (!json || json.trim() === "") {
        return null;
    }

    try {
        const parsed = JSON.parse(json);

        if (!parsed || typeof parsed !== "object" || !("type" in parsed)) {
            return null;
        }

        if (parsed.type === "single" && typeof parsed.date === "string") {
            return parsed as ICalendarFilterSingle;
        }

        if (
            parsed.type === "range" &&
            typeof parsed.start === "string" &&
            typeof parsed.end === "string"
        ) {
            return parsed as ICalendarFilterRange;
        }

        if (parsed.type === "clear") {
            return parsed as ICalendarFilterClear;
        }

        return null;
    } catch {
        return null;
    }
}

/**
 * Type guard: Check if filter is a single date
 */
export function isSingleDateFilter(filter: CalendarFilter): filter is ICalendarFilterSingle {
    return filter.type === "single";
}

/**
 * Type guard: Check if filter is a date range
 */
export function isRangeFilter(filter: CalendarFilter): filter is ICalendarFilterRange {
    return filter.type === "range";
}

/**
 * Type guard: Check if filter is clear
 */
export function isClearFilter(filter: CalendarFilter): filter is ICalendarFilterClear {
    return filter.type === "clear";
}

// =============================================================================
// Optimistic Row Update Types (Task 015)
// =============================================================================

/**
 * Field update for optimistic row update.
 * Represents a single field value change.
 */
export interface RowFieldUpdate {
    /** Field schema name (e.g., 'sprk_eventname', 'statuscode') */
    fieldName: string;

    /** New formatted value for display (e.g., 'Meeting with Client') */
    formattedValue: string;

    /** New raw value (optional, for lookup IDs, numbers, etc.) */
    rawValue?: unknown;
}

/**
 * Request to optimistically update a single row in the grid.
 * Used by Side Pane to update grid display without full refresh.
 */
export interface OptimisticRowUpdateRequest {
    /** Record ID (GUID) of the row to update */
    recordId: string;

    /** Fields to update with their new values */
    updates: RowFieldUpdate[];
}

/**
 * Result of an optimistic update operation.
 * Includes rollback function for error recovery.
 */
export interface OptimisticUpdateResult {
    /** Whether the update was successful */
    success: boolean;

    /** Error message if update failed */
    error?: string;

    /** Function to rollback to previous values (call on save error) */
    rollback: () => void;
}

/**
 * Callback function type for optimistic row updates.
 * Exposed via window object for Side Pane to call.
 */
export type OptimisticRowUpdateCallback = (
    request: OptimisticRowUpdateRequest
) => OptimisticUpdateResult;

/**
 * Global interface for grid communication.
 * Attached to window.spaarkeGrid for cross-component access.
 */
export interface SpaarkeGridApi {
    /**
     * Update a single row optimistically.
     * Call this from Side Pane after saving changes.
     *
     * @param request - The row update request
     * @returns Result with rollback function
     *
     * @example
     * // In Side Pane after successful save:
     * const result = window.spaarkeGrid.updateRow({
     *     recordId: 'abc-123-def',
     *     updates: [
     *         { fieldName: 'sprk_eventname', formattedValue: 'New Name' },
     *         { fieldName: 'statuscode', formattedValue: 'Open', rawValue: 3 }
     *     ]
     * });
     *
     * if (!result.success) {
     *     console.error('Update failed:', result.error);
     * }
     */
    updateRow: OptimisticRowUpdateCallback;

    /**
     * Force a full dataset refresh.
     * Use when optimistic update isn't suitable (e.g., complex changes).
     */
    refresh: () => void;
}

// Extend Window interface for TypeScript
declare global {
    interface Window {
        spaarkeGrid?: SpaarkeGridApi;
    }
}
