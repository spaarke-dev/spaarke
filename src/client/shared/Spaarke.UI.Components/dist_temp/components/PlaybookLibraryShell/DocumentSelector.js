/**
 * DocumentSelector — single-select document switcher for PlaybookLibraryShell.
 *
 * Rendered at the top of the PlaybookLibrary when two or more document IDs
 * are passed via the `documentIds` parameter. Fetches document names from
 * Dataverse and presents a RadioGroup so the user can switch the active
 * document before running an analysis.
 *
 * Single-select MVP: only one document can be active at a time.
 *
 * @see ADR-021 — Fluent v9 tokens only; dark mode via FluentProvider cascade.
 * @see ADR-012 — Shared component; IDataService for all Dataverse access.
 */
import React from 'react';
import { RadioGroup, Radio, Spinner, Text, makeStyles, tokens, mergeClasses, } from '@fluentui/react-components';
import { DocumentRegular } from '@fluentui/react-icons';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalXL,
        paddingRight: tokens.spacingHorizontalXL,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
        backgroundColor: tokens.colorNeutralBackground2,
        flexShrink: 0,
    },
    label: {
        color: tokens.colorNeutralForeground3,
        marginBottom: tokens.spacingVerticalXS,
    },
    radioGroup: {
        display: 'flex',
        flexDirection: 'row',
        flexWrap: 'wrap',
        gap: tokens.spacingHorizontalM,
    },
    radioItem: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXS,
    },
    docIcon: {
        color: tokens.colorNeutralForeground3,
        flexShrink: 0,
        fontSize: '16px',
    },
    loading: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
    },
});
// ---------------------------------------------------------------------------
// DocumentSelector component
// ---------------------------------------------------------------------------
/**
 * Fetches document display names for the given IDs and renders a horizontal
 * RadioGroup so the user can switch the active document.
 *
 * Hidden when documentIds.length < 2 (caller is responsible for the guard,
 * but the component also returns null defensively).
 */
export const DocumentSelector = ({ documentIds, selectedDocumentId, onSelect, dataService, className, }) => {
    const styles = useStyles();
    const [documents, setDocuments] = React.useState([]);
    const [isLoading, setIsLoading] = React.useState(true);
    const [fetchError, setFetchError] = React.useState(null);
    // Fetch document names from Dataverse on mount / when IDs change.
    React.useEffect(() => {
        if (documentIds.length === 0) {
            setIsLoading(false);
            return;
        }
        let cancelled = false;
        setIsLoading(true);
        setFetchError(null);
        (async () => {
            try {
                // Build an OData filter for the provided IDs.
                // Maximum supported by Dataverse in a single filter is well above the
                // expected handful of docs for this MVP scenario.
                const idList = documentIds
                    .map(id => `sprk_documentid eq '${id}'`)
                    .join(' or ');
                const options = `?$select=sprk_documentid,sprk_name&$filter=${idList}`;
                const result = await dataService.retrieveMultipleRecords('sprk_document', options);
                if (cancelled)
                    return;
                // Preserve caller-supplied order so the UI is stable.
                const nameMap = new Map();
                for (const entity of result.entities) {
                    const id = entity['sprk_documentid'];
                    const name = entity['sprk_name'];
                    if (id) {
                        nameMap.set(id, name ?? 'Untitled Document');
                    }
                }
                const resolved = documentIds.map(id => ({
                    id,
                    name: nameMap.get(id) ?? 'Untitled Document',
                }));
                setDocuments(resolved);
            }
            catch (err) {
                if (cancelled)
                    return;
                setFetchError(err instanceof Error ? err.message : 'Failed to load documents');
            }
            finally {
                if (!cancelled)
                    setIsLoading(false);
            }
        })();
        return () => {
            cancelled = true;
        };
    }, [documentIds, dataService]);
    // Guard: hide when fewer than 2 documents.
    if (documentIds.length < 2)
        return null;
    return (React.createElement("div", { className: mergeClasses(styles.root, className) },
        React.createElement(Text, { size: 200, weight: "semibold", className: styles.label }, "Analyze document"),
        isLoading ? (React.createElement("div", { className: styles.loading },
            React.createElement(Spinner, { size: "tiny" }),
            React.createElement(Text, { size: 200 }, "Loading documents\u2026"))) : fetchError ? (React.createElement(Text, { size: 200, style: { color: tokens.colorPaletteRedForeground1 } }, fetchError)) : (React.createElement(RadioGroup, { value: selectedDocumentId, onChange: (_ev, data) => onSelect(data.value), layout: "horizontal", className: styles.radioGroup }, documents.map(doc => (React.createElement(Radio, { key: doc.id, value: doc.id, label: React.createElement("span", { className: styles.radioItem },
                React.createElement(DocumentRegular, { className: styles.docIcon }),
                React.createElement("span", null, doc.name)) })))))));
};
export default DocumentSelector;
//# sourceMappingURL=DocumentSelector.js.map