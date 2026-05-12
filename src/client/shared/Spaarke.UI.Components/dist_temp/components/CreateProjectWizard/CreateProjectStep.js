/**
 * CreateProjectStep.tsx
 * Form step for the "Create New Project" wizard — 2-column grid with lookup fields.
 *
 * Mirrors CreateMatter/CreateRecordStep with AI pre-fill support:
 *   - On mount, sends uploaded files to BFF /workspace/projects/pre-fill
 *   - AI-extracted display names are fuzzy-matched against Dataverse lookups
 *   - Pre-filled fields show "AI" badge via AiFieldTag
 *   - Skeleton loading state while AI is processing
 *
 * Layout (CSS Grid):
 *   +---------------------------+------------------------------+
 *   |  Project Type (lookup)    |  Practice Area (lookup)       |
 *   +---------------------------+------------------------------+
 *   |  Project Name (Input, full-width) *                       |
 *   +---------------------------+------------------------------+
 *   |  Project Description (Textarea, full-width, optional)     |
 *   +----------------------------------------------------------+
 *
 * Form validation:
 *   Required: Project Name (non-empty after trim)
 *   -> `onValidChange(true)` emitted when projectName has a value
 *
 * Constraints:
 *   - Fluent v9 only: Input, Textarea, Field, Text, Skeleton, Badge
 *   - makeStyles with semantic tokens — ZERO hardcoded colours
 *   - Supports light, dark, and high-contrast modes
 */
import * as React from 'react';
import { Badge, Field, Input, Skeleton, SkeletonItem, Text, Textarea, makeStyles, tokens, } from '@fluentui/react-components';
import { EMPTY_PROJECT_FORM, } from './projectFormTypes';
import { ProjectService } from './projectService';
import { DataverseLookupField } from '../LookupField';
import { AiFieldTag } from '../AiFieldTag';
import { SecureProjectSection } from './SecureProjectSection';
import { useAiPrefill } from '../../hooks/useAiPrefill';
// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------
const PREFILL_PATH = '/api/workspace/projects/pre-fill';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
    },
    // ── Step header ──────────────────────────────────────────────────────────
    headerRow: {
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'space-between',
        gap: tokens.spacingHorizontalM,
    },
    headerText: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
    },
    // ── AI Pre-filled badge ───────────────────────────────────────────────────
    aiBadge: {
        flexShrink: 0,
        marginTop: tokens.spacingVerticalXS,
    },
    // ── 2-column grid ─────────────────────────────────────────────────────────
    formGrid: {
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        gap: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalL}`,
    },
    // Fields that should span both columns
    fullWidth: {
        gridColumn: '1 / -1',
    },
    // ── Skeleton items ────────────────────────────────────────────────────────
    skeletonField: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    skeletonLabel: {
        width: '120px',
        height: '16px',
    },
    skeletonInput: {
        width: '100%',
        height: '32px',
    },
    skeletonTextareaLg: {
        width: '100%',
        height: '100px',
    },
});
// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------
/** Skeleton placeholder for a single-line input field during AI loading. */
const FieldSkeleton = ({ large }) => {
    const styles = useStyles();
    return (React.createElement(Skeleton, { className: styles.skeletonField },
        React.createElement(SkeletonItem, { className: styles.skeletonLabel }),
        React.createElement(SkeletonItem, { className: large ? styles.skeletonTextareaLg : styles.skeletonInput })));
};
// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
function isFormValid(form) {
    return form.projectName.trim() !== '';
}
// ---------------------------------------------------------------------------
// CreateProjectStep (exported)
// ---------------------------------------------------------------------------
export const CreateProjectStep = ({ dataService, onValidChange, onFormValues, uploadedFiles = [], initialFormValues, authenticatedFetch: authFetch, bffBaseUrl, navigationService, }) => {
    const styles = useStyles();
    // ── Form state ──────────────────────────────────────────────────────────
    // When initialFormValues is provided with non-empty values (remount after
    // user navigated back), start from those to preserve user edits and
    // Assign Resources overrides. Otherwise start empty for first mount.
    const hasInitialValues = initialFormValues && initialFormValues.projectName.trim() !== '';
    const [formState, setFormState] = React.useState(() => hasInitialValues ? { ...initialFormValues } : { ...EMPTY_PROJECT_FORM });
    // ── AI pre-fill state ───────────────────────────────────────────────────
    const [aiState, setAiState] = React.useState({
        status: 'idle',
        prefilledFields: new Set(),
    });
    // ── Service ref (stable across re-renders) ─────────────────────────────
    const serviceRef = React.useRef(new ProjectService(dataService));
    React.useEffect(() => {
        serviceRef.current = new ProjectService(dataService);
    }, [dataService]);
    // ── Notify parent of validity changes ──────────────────────────────────
    const valid = isFormValid(formState);
    React.useEffect(() => {
        onValidChange(valid);
    }, [valid, onValidChange]);
    // ── Emit latest form values to parent on every change ──────────────────
    const onFormValuesRef = React.useRef(onFormValues);
    React.useEffect(() => {
        onFormValuesRef.current = onFormValues;
    }, [onFormValues]);
    React.useEffect(() => {
        onFormValuesRef.current(formState);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [formState]);
    // ── AI Pre-fill via shared hook ────────────────────────────────────────
    const handlePrefillApply = React.useCallback((resolved, _prefilledFieldNames) => {
        const updates = {};
        const pfFields = new Set();
        for (const [key, value] of Object.entries(resolved)) {
            if (typeof value === 'string') {
                updates[key] = value;
                pfFields.add(key);
            }
            else {
                // Lookup resolved: set both id and name fields
                const idKey = key.replace(/Name$/, 'Id');
                updates[idKey] = value.id;
                updates[key] = value.name;
                pfFields.add(idKey);
            }
        }
        if (Object.keys(updates).length > 0) {
            setFormState((prev) => ({ ...prev, ...updates }));
        }
        setAiState({ status: 'success', prefilledFields: pfFields });
    }, []);
    // Only run AI pre-fill if authenticatedFetch and bffBaseUrl are provided
    const prefill = useAiPrefill({
        endpoint: PREFILL_PATH,
        uploadedFiles,
        authenticatedFetch: authFetch ?? ((...args) => fetch(...args)),
        bffBaseUrl: bffBaseUrl ?? '',
        fieldExtractor: (data) => ({
            textFields: {
                projectName: data.projectName,
                description: data.description,
            },
            lookupFields: {
                projectTypeName: data.projectTypeName,
                practiceAreaName: data.practiceAreaName,
                assignedAttorneyName: data.assignedAttorneyName,
                assignedParalegalName: data.assignedParalegalName,
                assignedOutsideCounselName: data.assignedOutsideCounselName,
            },
        }),
        lookupResolvers: {
            projectTypeName: (v) => serviceRef.current.searchProjectTypes(v),
            practiceAreaName: (v) => serviceRef.current.searchPracticeAreas(v),
            assignedAttorneyName: (v) => serviceRef.current.searchContacts(v),
            assignedParalegalName: (v) => serviceRef.current.searchContacts(v),
            assignedOutsideCounselName: (v) => serviceRef.current.searchOrganizations(v),
        },
        onApply: handlePrefillApply,
        skipIfInitialized: !!hasInitialValues,
        logPrefix: 'CreateProject',
    });
    // Sync hook status with local AI state for loading/error display
    React.useEffect(() => {
        if (prefill.status === 'loading') {
            setAiState({ status: 'loading', prefilledFields: new Set() });
        }
        else if (prefill.status === 'error') {
            setAiState({ status: 'error', prefilledFields: new Set() });
        }
    }, [prefill.status]);
    // ── Lookup search callbacks (stable refs) ──────────────────────────────
    const handleSearchProjectTypes = React.useCallback((query) => serviceRef.current.searchProjectTypes(query), []);
    const handleSearchPracticeAreas = React.useCallback((query) => serviceRef.current.searchPracticeAreas(query), []);
    // ── Lookup change handlers ─────────────────────────────────────────────
    const handleProjectTypeChange = React.useCallback((item) => {
        setFormState((prev) => ({
            ...prev,
            projectTypeId: item?.id ?? '',
            projectTypeName: item?.name ?? '',
        }));
    }, []);
    const handlePracticeAreaChange = React.useCallback((item) => {
        setFormState((prev) => ({
            ...prev,
            practiceAreaId: item?.id ?? '',
            practiceAreaName: item?.name ?? '',
        }));
    }, []);
    // ── Text field change handlers ─────────────────────────────────────────
    const handleProjectNameChange = React.useCallback((e) => {
        setFormState((prev) => ({ ...prev, projectName: e.target.value }));
    }, []);
    const handleDescriptionChange = React.useCallback((e) => {
        setFormState((prev) => ({ ...prev, description: e.target.value }));
    }, []);
    const handleSecureChange = React.useCallback((value) => {
        setFormState((prev) => ({ ...prev, isSecure: value }));
    }, []);
    // ── Derived ───────────────────────────────────────────────────────────
    const isLoading = aiState.status === 'loading';
    const hasAnyPrefill = aiState.prefilledFields.size > 0;
    const isAiField = (field) => aiState.prefilledFields.has(field);
    // Build lookup value objects from form state
    const projectTypeValue = formState.projectTypeId
        ? { id: formState.projectTypeId, name: formState.projectTypeName }
        : null;
    const practiceAreaValue = formState.practiceAreaId
        ? { id: formState.practiceAreaId, name: formState.practiceAreaName }
        : null;
    // ── Render ─────────────────────────────────────────────────────────────
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.headerRow },
            React.createElement("div", { className: styles.headerText },
                React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Enter Info"),
                React.createElement(Text, { size: 200, className: styles.stepSubtitle }, isLoading
                    ? 'Analysing uploaded documents\u2026'
                    : 'Fill in the project details. Required fields are marked with *.')),
            hasAnyPrefill && (React.createElement(Badge, { className: styles.aiBadge, appearance: "tint", color: "brand", icon: React.createElement("span", { "aria-hidden": "true" }) }, "AI Pre-filled"))),
        !isLoading && (React.createElement(SecureProjectSection, { isSecure: formState.isSecure, onSecureChange: handleSecureChange })),
        isLoading ? (React.createElement("div", { className: styles.formGrid },
            React.createElement(FieldSkeleton, null),
            React.createElement(FieldSkeleton, null),
            React.createElement("div", { className: styles.fullWidth },
                React.createElement(FieldSkeleton, null)),
            React.createElement("div", { className: styles.fullWidth },
                React.createElement(FieldSkeleton, { large: true })))) : (React.createElement("div", { className: styles.formGrid },
            React.createElement(DataverseLookupField, { label: "Project Type", entityType: "sprk_projecttype_ref", value: projectTypeValue, onChange: handleProjectTypeChange, navigationService: navigationService, onSearch: handleSearchProjectTypes, placeholder: "Search project types...", labelExtra: isAiField('projectTypeId') ? React.createElement(AiFieldTag, null) : undefined, minSearchLength: 1 }),
            React.createElement(DataverseLookupField, { label: "Practice Area", entityType: "sprk_practicearea_ref", value: practiceAreaValue, onChange: handlePracticeAreaChange, navigationService: navigationService, onSearch: handleSearchPracticeAreas, placeholder: "Search practice areas...", labelExtra: isAiField('practiceAreaId') ? React.createElement(AiFieldTag, null) : undefined, minSearchLength: 1 }),
            React.createElement(Field, { className: styles.fullWidth, label: React.createElement("span", null,
                    "Project Name",
                    React.createElement("span", { "aria-hidden": "true", style: { color: tokens.colorPaletteRedForeground1 } }, ' *'),
                    isAiField('projectName') && React.createElement(AiFieldTag, null)), required: true },
                React.createElement(Input, { value: formState.projectName, onChange: handleProjectNameChange, placeholder: "Enter project name", "aria-label": "Project Name" })),
            React.createElement(Field, { className: styles.fullWidth, label: React.createElement("span", null,
                    "Project Description",
                    isAiField('description') && React.createElement(AiFieldTag, null)) },
                React.createElement(Textarea, { value: formState.description, onChange: handleDescriptionChange, placeholder: "Brief description of the project, its objectives, and scope", rows: 11, resize: "vertical", "aria-label": "Project Description", style: { width: '100%' } }))))));
};
export { CreateProjectStep as default };
//# sourceMappingURL=CreateProjectStep.js.map