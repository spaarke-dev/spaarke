/**
 * Service Interfaces - High-level abstractions for data, upload, and navigation operations
 *
 * These interfaces decouple shared UI components from platform-specific APIs
 * (Xrm.WebApi, ComponentFramework.WebApi, etc.) so components remain portable
 * across PCF controls, Code Pages, SPAs, and test harnesses.
 *
 * @see ADR-012 - Shared Component Library (no PCF-specific dependencies)
 * @see WebApiLike.ts - Lower-level WebAPI abstraction (consumed by services, not components)
 */

// ---------------------------------------------------------------------------
// IDataService
// ---------------------------------------------------------------------------

/**
 * High-level data access abstraction for Dataverse entity operations.
 *
 * Unlike {@link IWebApiLike} (which mirrors the raw WebAPI surface),
 * IDataService provides a simplified contract suitable for direct consumption
 * by shared React components and hooks.
 *
 * @example PCF adapter:
 * ```typescript
 * const dataService: IDataService = {
 *   createRecord: async (entityName, data) => {
 *     const ref = await context.webAPI.createRecord(entityName, data);
 *     return ref.id;
 *   },
 *   retrieveRecord: (entityName, id, options) =>
 *     context.webAPI.retrieveRecord(entityName, id, options),
 *   retrieveMultipleRecords: async (entityName, options) => {
 *     const result = await context.webAPI.retrieveMultipleRecords(entityName, options);
 *     return { entities: result.entities };
 *   },
 *   updateRecord: async (entityName, id, data) => {
 *     await context.webAPI.updateRecord(entityName, id, data);
 *   },
 *   deleteRecord: async (entityName, id) => {
 *     await context.webAPI.deleteRecord(entityName, id);
 *   },
 * };
 * ```
 *
 * @example Test mock:
 * ```typescript
 * const mockDataService: IDataService = {
 *   createRecord: jest.fn().mockResolvedValue("00000000-0000-0000-0000-000000000001"),
 *   retrieveRecord: jest.fn().mockResolvedValue({ sprk_name: "Test" }),
 *   retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
 *   updateRecord: jest.fn().mockResolvedValue(undefined),
 *   deleteRecord: jest.fn().mockResolvedValue(undefined),
 * };
 * ```
 */
export interface IDataService {
  /**
   * Creates a new entity record.
   *
   * @param entityName - Logical name of the entity (e.g., "sprk_matter")
   * @param data - Field values and @odata.bind lookups for the new record
   * @returns Promise resolving to the GUID of the created record
   */
  createRecord(entityName: string, data: Record<string, unknown>): Promise<string>;

  /**
   * Retrieves a single entity record by ID.
   *
   * @param entityName - Logical name of the entity (e.g., "sprk_matter")
   * @param id - GUID of the record to retrieve
   * @param options - OData query options (e.g., "$select=sprk_name,statuscode")
   * @returns Promise resolving to the entity record
   */
  retrieveRecord(
    entityName: string,
    id: string,
    options?: string
  ): Promise<Record<string, unknown>>;

  /**
   * Retrieves multiple entity records using an OData query.
   *
   * @param entityName - Logical name of the entity (e.g., "sprk_matter")
   * @param options - OData query string (e.g., "?$filter=statecode eq 0&$select=sprk_name")
   * @returns Promise resolving to an object containing the entities array
   */
  retrieveMultipleRecords(
    entityName: string,
    options?: string
  ): Promise<{ entities: Record<string, unknown>[] }>;

  /**
   * Updates an existing entity record.
   *
   * @param entityName - Logical name of the entity (e.g., "sprk_matter")
   * @param id - GUID of the record to update
   * @param data - Field values to update
   */
  updateRecord(entityName: string, id: string, data: Record<string, unknown>): Promise<void>;

  /**
   * Deletes an entity record.
   *
   * @param entityName - Logical name of the entity (e.g., "sprk_matter")
   * @param id - GUID of the record to delete
   */
  deleteRecord(entityName: string, id: string): Promise<void>;
}

// ---------------------------------------------------------------------------
// IUploadService
// ---------------------------------------------------------------------------

/**
 * Progress callback invoked during file upload.
 *
 * @param loaded - Number of bytes uploaded so far
 * @param total - Total file size in bytes
 */
export type UploadProgressCallback = (loaded: number, total: number) => void;

/**
 * Options for file upload operations.
 */
export interface UploadOptions {
  /** Callback invoked as upload bytes are transmitted */
  onProgress?: UploadProgressCallback;
  /** Arbitrary metadata to associate with the uploaded file */
  metadata?: Record<string, unknown>;
}

/**
 * Result returned after a successful file upload.
 */
export interface UploadResult {
  /** Unique identifier of the uploaded file */
  id: string;
  /** Display name of the uploaded file */
  name: string;
  /** File size in bytes */
  size: number;
  /** URL where the uploaded file can be accessed */
  url: string;
}

/**
 * High-level abstraction for file upload operations.
 *
 * Decouples shared components from the underlying storage mechanism
 * (SharePoint Embedded, Blob Storage, etc.) and from BFF API specifics.
 *
 * @example Usage in a shared component:
 * ```typescript
 * async function handleUpload(uploadService: IUploadService, file: File) {
 *   const result = await uploadService.uploadFile(
 *     "sprk_matter",
 *     matterId,
 *     file,
 *     { onProgress: (loaded, total) => setProgress(loaded / total * 100) }
 *   );
 *   console.log(`Uploaded ${result.name} (${result.size} bytes) → ${result.url}`);
 * }
 * ```
 *
 * @example Test mock:
 * ```typescript
 * const mockUploadService: IUploadService = {
 *   uploadFile: jest.fn().mockResolvedValue({
 *     id: "file-001",
 *     name: "document.pdf",
 *     size: 1024,
 *     url: "https://example.com/document.pdf",
 *   }),
 *   getContainerIdForEntity: jest.fn().mockResolvedValue("container-001"),
 * };
 * ```
 */
export interface IUploadService {
  /**
   * Uploads a file associated with a Dataverse entity record.
   *
   * @param entityName - Logical name of the parent entity (e.g., "sprk_matter")
   * @param entityId - GUID of the parent entity record
   * @param file - The File object to upload
   * @param options - Optional upload configuration (progress callback, metadata)
   * @returns Promise resolving to the upload result with file details
   */
  uploadFile(
    entityName: string,
    entityId: string,
    file: File,
    options?: UploadOptions
  ): Promise<UploadResult>;

  /**
   * Retrieves the storage container ID for a given entity record.
   *
   * Used when the caller needs the container reference before performing
   * additional storage operations (e.g., listing files, bulk uploads).
   *
   * @param entityName - Logical name of the entity (e.g., "sprk_matter")
   * @param entityId - GUID of the entity record
   * @returns Promise resolving to the container ID string
   */
  getContainerIdForEntity(entityName: string, entityId: string): Promise<string>;
}

// ---------------------------------------------------------------------------
// INavigationService
// ---------------------------------------------------------------------------

/**
 * Options passed to {@link INavigationService.openLookup} to configure the
 * Dataverse lookup dialog.
 */
export interface LookupOptions {
  /**
   * Logical name of the primary entity to search (e.g., "sprk_matter").
   * Must be provided; used when `entityTypes` is not supplied.
   */
  entityType: string;

  /**
   * Whether the user may select more than one record.
   * Defaults to `false` (single-select).
   */
  allowMultiSelect?: boolean;

  /**
   * The entity type that is pre-selected in the entity-type picker when the
   * lookup dialog supports multiple entity types. Defaults to `entityType`.
   */
  defaultEntityType?: string;

  /**
   * List of entity logical names available in the entity-type picker.
   * When omitted the dialog restricts to `entityType` only.
   */
  entityTypes?: string[];

  /**
   * GUID of the default view to display in the lookup dialog.
   * When omitted the entity's default lookup view is used.
   */
  defaultViewId?: string;
}

/**
 * A single record returned by {@link INavigationService.openLookup}.
 */
export interface LookupResult {
  /** GUID of the selected record. */
  id: string;
  /** Display name of the selected record. */
  name: string;
  /** Logical name of the entity (e.g., "sprk_matter"). */
  entityType: string;
}

/**
 * Options for opening a dialog (webresource-based Code Page).
 */
export interface DialogOptions {
  /** Dialog width — number of pixels or a percentage object */
  width?: number | { value: number; unit: "%" | "px" };
  /** Dialog height — number of pixels or a percentage object */
  height?: number | { value: number; unit: "%" | "px" };
  /** Title displayed in the dialog header */
  title?: string;
}

/**
 * Result returned when a dialog is closed.
 */
export interface DialogResult {
  /** Whether the user confirmed / completed the dialog action */
  confirmed: boolean;
  /** Arbitrary data returned by the dialog */
  data?: unknown;
}

/**
 * High-level abstraction for navigation and dialog operations.
 *
 * Decouples shared components from Xrm.Navigation so they remain
 * portable across PCF controls, Code Pages, SPAs, and test harnesses.
 *
 * @example Usage in a shared component:
 * ```typescript
 * async function openMatterDetail(nav: INavigationService, matterId: string) {
 *   await nav.openRecord("sprk_matter", matterId);
 * }
 *
 * async function showUploadWizard(nav: INavigationService, matterId: string) {
 *   const result = await nav.openDialog(
 *     "sprk_uploadwizard",
 *     `matterId=${matterId}`,
 *     { width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" }, title: "Upload Files" }
 *   );
 *   if (result.confirmed) {
 *     console.log("Upload completed:", result.data);
 *   }
 * }
 * ```
 *
 * @example Xrm adapter:
 * ```typescript
 * const navigationService: INavigationService = {
 *   openRecord: (entityName, entityId) =>
 *     Xrm.Navigation.openForm({ entityName, entityId }),
 *   openDialog: async (webresourceName, data, options) => {
 *     const result = await Xrm.Navigation.navigateTo(
 *       { pageType: "webresource", webresourceName, data },
 *       { target: 2, width: options?.width, height: options?.height, title: options?.title }
 *     );
 *     return result as DialogResult;
 *   },
 *   closeDialog: () => window.close(),
 * };
 * ```
 *
 * @example Test mock:
 * ```typescript
 * const mockNavService: INavigationService = {
 *   openRecord: jest.fn().mockResolvedValue(undefined),
 *   openDialog: jest.fn().mockResolvedValue({ confirmed: true, data: { id: "123" } }),
 *   closeDialog: jest.fn(),
 *   openLookup: jest.fn().mockResolvedValue([]),
 * };
 * ```
 */
export interface INavigationService {
  /**
   * Opens a Dataverse entity record form.
   *
   * @param entityName - Logical name of the entity (e.g., "sprk_matter")
   * @param entityId - GUID of the record to open
   */
  openRecord(entityName: string, entityId: string): Promise<void>;

  /**
   * Opens a dialog backed by a webresource (Code Page).
   *
   * @param webresourceName - Name of the webresource (e.g., "sprk_uploadwizard")
   * @param data - Optional query-string data to pass to the dialog
   * @param options - Optional dialog dimensions and title
   * @returns Promise resolving to the dialog result when closed
   */
  openDialog(
    webresourceName: string,
    data?: string,
    options?: DialogOptions
  ): Promise<DialogResult>;

  /**
   * Closes the current dialog, optionally returning a result to the opener.
   *
   * @param result - Optional data to pass back to the dialog opener
   */
  closeDialog(result?: unknown): void;

  /**
   * Opens a Dataverse lookup dialog and returns the selected record(s).
   *
   * In a Dataverse-hosted context this delegates to `Xrm.Utility.lookupObjects`.
   * In an SPA/BFF context the lookup dialog is not available — the adapter returns
   * an empty array as a graceful no-op.
   *
   * @param options - Entity type, multi-select flag, and optional view/entity filters
   * @returns Promise resolving to the array of selected records (empty if cancelled)
   */
  openLookup(options: LookupOptions): Promise<LookupResult[]>;
}
