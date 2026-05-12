/**
 * SprkChatMessageRenderer - Structured response card renderer for SprkChat
 *
 * Selects the appropriate card renderer based on `responseType` and renders
 * structured response data. Supports five card types:
 * - markdown (default): rendered as formatted text
 * - citations: text with clickable citation references + source list
 * - diff: summary with "Open in Diff Viewer" action button
 * - entity_card: Dataverse record link with key fields and navigation
 * - action_confirmation: completed action summary with status badge
 *
 * Navigation for entity cards is handled via the onNavigate callback —
 * this component MUST NOT call Xrm.Navigation directly (ADR-012).
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode required
 * @see ADR-012 - Shared Component Library; no Xrm/ComponentFramework imports
 */
import * as React from 'react';
import type { CitationSourceType } from './types';
/** A single citation reference within a response. */
export interface ICitationRef {
    /** 1-based index matching [N] markers in response text */
    index: number;
    /** Display title of the source document or web page */
    title: string;
    /** Dataverse document GUID for linking (optional for web citations) */
    documentId?: string;
    /**
     * Citation source type discriminator.
     * - 'document' (default) — internal SPE document reference
     * - 'web' — external web search result
     * When absent, defaults to 'document' for backward compatibility.
     */
    sourceType?: CitationSourceType;
    /** Full URL of the web search result. Present when sourceType is 'web'. */
    url?: string;
}
/** Response data for markdown card type. */
export interface IMarkdownResponse {
    /** The text content (plain text or simple markdown) */
    text: string;
}
/** Response data for citations card type. */
export interface ICitationsResponse {
    /** The AI response text with [N] citation markers */
    text: string;
    /** Ordered list of citation references */
    citations: ICitationRef[];
}
/** Response data for diff card type. */
export interface IDiffResponse {
    /** Human-readable summary of proposed changes */
    summary: string;
    /** The proposed revised text passed to onOpenDiff */
    proposedText: string;
}
/** A key-value field on an entity card. */
export interface IEntityCardField {
    /** Field label for display */
    label: string;
    /** Field value string */
    value: string;
}
/** Response data for entity_card card type. */
export interface IEntityCardResponse {
    /** Display name of the entity record */
    entityName: string;
    /** Entity logical name (e.g., "matter", "contact") */
    entityType: string;
    /** GUID of the entity record */
    entityId: string;
    /** Optional ordered list of key fields to display */
    fields?: IEntityCardField[];
}
/** Response data for action_confirmation card type. */
export interface IActionConfirmationResponse {
    /** Name/label of the action that was completed */
    actionName: string;
    /** Whether the action succeeded or failed */
    status: 'success' | 'failure';
    /** Human-readable summary of what was done */
    summary: string;
}
/** Union of all structured response data types. */
export type StructuredResponseData = IMarkdownResponse | ICitationsResponse | IDiffResponse | IEntityCardResponse | IActionConfirmationResponse;
/** Props for the SprkChatMessageRenderer component. */
export interface ISprkChatMessageRendererProps {
    /** Discriminates which card renderer to use */
    responseType: 'markdown' | 'citations' | 'diff' | 'entity_card' | 'action_confirmation' | string;
    /** Structured data for the selected card renderer */
    data: StructuredResponseData;
    /**
     * Callback for entity card navigation.
     * MUST NOT call Xrm.Navigation directly — delegate to Code Page layer.
     */
    onNavigate?: (entityType: string, entityId: string) => void;
    /** Callback for diff card — receives the proposed text to open in diff viewer */
    onOpenDiff?: (proposedText: string) => void;
}
/**
 * SprkChatMessageRenderer - Renders structured AI response cards.
 *
 * Selects a card renderer based on `responseType`. Unknown types fall back to
 * the markdown renderer without errors.
 *
 * @example
 * ```tsx
 * <SprkChatMessageRenderer
 *   responseType="diff"
 *   data={{ summary: "Simplified 3 sentences", proposedText: "..." }}
 *   onOpenDiff={(text) => setDiffText(text)}
 * />
 *
 * <SprkChatMessageRenderer
 *   responseType="entity_card"
 *   data={{ entityName: "Smith v. Jones", entityType: "matter", entityId: "abc-123", fields: [] }}
 *   onNavigate={(type, id) => navigateToRecord(type, id)}
 * />
 * ```
 */
export declare const SprkChatMessageRenderer: React.FC<ISprkChatMessageRendererProps>;
export default SprkChatMessageRenderer;
//# sourceMappingURL=SprkChatMessageRenderer.d.ts.map