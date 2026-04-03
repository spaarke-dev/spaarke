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

import type {
  DialogOptions,
  DialogResult,
  INavigationService,
  LookupOptions,
  LookupResult,
} from '../../types/serviceInterfaces';
import { getXrm } from '../xrmContext';

/**
 * Normalises a dimension value to the Xrm.Navigation format.
 *
 * INavigationService accepts `number` (pixels) or `{ value, unit }`.
 * Xrm.Navigation.navigateTo also accepts both forms, so we pass through
 * as-is, but ensure plain numbers are treated as pixels.
 *
 * @param dim - Dimension from DialogOptions (number | { value, unit })
 * @returns The dimension in Xrm-compatible format
 */
function normaliseDimension(
  dim: number | { value: number; unit: '%' | 'px' } | undefined
): number | { value: number; unit: '%' | 'px' } | undefined {
  if (dim === undefined) {
    return undefined;
  }
  if (typeof dim === 'number') {
    return { value: dim, unit: 'px' };
  }
  return dim;
}

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
export function createXrmNavigationService(): INavigationService {
  /**
   * Resolves the Xrm.Navigation reference, throwing a descriptive error
   * when the host environment does not expose navigation capabilities.
   */
  function getNavigation() {
    const xrm = getXrm();
    if (!xrm?.Navigation) {
      throw new Error(
        'Xrm.Navigation is not available. Ensure this adapter is used within a Dataverse-hosted context (PCF control or Code Page).'
      );
    }
    return xrm.Navigation;
  }

  return {
    async openRecord(entityName: string, entityId: string): Promise<void> {
      const navigation = getNavigation();
      await navigation.openForm({ entityName, entityId });
    },

    async openDialog(
      webresourceName: string,
      data?: string,
      options?: DialogOptions
    ): Promise<DialogResult> {
      const navigation = getNavigation();

      // Build the navigateTo page input
      const pageInput: Record<string, unknown> = {
        pageType: 'webresource',
        webresourceName,
      };
      if (data !== undefined) {
        pageInput['data'] = data;
      }

      // Build the navigation options
      const navOptions: Record<string, unknown> = {
        target: 2, // Open as dialog
      };

      if (options?.width !== undefined) {
        navOptions['width'] = normaliseDimension(options.width);
      }
      if (options?.height !== undefined) {
        navOptions['height'] = normaliseDimension(options.height);
      }
      if (options?.title !== undefined) {
        navOptions['title'] = options.title;
      }

      try {
        // Xrm.Navigation.navigateTo returns a promise that resolves when
        // the dialog is closed. The actual Xrm API accepts (pageInput, navOptions)
        // but our typed interface only has one parameter. We use the underlying
        // runtime call directly via the Xrm object.
        const xrm = getXrm();
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrmNav = xrm?.Navigation as any;
        const result = await xrmNav.navigateTo(pageInput, navOptions);

        // The navigateTo result may contain dialog return data
        if (result && typeof result === 'object') {
          return result as DialogResult;
        }

        // Dialog closed without explicit result — treat as confirmed
        return { confirmed: true };
      } catch {
        // Dialog was cancelled or closed via X button
        return { confirmed: false };
      }
    },

    closeDialog(result?: unknown): void {
      // Code Pages opened via Xrm.Navigation.navigateTo({ target: 2 }) run
      // inside a Dataverse dialog iframe. window.close() is blocked by browsers.
      // The proven approach: find and click the platform's X button in the
      // parent frame DOM (same-origin in Dataverse web resources).
      //
      // This is the same pattern used by DocumentUploadWizard/App.tsx which
      // has been validated in production.
      if (typeof window === 'undefined') return;

      if (result !== undefined) {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        (window as any).__dialogResult = result;
      }

      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const frames = [window, window.parent, window.top].filter(Boolean) as Window[];
      for (const frame of frames) {
        try {
          const closeBtn =
            frame?.document?.querySelector('[data-id="dialogCloseIconButton"]') as HTMLElement
            ?? frame?.document?.querySelector('.ms-Dialog-button--close') as HTMLElement;
          if (closeBtn) {
            closeBtn.click();
            return;
          }
        } catch { /* cross-origin frame */ }
      }

      // Fallback: window.close() — works for standalone popups
      try { window.close(); } catch { /* blocked by browser */ }
    },

    async openLookup(options: LookupOptions): Promise<LookupResult[]> {
      const xrm = getXrm();
      if (!xrm?.Utility) {
        throw new Error(
          'Xrm.Utility is not available. Ensure this adapter is used within a Dataverse-hosted context (PCF control or Code Page).'
        );
      }

      // Build the lookupObjects options
      // Xrm.Utility.lookupObjects accepts: entityTypes, allowMultiSelect, defaultEntityType, defaultViewId
      const lookupObjectsOptions: Record<string, unknown> = {
        entityTypes: options.entityTypes ?? [options.entityType],
        allowMultiSelect: options.allowMultiSelect ?? false,
      };

      if (options.defaultEntityType !== undefined) {
        lookupObjectsOptions['defaultEntityType'] = options.defaultEntityType;
      }
      if (options.defaultViewId !== undefined) {
        lookupObjectsOptions['defaultViewId'] = options.defaultViewId;
      }

      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrmUtility = xrm.Utility as any;
        // Xrm.Utility.lookupObjects returns Promise<LookupValue[]>
        // where LookupValue has: id, name, entityType
        const results: Array<{ id: string; name: string; entityType: string }> =
          await xrmUtility.lookupObjects(lookupObjectsOptions);

        if (!Array.isArray(results)) {
          return [];
        }

        return results.map((r) => ({
          id: r.id,
          name: r.name,
          entityType: r.entityType,
        }));
      } catch {
        // User cancelled the lookup dialog — return empty array
        return [];
      }
    },
  };
}
