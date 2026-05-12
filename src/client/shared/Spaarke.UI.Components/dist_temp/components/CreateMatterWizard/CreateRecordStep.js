/**
 * CreateRecordStep.tsx
 * Step 2 of the "Create New Matter" wizard -- 2-column form with lookup fields.
 *
 * Layout (CSS Grid):
 *   +---------------------------+------------------------------+
 *   |  Matter Type (lookup)     |  Practice Area (lookup)       |
 *   +---------------------------+------------------------------+
 *   |  Matter Name (Input, full-width) *                       |
 *   +---------------------------+------------------------------+
 *   |  Assigned Attorney (lookup)|  Assigned Paralegal (lookup) |
 *   +---------------------------+------------------------------+
 *   |  Summary (Textarea, full-width) + "Generate with AI"     |
 *   +----------------------------------------------------------+
 *
 * Lookup fields use LookupField component with debounced Dataverse search.
 * Summary has an AI generate button that calls BFF endpoint.
 *
 * Form validation:
 *   Required: Matter Type, Practice Area, Matter Name
 *   -> `onValidChange(true)` emitted when all three have values
 *
 * Constraints:
 *   - Fluent v9 only: Input, Textarea, Field, Label, Skeleton, Button, Spinner
 *   - makeStyles with semantic tokens -- ZERO hardcoded colours
 *   - Supports light, dark, and high-contrast modes
 */
import * as React from 'react';
import { Field, Input, Skeleton, SkeletonItem, Text, Badge, Textarea, Button, Spinner, makeStyles, tokens, } from '@fluentui/react-components';
import { SparkleRegular } from '@fluentui/react-icons';
import { AiFieldTag } from './AiFieldTag';
import { DataverseLookupField } from '../LookupField';
import { searchMatterTypes, searchPracticeAreas, searchContactsAsLookup, searchOrganizationsAsLookup, fetchAiDraftSummary, } from './matterService';
import { useAiPrefill } from '../../hooks/useAiPrefill';
// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------
const PREFILL_PATH = '/api/workspace/matters/pre-fill';
// ---------------------------------------------------------------------------
// Initial state factories
// ---------------------------------------------------------------------------
function buildInitialFormState() {
    return {
        matterTypeId: '',
        matterTypeName: '',
        practiceAreaId: '',
        practiceAreaName: '',
        matterName: '',
        assignedAttorneyId: '',
        assignedAttorneyName: '',
        assignedParalegalId: '',
        assignedParalegalName: '',
        assignedOutsideCounselId: '',
        assignedOutsideCounselName: '',
        summary: '',
    };
}
function buildInitialAiState() {
    return {
        status: 'idle',
        prefilledFields: new Set(),
    };
}
function combinedReducer(state, action) {
    switch (action.type) {
        case 'SET_FIELD': {
            return {
                ...state,
                form: { ...state.form, [action.field]: action.value },
            };
        }
        case 'SET_LOOKUP': {
            return {
                ...state,
                form: {
                    ...state.form,
                    [action.idField]: action.id,
                    [action.nameField]: action.name,
                },
            };
        }
        case 'APPLY_AI_PREFILL': {
            const fields = action.fields;
            const nextForm = { ...state.form };
            const prefilledFields = new Set();
            Object.keys(fields).forEach((key) => {
                const val = fields[key];
                if (val !== undefined && val !== '') {
                    nextForm[key] = val;
                    prefilledFields.add(key);
                }
            });
            return {
                form: nextForm,
                ai: {
                    ...state.ai,
                    prefilledFields,
                },
            };
        }
        case 'AI_PREFILL_LOADING': {
            return {
                ...state,
                ai: { status: 'loading', prefilledFields: new Set() },
            };
        }
        case 'AI_PREFILL_SUCCESS': {
            return {
                ...state,
                ai: { ...state.ai, status: 'success' },
            };
        }
        case 'AI_PREFILL_ERROR': {
            return {
                ...state,
                ai: { status: 'error', prefilledFields: new Set() },
            };
        }
        case 'CLEAR_ERRORS': {
            return state;
        }
        default:
            return state;
    }
}
// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
/**
 * Derives whether all required fields have values (for Next-button enablement).
 */
function isFormValid(form) {
    return (form.matterTypeId !== '' &&
        form.practiceAreaId !== '' &&
        form.matterName.trim() !== '');
}
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
    },
    // -- Step header --
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
    // -- AI Pre-filled badge --
    aiBadge: {
        flexShrink: 0,
        marginTop: tokens.spacingVerticalXS,
    },
    // -- 2-column grid --
    formGrid: {
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        gap: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalL}`,
    },
    // Fields that should span both columns
    fullWidth: {
        gridColumn: '1 / -1',
    },
    // -- Field label row (label + optional AI tag) --
    labelRow: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXXS,
    },
    // -- Summary section --
    summaryHeader: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: tokens.spacingHorizontalM,
    },
    // -- Skeleton items --
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
// CreateRecordStep (exported)
// ---------------------------------------------------------------------------
export const CreateRecordStep = ({ dataService, uploadedFileNames, uploadedFiles, onValidChange, onSubmit, initialFormValues, authenticatedFetch, bffBaseUrl, navigationService, }) => {
    const styles = useStyles();
    // -- Reducer --
    // When initialFormValues is provided with non-empty values (remount after
    // user navigated back), start from those values to preserve user edits and
    // Assign Resources overrides. Otherwise start empty for first mount.
    const hasInitialValues = initialFormValues && initialFormValues.matterName.trim() !== '';
    const [state, dispatch] = React.useReducer(combinedReducer, undefined, () => ({
        form: hasInitialValues ? { ...initialFormValues } : buildInitialFormState(),
        ai: buildInitialAiState(),
    }));
    const { form, ai } = state;
    // -- AI summary generation state --
    const [isGeneratingSummary, setIsGeneratingSummary] = React.useState(false);
    // -- Notify parent of validity changes --
    const valid = isFormValid(form);
    React.useEffect(() => {
        onValidChange(valid);
    }, [valid, onValidChange]);
    // -- Emit latest form values to parent on every change --
    const onSubmitRef = React.useRef(onSubmit);
    React.useEffect(() => {
        onSubmitRef.current = onSubmit;
    }, [onSubmit]);
    React.useEffect(() => {
        onSubmitRef.current(form);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [form]);
    // -- AI Pre-fill via shared hook --
    const handlePrefillApply = React.useCallback((resolved, prefilledFieldNames) => {
        const fields = {};
        for (const [key, value] of Object.entries(resolved)) {
            if (typeof value === 'string') {
                fields[key] = value;
            }
            else {
                // Lookup resolved: set both id and name fields
                // e.g., matterTypeName -> { id, name } -> set matterTypeId + matterTypeName
                const idKey = key.replace(/Name$/, 'Id');
                fields[idKey] = value.id;
                fields[key] = value.name;
            }
        }
        if (Object.keys(fields).length > 0) {
            dispatch({ type: 'APPLY_AI_PREFILL', fields });
        }
        dispatch({ type: 'AI_PREFILL_SUCCESS' });
    }, []);
    const prefill = useAiPrefill({
        endpoint: PREFILL_PATH,
        uploadedFiles,
        authenticatedFetch,
        bffBaseUrl,
        fieldExtractor: (data) => ({
            textFields: {
                matterName: data.matterName,
                summary: data.summary,
            },
            lookupFields: {
                // BFF may return old or new field names -- handle both
                matterTypeName: (data.matterTypeName || data.matterType),
                practiceAreaName: (data.practiceAreaName || data.practiceArea),
                assignedAttorneyName: data.assignedAttorneyName,
                assignedParalegalName: data.assignedParalegalName,
                assignedOutsideCounselName: data.assignedOutsideCounselName,
            },
        }),
        lookupResolvers: {
            matterTypeName: (v) => searchMatterTypes(dataService, v),
            practiceAreaName: (v) => searchPracticeAreas(dataService, v),
            assignedAttorneyName: (v) => searchContactsAsLookup(dataService, v),
            assignedParalegalName: (v) => searchContactsAsLookup(dataService, v),
            assignedOutsideCounselName: (v) => searchOrganizationsAsLookup(dataService, v),
        },
        onApply: handlePrefillApply,
        skipIfInitialized: !!hasInitialValues,
        logPrefix: 'CreateMatter',
    });
    // Sync hook status with reducer for loading/error states
    React.useEffect(() => {
        if (prefill.status === 'loading') {
            dispatch({ type: 'AI_PREFILL_LOADING' });
        }
        else if (prefill.status === 'error') {
            dispatch({ type: 'AI_PREFILL_ERROR' });
        }
    }, [prefill.status]);
    // -- Lookup search callbacks (stable refs) --
    const handleSearchMatterTypes = React.useCallback((query) => searchMatterTypes(dataService, query), [dataService]);
    const handleSearchPracticeAreas = React.useCallback((query) => searchPracticeAreas(dataService, query), [dataService]);
    const handleSearchAttorneys = React.useCallback((query) => searchContactsAsLookup(dataService, query), [dataService]);
    const handleSearchParalegals = React.useCallback((query) => searchContactsAsLookup(dataService, query), [dataService]);
    const handleSearchOutsideCounsel = React.useCallback((query) => searchOrganizationsAsLookup(dataService, query), [dataService]);
    // -- Lookup change handlers --
    const handleMatterTypeChange = React.useCallback((item) => {
        dispatch({
            type: 'SET_LOOKUP',
            idField: 'matterTypeId',
            nameField: 'matterTypeName',
            id: item?.id ?? '',
            name: item?.name ?? '',
        });
    }, []);
    const handlePracticeAreaChange = React.useCallback((item) => {
        dispatch({
            type: 'SET_LOOKUP',
            idField: 'practiceAreaId',
            nameField: 'practiceAreaName',
            id: item?.id ?? '',
            name: item?.name ?? '',
        });
    }, []);
    const handleAttorneyChange = React.useCallback((item) => {
        dispatch({
            type: 'SET_LOOKUP',
            idField: 'assignedAttorneyId',
            nameField: 'assignedAttorneyName',
            id: item?.id ?? '',
            name: item?.name ?? '',
        });
    }, []);
    const handleParalegalChange = React.useCallback((item) => {
        dispatch({
            type: 'SET_LOOKUP',
            idField: 'assignedParalegalId',
            nameField: 'assignedParalegalName',
            id: item?.id ?? '',
            name: item?.name ?? '',
        });
    }, []);
    const handleOutsideCounselChange = React.useCallback((item) => {
        dispatch({
            type: 'SET_LOOKUP',
            idField: 'assignedOutsideCounselId',
            nameField: 'assignedOutsideCounselName',
            id: item?.id ?? '',
            name: item?.name ?? '',
        });
    }, []);
    // -- Text field change handler --
    const handleFieldChange = React.useCallback((field) => (e) => {
        dispatch({ type: 'SET_FIELD', field, value: e.target.value });
    }, []);
    // -- AI Summary generation --
    const handleGenerateSummary = React.useCallback(async () => {
        setIsGeneratingSummary(true);
        try {
            const result = await fetchAiDraftSummary(form.matterName, form.matterTypeName, form.practiceAreaName, authenticatedFetch, bffBaseUrl);
            dispatch({ type: 'SET_FIELD', field: 'summary', value: result.summary });
        }
        finally {
            setIsGeneratingSummary(false);
        }
    }, [form.matterName, form.matterTypeName, form.practiceAreaName, authenticatedFetch, bffBaseUrl]);
    // -- Derived --
    const isLoading = ai.status === 'loading';
    const hasAnyPrefill = ai.prefilledFields.size > 0;
    const isAiField = (field) => ai.prefilledFields.has(field);
    // Build lookup value objects from form state
    const matterTypeValue = form.matterTypeId
        ? { id: form.matterTypeId, name: form.matterTypeName }
        : null;
    const practiceAreaValue = form.practiceAreaId
        ? { id: form.practiceAreaId, name: form.practiceAreaName }
        : null;
    const attorneyValue = form.assignedAttorneyId
        ? { id: form.assignedAttorneyId, name: form.assignedAttorneyName }
        : null;
    const paralegalValue = form.assignedParalegalId
        ? { id: form.assignedParalegalId, name: form.assignedParalegalName }
        : null;
    const outsideCounselValue = form.assignedOutsideCounselId
        ? { id: form.assignedOutsideCounselId, name: form.assignedOutsideCounselName }
        : null;
    /**
     * Renders the label for a text field with optional required mark and AI tag.
     */
    const renderLabel = (text, field, required) => (React.createElement("span", { className: styles.labelRow },
        text,
        required && (React.createElement("span", { "aria-hidden": "true", style: { color: tokens.colorPaletteRedForeground1 } }, ' *')),
        isAiField(field) && React.createElement(AiFieldTag, null)));
    // -- Render --
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.headerRow },
            React.createElement("div", { className: styles.headerText },
                React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Enter Info"),
                React.createElement(Text, { size: 200, className: styles.stepSubtitle }, isLoading
                    ? 'Analysing uploaded documents\u2026'
                    : 'Fill in the matter details. Required fields are marked with *.')),
            hasAnyPrefill && (React.createElement(Badge, { className: styles.aiBadge, appearance: "tint", color: "brand", icon: React.createElement("span", { "aria-hidden": "true" }) }, "AI Pre-filled"))),
        isLoading ? (React.createElement("div", { className: styles.formGrid },
            React.createElement(FieldSkeleton, null),
            React.createElement(FieldSkeleton, null),
            React.createElement("div", { className: styles.fullWidth },
                React.createElement(FieldSkeleton, null)),
            React.createElement("div", { className: styles.fullWidth },
                React.createElement(FieldSkeleton, { large: true })))) : (React.createElement("div", { className: styles.formGrid },
            React.createElement(DataverseLookupField, { label: "Matter Type", required: true, entityType: "sprk_mattertype_ref", value: matterTypeValue, onChange: handleMatterTypeChange, navigationService: navigationService, onSearch: handleSearchMatterTypes, placeholder: "Search matter types...", labelExtra: isAiField('matterTypeId') ? React.createElement(AiFieldTag, null) : undefined, minSearchLength: 1 }),
            React.createElement(DataverseLookupField, { label: "Practice Area", required: true, entityType: "sprk_practicearea_ref", value: practiceAreaValue, onChange: handlePracticeAreaChange, navigationService: navigationService, onSearch: handleSearchPracticeAreas, placeholder: "Search practice areas...", labelExtra: isAiField('practiceAreaId') ? React.createElement(AiFieldTag, null) : undefined, minSearchLength: 1 }),
            React.createElement(Field, { className: styles.fullWidth, label: renderLabel('Matter Name', 'matterName', true), required: true },
                React.createElement(Input, { value: form.matterName, onChange: handleFieldChange('matterName'), placeholder: "Enter matter name", "aria-label": "Matter Name" })),
            React.createElement("div", { className: styles.fullWidth },
                React.createElement("div", { className: styles.summaryHeader },
                    renderLabel('Matter Description', 'summary'),
                    React.createElement(Button, { appearance: "subtle", size: "small", icon: isGeneratingSummary ? React.createElement(Spinner, { size: "extra-tiny" }) : React.createElement(SparkleRegular, null), onClick: handleGenerateSummary, disabled: isGeneratingSummary || !form.matterName.trim(), "aria-label": "Generate description with AI" }, isGeneratingSummary ? 'Generating\u2026' : 'Generate with AI')),
                React.createElement(Textarea, { value: form.summary, onChange: handleFieldChange('summary'), placeholder: "Brief description of the matter, its background, and objectives", rows: 11, resize: "vertical", "aria-label": "Matter Description", style: { marginTop: tokens.spacingVerticalXS, width: '100%' } }))))));
};
export { CreateRecordStep as default };
//# sourceMappingURL=CreateRecordStep.js.map