/**
 * Mock module for @spaarke/ui-components (barrel import)
 *
 * Provides mock implementations for components imported from the shared library.
 * Used by jest moduleNameMapper to replace the real library in tests.
 */
import React from "react";

// Mock RichTextEditor as a simple div that calls onChange
export const RichTextEditor = React.forwardRef(function MockRichTextEditor(
    props: {
        value?: string;
        onChange?: (html: string) => void;
        readOnly?: boolean;
        placeholder?: string;
    },
    ref: React.Ref<unknown>
) {
    React.useImperativeHandle(ref, () => ({
        focus: jest.fn(),
        getHtml: () => props.value ?? "",
        setHtml: jest.fn(),
        clear: jest.fn(),
        beginStreamingInsert: jest.fn(),
        appendStreamToken: jest.fn(),
        endStreamingInsert: jest.fn(),
    }));

    return React.createElement("div", {
        "data-testid": "mock-rich-text-editor",
        "data-value": props.value ?? "",
        "data-readonly": String(props.readOnly ?? false),
    }, props.placeholder ?? "");
});

// Re-export type (mock stub)
export type RichTextEditorRef = {
    focus: () => void;
    getHtml: () => string;
    setHtml: (html: string) => void;
    clear: () => void;
    beginStreamingInsert: (position: string) => unknown;
    appendStreamToken: (handle: unknown, token: string) => void;
    endStreamingInsert: (handle: unknown) => void;
};

// Mock DiffCompareView for DiffReviewPanel tests
export function DiffCompareView(props: {
    originalText: string;
    proposedText: string;
    htmlMode?: boolean;
    mode?: string;
    onAccept: (text: string) => void;
    onReject: () => void;
    onEdit?: (text: string) => void;
    title?: string;
    ariaLabel?: string;
}) {
    return React.createElement("div", {
        "data-testid": "mock-diff-compare-view",
        "data-original": props.originalText,
        "data-proposed": props.proposedText,
    }, [
        React.createElement("button", {
            key: "accept",
            "data-testid": "diff-accept-button",
            onClick: () => props.onAccept(props.proposedText),
        }, "Accept"),
        React.createElement("button", {
            key: "reject",
            "data-testid": "diff-reject-button",
            onClick: () => props.onReject(),
        }, "Reject"),
        props.onEdit ? React.createElement("button", {
            key: "edit",
            "data-testid": "diff-edit-button",
            onClick: () => props.onEdit!(props.proposedText + " (edited)"),
        }, "Edit") : null,
    ]);
}
