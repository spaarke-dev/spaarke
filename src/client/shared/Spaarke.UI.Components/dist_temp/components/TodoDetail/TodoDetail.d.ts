/**
 * TodoDetail — Shared content component for the To Do Detail side pane.
 *
 * Layout (top to bottom):
 *   1. Description (editable, auto-expands, no scroll)
 *   2. Details: Record Type, Record link, Due Date, Assigned To
 *   3. To Do Notes (editable, auto-expands, no scroll) — from sprk_eventtodo
 *   4. To Do Score section (Priority, Effort, Urgency sliders)
 *   5. Sticky footer: Remove, Save, Completed buttons
 *
 * Data spans TWO entities:
 *   - sprk_event: description, due date, scores, lookups
 *   - sprk_eventtodo: notes, completed flag/date, statuscode
 *
 * Context-agnostic (ADR-012): No Xrm, no PCF APIs.
 * All external I/O is via callback props.
 * All colours from Fluent UI v9 semantic tokens (ADR-021).
 */
import * as React from "react";
import type { ITodoRecord, ITodoExtension, IEventFieldUpdates, ITodoExtensionUpdates, IContactOption } from "./types";
export interface ITodoDetailProps {
    record: ITodoRecord | null;
    /** sprk_eventtodo extension record (notes, completed, statuscode). */
    todoExtension: ITodoExtension | null;
    isLoading: boolean;
    error: string | null;
    /** Save event fields (sprk_event). */
    onSaveEventFields: (eventId: string, fields: IEventFieldUpdates) => Promise<{
        success: boolean;
        error?: string;
    }>;
    /** Save todo extension fields (sprk_eventtodo). */
    onSaveTodoExtFields: (todoId: string, fields: ITodoExtensionUpdates) => Promise<{
        success: boolean;
        error?: string;
    }>;
    /** Deactivate sprk_eventtodo (statecode=1, statuscode=2) via direct REST API. */
    onDeactivateTodoExt: (todoId: string) => Promise<{
        success: boolean;
        error?: string;
    }>;
    /** Remove from To Do (sets sprk_todoflag=false, then closes pane). */
    onRemoveTodo?: (eventId: string) => Promise<void>;
    /** Close the side pane. */
    onClose?: () => void;
    /**
     * Search contacts by name for the Assigned To picker.
     * Decoupled from Xrm — host provides the implementation (ADR-012).
     */
    onSearchContacts: (query: string) => Promise<IContactOption[]>;
    /**
     * Open the regarding record (matter/project) in a new tab/window.
     * Decoupled from Xrm — host provides the navigation implementation (ADR-012).
     * Called with the entity logical name and record ID.
     */
    onOpenRegardingRecord?: (entityName: string, recordId: string) => void;
}
export declare const TodoDetail: React.FC<ITodoDetailProps>;
//# sourceMappingURL=TodoDetail.d.ts.map