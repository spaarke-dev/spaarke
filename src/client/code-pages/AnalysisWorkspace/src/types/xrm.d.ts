/**
 * Xrm SDK type declarations for AnalysisWorkspace Code Page
 *
 * Declares the subset of the Xrm SDK used for authentication and
 * environment discovery. The AnalysisWorkspace runs inside a Dataverse-hosted
 * iframe and has access to Xrm via the global scope or parent frames.
 *
 * IMPORTANT: These are ambient declarations -- DO NOT import this file.
 * TypeScript picks them up automatically via tsconfig "include".
 *
 * Pattern: Copied from SprkChatPane/src/types/xrm.d.ts (task 013)
 *
 * @see https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-utility
 */

// ---------------------------------------------------------------------------
// Xrm.Utility.getGlobalContext() return type
// ---------------------------------------------------------------------------

/**
 * Subset of the Xrm GlobalContext API used for authentication.
 *
 * @see https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-utility/getglobalcontext
 */
interface XrmGlobalContext {
    /** Returns the base URL of the Dataverse environment (e.g., "https://org.crm.dynamics.com"). */
    getClientUrl(): string;

    /** Returns the current user's unique ID (GUID without braces). */
    getUserId(): string;

    /** Returns the current user's display name. */
    getUserName(): string;

    /**
     * Returns the Dataverse theme information.
     * Available in model-driven apps to detect dark/light mode.
     */
    getCurrentTheme?(): XrmThemeInfo | undefined;
}

/**
 * Theme information returned by Xrm.Utility.getGlobalContext().getCurrentTheme().
 */
interface XrmThemeInfo {
    /** Page/content background color (e.g., "#ffffff" or "#1f1f1f"). */
    backgroundcolor?: string;
    /** Navigation bar background color (brand color -- not light/dark indicator). */
    navbarbackgroundcolor?: string;
    /** Navigation bar shelf (secondary nav) color. */
    navbarshelfcolor?: string;
    /** Header color. */
    headercolor?: string;
}

// ---------------------------------------------------------------------------
// Xrm.Utility namespace
// ---------------------------------------------------------------------------

/**
 * Xrm.Utility namespace -- provides helper methods including getGlobalContext().
 */
interface XrmUtilityNamespace {
    /** Returns the global context object for the Dataverse environment. */
    getGlobalContext(): XrmGlobalContext;
}

// ---------------------------------------------------------------------------
// Xrm.Page namespace (form context for entity data)
// ---------------------------------------------------------------------------

/**
 * Xrm.Page.data.entity -- provides entity metadata from the current form.
 */
interface XrmPageEntity {
    /** Returns the logical name of the entity (e.g., "sprk_matter"). */
    getEntityName(): string;
    /** Returns the record GUID (with braces, e.g., "{GUID}"). */
    getId(): string;
}

/**
 * Xrm.Page.data -- provides access to entity data on the current form.
 */
interface XrmPageData {
    entity: XrmPageEntity;
}

/**
 * Xrm.Page -- the legacy page context API (still widely available).
 */
interface XrmPage {
    data?: XrmPageData;
    context?: {
        getAuthToken?(): Promise<string>;
    };
}

// ---------------------------------------------------------------------------
// Xrm.Utility.getPageContext() return type
// ---------------------------------------------------------------------------

/**
 * Page context returned by Xrm.Utility.getPageContext().
 * Contains the input object with entity routing info.
 */
interface XrmPageContext {
    input?: {
        /** The logical name of the entity (e.g., "sprk_matter"). */
        entityName?: string;
        /** The record GUID. */
        entityId?: string;
    };
}

// ---------------------------------------------------------------------------
// Top-level Xrm namespace
// ---------------------------------------------------------------------------

/**
 * Top-level Xrm namespace available in Dataverse model-driven app contexts.
 * May exist on window, window.parent, or window.top depending on iframe nesting.
 */
interface XrmNamespace {
    Utility: XrmUtilityNamespace & {
        /** Returns page context with entity routing info. */
        getPageContext?(): XrmPageContext | undefined;
    };
    /** Legacy page context -- available when a form is loaded. */
    Page?: XrmPage;
}

// ---------------------------------------------------------------------------
// Window augmentation
// ---------------------------------------------------------------------------

/**
 * Augment the Window interface so TypeScript recognizes window.Xrm.
 * Xrm is optional because the Code Page may be loaded outside Dataverse
 * (e.g., during local development or testing).
 */
interface Window {
    Xrm?: XrmNamespace;
}
