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
import { makeStyles, tokens, Text, Textarea, Input, Slider, Combobox, Option, Button, Badge, Link, Popover, PopoverTrigger, PopoverSurface, Spinner, MessageBar, MessageBarBody, } from "@fluentui/react-components";
import { SaveRegular, InfoRegular, DeleteRegular, CheckmarkRegular, OpenRegular, } from "@fluentui/react-icons";
// ---------------------------------------------------------------------------
// To Do Score computation (self-contained — no cross-solution imports)
// ---------------------------------------------------------------------------
/**
 * Compute To Do Score — mirrors LegalWorkspace computeTodoScore() exactly.
 *
 * Formula: priority*0.50 + invertedEffort*0.20 + urgencyRaw*0.30
 * Uses Math.ceil for diffDays and Math.round for the final score
 * to match the Kanban card computation.
 */
function computeScore(priority, effort, duedate) {
    const invertedEffort = 100 - effort;
    let urgencyRaw = 0;
    if (duedate) {
        const due = new Date(duedate);
        if (!isNaN(due.getTime())) {
            const now = new Date();
            const diffMs = due.getTime() - now.getTime();
            const diffDays = Math.ceil(diffMs / (1000 * 60 * 60 * 24));
            if (diffDays < 0)
                urgencyRaw = 100;
            else if (diffDays <= 3)
                urgencyRaw = 80;
            else if (diffDays <= 7)
                urgencyRaw = 50;
            else if (diffDays <= 10)
                urgencyRaw = 25;
        }
    }
    const priorityComponent = priority * 0.5;
    const effortComponent = invertedEffort * 0.2;
    const urgencyComponent = urgencyRaw * 0.3;
    const raw = priorityComponent + effortComponent + urgencyComponent;
    const todoScore = Math.max(0, Math.min(100, Math.round(raw)));
    return { todoScore, priorityComponent, effortComponent, urgencyRaw, urgencyComponent };
}
/** Convert ISO date string to YYYY-MM-DD for input[type="date"]. */
function toDateInputValue(dateStr) {
    if (!dateStr)
        return "";
    const d = new Date(dateStr);
    if (isNaN(d.getTime()))
        return "";
    return d.toISOString().split("T")[0];
}
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        overflow: "hidden",
    },
    content: {
        flex: "1 1 0",
        overflowY: "auto",
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalL,
        display: "flex",
        flexDirection: "column",
        gap: "0px",
    },
    divider: {
        height: "1px",
        backgroundColor: tokens.colorNeutralStroke2,
        flexShrink: 0,
        marginTop: "25px",
        marginBottom: "25px",
    },
    section: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    sectionTitleRow: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    sectionTitle: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    fieldRow: {
        display: "flex",
        flexDirection: "column",
        gap: "2px",
    },
    fieldLabel: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
    },
    sliderRow: {
        display: "flex",
        flexDirection: "column",
        gap: "2px",
    },
    sliderLabelRow: {
        display: "flex",
        flexDirection: "row",
        justifyContent: "space-between",
        alignItems: "center",
    },
    sliderValue: {
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        minWidth: "24px",
        textAlign: "right",
    },
    scoreCircle: {
        width: "36px",
        height: "36px",
        borderRadius: "50%",
        backgroundColor: tokens.colorBrandBackground,
        color: tokens.colorNeutralForegroundOnBrand,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontWeight: tokens.fontWeightBold,
        fontSize: tokens.fontSizeBase300,
        flexShrink: 0,
    },
    infoPopover: {
        maxWidth: "320px",
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
    },
    infoSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
    },
    infoSectionTitle: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    infoSectionBody: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
    },
    footer: {
        display: "flex",
        justifyContent: "flex-end",
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalL,
        borderTopWidth: "1px",
        borderTopStyle: "solid",
        borderTopColor: tokens.colorNeutralStroke2,
        flexShrink: 0,
    },
    emptyState: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        flex: "1 1 0",
        color: tokens.colorNeutralForeground4,
        paddingTop: tokens.spacingVerticalXXXL,
    },
    loadingState: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        flex: "1 1 0",
        paddingTop: tokens.spacingVerticalXXXL,
    },
    errorBanner: {
        flexShrink: 0,
    },
    assignedToDisplay: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    assignedToName: {
        flex: "1 1 0",
        color: tokens.colorNeutralForeground1,
        fontSize: tokens.fontSizeBase300,
    },
    recordLink: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        cursor: "pointer",
    },
    completeBtn: {
        backgroundColor: tokens.colorPaletteYellowBackground3,
        color: tokens.colorNeutralForeground1,
        ":hover": {
            backgroundColor: tokens.colorPaletteYellowForeground2,
        },
    },
    completedBtn: {
        backgroundColor: tokens.colorPaletteGreenBackground3,
        color: tokens.colorNeutralForegroundOnBrand,
        ":hover": {
            backgroundColor: tokens.colorPaletteGreenForeground2,
        },
    },
});
/** Map record type display name to Dataverse entity logical name for navigation. */
const RECORD_TYPE_ENTITY_MAP = {
    Matter: "sprk_matter",
    Project: "sprk_project",
};
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const TodoDetail = React.memo(({ record, todoExtension, isLoading, error, onSaveEventFields, onSaveTodoExtFields, onDeactivateTodoExt, onRemoveTodo, onClose, onSearchContacts, onOpenRegardingRecord, }) => {
    const styles = useStyles();
    // Auto-expand textarea refs
    const textareaRef = React.useRef(null);
    const notesTextareaRef = React.useRef(null);
    // Editable field values (sprk_event fields)
    const [description, setDescription] = React.useState("");
    const [dueDate, setDueDate] = React.useState("");
    const [priority, setPriority] = React.useState(50);
    const [effort, setEffort] = React.useState(50);
    // Editable field value (sprk_eventtodo field)
    const [toDoNotes, setToDoNotes] = React.useState("");
    // Assigned To state
    const [assignedToId, setAssignedToId] = React.useState(null);
    const [assignedToName, setAssignedToName] = React.useState("");
    const [contactQuery, setContactQuery] = React.useState("");
    const [contactOptions, setContactOptions] = React.useState([]);
    const [isSearching, setIsSearching] = React.useState(false);
    const [isEditingAssignedTo, setIsEditingAssignedTo] = React.useState(false);
    // Save state
    const [isSaving, setIsSaving] = React.useState(false);
    const [isRemoving, setIsRemoving] = React.useState(false);
    const [isCompleting, setIsCompleting] = React.useState(false);
    const [saveError, setSaveError] = React.useState(null);
    // Snapshot of original values (for dirty detection)
    const origRef = React.useRef({
        description: "",
        dueDate: "",
        priority: 50,
        effort: 50,
        assignedToId: null,
        toDoNotes: "",
    });
    // Reset when record changes
    React.useEffect(() => {
        if (record) {
            const desc = record.sprk_description ?? "";
            const dd = toDateInputValue(record.sprk_duedate);
            const pri = record.sprk_priorityscore ?? 50;
            const eff = record.sprk_effortscore ?? 50;
            const aId = record._sprk_assignedto_value ?? null;
            const aName = record["_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue"] ?? "";
            setDescription(desc);
            setDueDate(dd);
            setPriority(pri);
            setEffort(eff);
            setAssignedToId(aId);
            setAssignedToName(aName);
            setContactQuery("");
            setContactOptions([]);
            setIsEditingAssignedTo(false);
            setSaveError(null);
            origRef.current = {
                ...origRef.current,
                description: desc,
                dueDate: dd,
                priority: pri,
                effort: eff,
                assignedToId: aId,
            };
        }
    }, [record?.sprk_eventid]); // eslint-disable-line react-hooks/exhaustive-deps
    // Reset notes when todoExtension changes
    React.useEffect(() => {
        const notes = todoExtension?.sprk_todonotes ?? "";
        setToDoNotes(notes);
        origRef.current = { ...origRef.current, toDoNotes: notes };
    }, [todoExtension?.sprk_eventtodoid]); // eslint-disable-line react-hooks/exhaustive-deps
    // Dirty detection
    const isEventDirty = description !== origRef.current.description ||
        dueDate !== origRef.current.dueDate ||
        priority !== origRef.current.priority ||
        effort !== origRef.current.effort ||
        assignedToId !== origRef.current.assignedToId;
    const isNotesDirty = toDoNotes !== origRef.current.toDoNotes;
    const isDirty = isEventDirty || isNotesDirty;
    // --- Handlers ---
    const handleDescriptionChange = React.useCallback((_ev, data) => {
        setDescription(data.value);
        requestAnimationFrame(() => {
            const el = textareaRef.current;
            if (!el)
                return;
            el.style.height = "auto";
            el.style.height = `${el.scrollHeight}px`;
            el.style.overflowY = "hidden";
        });
    }, []);
    // Auto-resize description textarea on initial load
    React.useEffect(() => {
        const el = textareaRef.current;
        if (!el)
            return;
        el.style.height = "auto";
        el.style.height = `${el.scrollHeight}px`;
        el.style.overflowY = "hidden";
    }, [description]);
    const handleNotesChange = React.useCallback((_ev, data) => {
        setToDoNotes(data.value);
        requestAnimationFrame(() => {
            const el = notesTextareaRef.current;
            if (!el)
                return;
            el.style.height = "auto";
            el.style.height = `${el.scrollHeight}px`;
            el.style.overflowY = "hidden";
        });
    }, []);
    // Auto-resize notes textarea on initial load
    React.useEffect(() => {
        const el = notesTextareaRef.current;
        if (!el)
            return;
        el.style.height = "auto";
        el.style.height = `${el.scrollHeight}px`;
        el.style.overflowY = "hidden";
    }, [toDoNotes]);
    const handleDueDateChange = React.useCallback((ev) => setDueDate(ev.target.value), []);
    const handlePriorityChange = React.useCallback((_ev, data) => {
        setPriority(data.value);
    }, []);
    const handleEffortChange = React.useCallback((_ev, data) => {
        setEffort(data.value);
    }, []);
    // Debounced contact search (uses onSearchContacts callback prop)
    const searchTimerRef = React.useRef();
    const handleContactInput = React.useCallback((ev) => {
        const q = ev.target.value;
        setContactQuery(q);
        clearTimeout(searchTimerRef.current);
        if (q.length < 2) {
            setContactOptions([]);
            return;
        }
        setIsSearching(true);
        searchTimerRef.current = setTimeout(async () => {
            const results = await onSearchContacts(q);
            setContactOptions(results);
            setIsSearching(false);
        }, 300);
    }, [onSearchContacts]);
    const handleContactSelect = React.useCallback((_ev, data) => {
        if (data.optionValue && data.optionText) {
            setAssignedToId(data.optionValue);
            setAssignedToName(data.optionText);
            setContactQuery("");
            setContactOptions([]);
            setIsEditingAssignedTo(false);
        }
    }, []);
    // Save dirty fields to the correct entities
    const handleSave = React.useCallback(async () => {
        if (!record || !isDirty)
            return;
        setIsSaving(true);
        setSaveError(null);
        try {
            // Save event fields if any changed
            if (isEventDirty) {
                const eventUpdates = {};
                if (description !== origRef.current.description) {
                    eventUpdates.sprk_description = description;
                }
                if (dueDate !== origRef.current.dueDate) {
                    eventUpdates.sprk_duedate = dueDate || null;
                }
                if (priority !== origRef.current.priority) {
                    eventUpdates.sprk_priorityscore = priority;
                }
                if (effort !== origRef.current.effort) {
                    eventUpdates.sprk_effortscore = effort;
                }
                if (assignedToId !== origRef.current.assignedToId) {
                    eventUpdates["sprk_AssignedTo@odata.bind"] = assignedToId
                        ? `/contacts(${assignedToId})`
                        : null;
                }
                const eventResult = await onSaveEventFields(record.sprk_eventid, eventUpdates);
                if (!eventResult.success) {
                    setSaveError(eventResult.error ?? "Failed to save event fields");
                    setIsSaving(false);
                    return;
                }
            }
            // Save notes if changed (requires todoExtension record)
            if (isNotesDirty && todoExtension?.sprk_eventtodoid) {
                const extUpdates = {
                    sprk_todonotes: toDoNotes,
                };
                const extResult = await onSaveTodoExtFields(todoExtension.sprk_eventtodoid, extUpdates);
                if (!extResult.success) {
                    setSaveError(extResult.error ?? "Failed to save notes");
                    setIsSaving(false);
                    return;
                }
            }
            // Update snapshots on success
            origRef.current = {
                description,
                dueDate,
                priority,
                effort,
                assignedToId,
                toDoNotes,
            };
        }
        catch {
            setSaveError("Save failed — unexpected error");
        }
        finally {
            setIsSaving(false);
        }
    }, [
        record,
        todoExtension,
        isDirty,
        isEventDirty,
        isNotesDirty,
        description,
        dueDate,
        priority,
        effort,
        assignedToId,
        toDoNotes,
        onSaveEventFields,
        onSaveTodoExtFields,
    ]);
    // Remove from To Do: sets sprk_todoflag = false, notifies Kanban, closes pane
    const handleRemoveTodo = React.useCallback(async () => {
        if (!record || !onRemoveTodo)
            return;
        setIsRemoving(true);
        setSaveError(null);
        try {
            await onRemoveTodo(record.sprk_eventid);
        }
        catch {
            setSaveError("Failed to remove from To Do");
            setIsRemoving(false);
        }
    }, [record, onRemoveTodo]);
    // Completed: saves dirty fields + marks sprk_eventtodo as completed
    const handleCompleted = React.useCallback(async () => {
        if (!record)
            return;
        setIsCompleting(true);
        setSaveError(null);
        try {
            // Save any dirty event fields first
            if (isEventDirty) {
                const eventUpdates = {};
                if (description !== origRef.current.description) {
                    eventUpdates.sprk_description = description;
                }
                if (dueDate !== origRef.current.dueDate) {
                    eventUpdates.sprk_duedate = dueDate || null;
                }
                if (priority !== origRef.current.priority) {
                    eventUpdates.sprk_priorityscore = priority;
                }
                if (effort !== origRef.current.effort) {
                    eventUpdates.sprk_effortscore = effort;
                }
                if (assignedToId !== origRef.current.assignedToId) {
                    eventUpdates["sprk_AssignedTo@odata.bind"] = assignedToId
                        ? `/contacts(${assignedToId})`
                        : null;
                }
                const eventResult = await onSaveEventFields(record.sprk_eventid, eventUpdates);
                if (!eventResult.success) {
                    setSaveError(eventResult.error ?? "Failed to save event fields");
                    setIsCompleting(false);
                    return;
                }
            }
            // Mark as completed on sprk_eventtodo — TWO separate calls:
            // 1) Data fields via callback  2) State change via deactivate callback
            if (todoExtension?.sprk_eventtodoid) {
                // 1) Save data fields (completed flag, date, notes) while record is still active
                const dataUpdates = {
                    sprk_completed: true,
                    sprk_completeddate: new Date().toISOString(),
                };
                if (isNotesDirty) {
                    dataUpdates.sprk_todonotes = toDoNotes;
                }
                const dataResult = await onSaveTodoExtFields(todoExtension.sprk_eventtodoid, dataUpdates);
                if (!dataResult.success) {
                    setSaveError(dataResult.error ?? "Failed to save completion data");
                    setIsCompleting(false);
                    return;
                }
                // 2) Deactivate via callback
                const stateResult = await onDeactivateTodoExt(todoExtension.sprk_eventtodoid);
                if (!stateResult.success) {
                    setSaveError(stateResult.error ?? "Failed to deactivate record");
                    setIsCompleting(false);
                    return;
                }
            }
        }
        catch {
            setSaveError("Failed to mark as completed — unexpected error");
        }
        finally {
            setIsCompleting(false);
        }
    }, [
        record,
        todoExtension,
        isEventDirty,
        isNotesDirty,
        description,
        dueDate,
        priority,
        effort,
        assignedToId,
        toDoNotes,
        onSaveEventFields,
        onSaveTodoExtFields,
        onDeactivateTodoExt,
    ]);
    // Open regarding record — delegates to host via callback prop
    const handleOpenRegardingRecord = React.useCallback(() => {
        if (!record?.sprk_regardingrecordid || !onOpenRegardingRecord)
            return;
        const typeName = record["_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue"] ?? "";
        const entityName = RECORD_TYPE_ENTITY_MAP[typeName];
        if (!entityName)
            return;
        onOpenRegardingRecord(entityName, record.sprk_regardingrecordid);
    }, [record, onOpenRegardingRecord]);
    // --- Render states ---
    if (isLoading) {
        return (React.createElement("div", { className: styles.loadingState },
            React.createElement(Spinner, { size: "medium", label: "Loading..." })));
    }
    if (error) {
        return (React.createElement("div", { className: styles.emptyState },
            React.createElement(Text, null, error)));
    }
    if (!record) {
        return (React.createElement("div", { className: styles.emptyState },
            React.createElement(Text, null, "No event selected")));
    }
    // Compute score from CURRENT field values (live preview)
    const score = computeScore(priority, effort, dueDate || record.sprk_duedate);
    return (React.createElement("div", { className: styles.container },
        React.createElement("div", { className: styles.content },
            saveError && (React.createElement(MessageBar, { intent: "error", className: styles.errorBanner },
                React.createElement(MessageBarBody, null, saveError))),
            React.createElement("div", { className: styles.section },
                React.createElement(Text, { className: styles.sectionTitle, size: 300 }, "Description"),
                React.createElement(Textarea, { value: description, onChange: handleDescriptionChange, placeholder: "Add a description...", resize: "none", textarea: {
                        ref: textareaRef,
                        style: { minHeight: "160px" },
                    } })),
            React.createElement("div", { className: styles.divider, role: "separator" }),
            React.createElement("div", { className: styles.section },
                React.createElement(Text, { className: styles.sectionTitle, size: 300 }, "Details"),
                record["_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue"] && (React.createElement("div", { className: styles.fieldRow },
                    React.createElement("label", { className: styles.fieldLabel }, "Record Type"),
                    React.createElement("div", null,
                        React.createElement(Badge, { appearance: "filled", color: "informative", size: "medium" }, record["_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue"])))),
                record.sprk_regardingrecordname && record.sprk_regardingrecordid && (React.createElement("div", { className: styles.fieldRow },
                    React.createElement("label", { className: styles.fieldLabel }, "Record"),
                    React.createElement(Link, { className: styles.recordLink, onClick: handleOpenRegardingRecord, as: "button" },
                        record.sprk_regardingrecordname,
                        React.createElement(OpenRegular, { style: { fontSize: "12px" } })))),
                React.createElement("div", { className: styles.fieldRow },
                    React.createElement("label", { className: styles.fieldLabel }, "Due Date"),
                    React.createElement(Input, { type: "date", value: dueDate, onChange: handleDueDateChange })),
                React.createElement("div", { className: styles.fieldRow },
                    React.createElement("label", { className: styles.fieldLabel }, "Assigned To"),
                    assignedToName && !isEditingAssignedTo ? (React.createElement("div", { className: styles.assignedToDisplay },
                        React.createElement(Text, { className: styles.assignedToName }, assignedToName),
                        React.createElement(Button, { appearance: "subtle", size: "small", onClick: () => setIsEditingAssignedTo(true) }, "Change"))) : (React.createElement(Combobox, { freeform: true, placeholder: "Search contacts...", value: contactQuery, onInput: handleContactInput, onOptionSelect: handleContactSelect, selectedOptions: assignedToId ? [assignedToId] : [] },
                        isSearching && (React.createElement(Option, { key: "__loading", value: "", text: "", disabled: true }, "Searching...")),
                        !isSearching && contactOptions.length === 0 && contactQuery.length >= 2 && (React.createElement(Option, { key: "__empty", value: "", text: "", disabled: true }, "No contacts found")),
                        contactOptions.map((c) => (React.createElement(Option, { key: c.id, value: c.id, text: c.name }, c.name))))))),
            React.createElement("div", { className: styles.divider, role: "separator" }),
            React.createElement("div", { className: styles.section },
                React.createElement(Text, { className: styles.sectionTitle, size: 300 }, "To Do Notes"),
                React.createElement(Textarea, { value: toDoNotes, onChange: handleNotesChange, placeholder: "Add notes...", resize: "none", textarea: {
                        ref: notesTextareaRef,
                        style: { minHeight: "160px" },
                    } })),
            React.createElement("div", { className: styles.divider, role: "separator" }),
            React.createElement("div", { className: styles.section, style: { marginBottom: "20px" } },
                React.createElement("div", { className: styles.sectionTitleRow },
                    React.createElement(Text, { className: styles.sectionTitle, size: 300 }, "To Do Score"),
                    React.createElement(Popover, { withArrow: true },
                        React.createElement(PopoverTrigger, { disableButtonEnhancement: true },
                            React.createElement(Button, { appearance: "subtle", size: "small", icon: React.createElement(InfoRegular, null), "aria-label": "Score information" })),
                        React.createElement(PopoverSurface, null,
                            React.createElement("div", { className: styles.infoPopover },
                                React.createElement("div", { className: styles.infoSection },
                                    React.createElement(Text, { className: styles.infoSectionTitle, size: 300 }, "How Scoring Works"),
                                    React.createElement(Text, { className: styles.infoSectionBody }, "The To Do Score combines three factors into a single 0-100 number. Higher scores surface more important items first in the Kanban board.")),
                                React.createElement("div", { className: styles.infoSection },
                                    React.createElement(Text, { className: styles.infoSectionTitle, size: 300 }, "Score Formula"),
                                    React.createElement(Text, { className: styles.infoSectionBody }, "Score = Priority (50%) + Inverted Effort (20%) + Urgency (30%). Lower effort items score higher (quick wins bubble up).")),
                                React.createElement("div", { className: styles.infoSection },
                                    React.createElement(Text, { className: styles.infoSectionTitle, size: 300 }, "Urgency Score"),
                                    React.createElement(Text, { className: styles.infoSectionBody }, "Auto-calculated from due date: Overdue = 100, within 3 days = 80, within 7 days = 50, within 10 days = 25, more than 10 days = 0."))))),
                    React.createElement("div", { className: styles.scoreCircle, style: { marginLeft: "auto" } }, Math.round(score.todoScore))),
                React.createElement("div", { className: styles.sliderRow },
                    React.createElement("div", { className: styles.sliderLabelRow },
                        React.createElement("label", { className: styles.fieldLabel }, "Priority (50%)"),
                        React.createElement("span", { className: styles.sliderValue }, priority)),
                    React.createElement(Slider, { value: priority, onChange: handlePriorityChange, min: 0, max: 100, step: 5 })),
                React.createElement("div", { className: styles.sliderRow },
                    React.createElement("div", { className: styles.sliderLabelRow },
                        React.createElement("label", { className: styles.fieldLabel }, "Effort (20%)"),
                        React.createElement("span", { className: styles.sliderValue }, effort)),
                    React.createElement(Slider, { value: effort, onChange: handleEffortChange, min: 0, max: 100, step: 5 })))),
        React.createElement("div", { className: styles.footer },
            onRemoveTodo && (React.createElement(Button, { appearance: "subtle", icon: React.createElement(DeleteRegular, null), onClick: handleRemoveTodo, disabled: isRemoving || isSaving || isCompleting, style: { color: tokens.colorPaletteRedForeground1, marginRight: "auto" } }, isRemoving ? "Removing..." : "Remove")),
            React.createElement(Button, { appearance: "primary", icon: React.createElement(SaveRegular, null), onClick: handleSave, disabled: !isDirty || isSaving || isCompleting }, isSaving ? "Saving..." : "Save"),
            todoExtension?.statecode === 1 || todoExtension?.statuscode === 2 ? (React.createElement(Button, { icon: React.createElement(CheckmarkRegular, null), disabled: true, className: styles.completedBtn }, "Completed")) : (React.createElement(Button, { icon: React.createElement(CheckmarkRegular, null), onClick: handleCompleted, disabled: isSaving || isCompleting, className: styles.completeBtn }, isCompleting ? "Completing..." : "Complete")))));
});
TodoDetail.displayName = "TodoDetail";
//# sourceMappingURL=TodoDetail.js.map