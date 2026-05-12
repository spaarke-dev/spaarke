/**
 * Xrm Navigation Service Adapter for INavigationService
 *
 * Bridges the Xrm.Navigation runtime API to the platform-agnostic
 * INavigationService interface, enabling shared components to open
 * entity forms, Code Page dialogs, and close dialogs without coupling
 * to the Xrm SDK directly.
 *
 * @see INavigationService in ../../types/serviceInterfaces
 * @see ADR-006 - Anti-Legacy JS (Code Pages for standalone dialogs)
 * @see ADR-012 - Shared Component Library
 *
 * @example
 * ```typescript
 * import { createXrmNavigationService } from "@spaarke/ui-components";
 *
 * const navService = createXrmNavigationService();
 *
 * // Open a record form
 * await navService.openRecord("sprk_matter", matterId);
 *
 * // Open a Code Page dialog
 * const result = await navService.openDialog(
 *   "sprk_uploadwizard",
 *   `matterId=${matterId}`,
 *   { width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" }, title: "Upload Files" }
 * );
 * if (result.confirmed) {
 *   console.log("Dialog completed:", result.data);
 * }
 * ```
 */
import type { INavigationService } from '../../types/serviceInterfaces';
/**
 * Creates an INavigationService implementation backed by Xrm.Navigation.
 *
 * - `openRecord` delegates to `Xrm.Navigation.openForm`
 * - `openDialog` delegates to `Xrm.Navigation.navigateTo` with `pageType: "webresource"`
 * - `closeDialog` calls `window.close()` (Code Pages close their own browser window)
 *
 * @returns An INavigationService backed by the current Xrm.Navigation context
 * @throws Error if the Xrm context or Navigation API is unavailable at call time
 *
 * @example
 * ```typescript
 * const navService = createXrmNavigationService();
 *
 * // Open a matter record
 * await navService.openRecord("sprk_matter", "00000000-0000-0000-0000-000000000001");
 *
 * // Open a dialog with percentage-based sizing
 * const result = await navService.openDialog(
 *   "sprk_documentviewer",
 *   `documentId=${docId}`,
 *   { width: { value: 90, unit: "%" }, height: { value: 90, unit: "%" }, title: "View Document" }
 * );
 * ```
 */
export declare function createXrmNavigationService(): INavigationService;
//# sourceMappingURL=xrmNavigationServiceAdapter.d.ts.map