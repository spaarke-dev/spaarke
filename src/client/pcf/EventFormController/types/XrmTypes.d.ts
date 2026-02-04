/**
 * Xrm type declarations for form context access
 */

interface XrmAttribute {
    getValue(): Array<{ id: string; name: string; entityType: string }> | null;
    addOnChange(handler: () => void): void;
    removeOnChange(handler: () => void): void;
}

interface XrmPage {
    getAttribute(name: string): XrmAttribute | null;
}

interface XrmGlobal {
    Page?: XrmPage;
}

interface Window {
    Xrm?: XrmGlobal;
}
