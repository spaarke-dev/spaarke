/**
 * AssignCounselStep.tsx
 * Follow-on step for "Assign Counsel" in the Create New Matter wizard.
 *
 * Uses IDataService (via searchContacts from matterService) to query
 * contact records filtered by name. Minimum 2 characters required
 * before a search fires. Results debounced 400ms.
 *
 * Constraints:
 *   - Fluent v9: Input, Text, Button, Spinner, MessageBar
 *   - makeStyles with semantic tokens -- ZERO hardcoded colors
 */
import * as React from 'react';
import { Input, Text, Button, Spinner, MessageBar, MessageBarBody, makeStyles, tokens, mergeClasses, } from '@fluentui/react-components';
import { PersonRegular, DismissRegular, SearchRegular } from '@fluentui/react-icons';
import { searchContacts } from './matterService';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
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
    // -- Search area --
    searchWrapper: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
    },
    // -- Results list --
    resultsList: {
        display: 'flex',
        flexDirection: 'column',
        gap: '2px',
        borderTopWidth: '1px',
        borderRightWidth: '1px',
        borderBottomWidth: '1px',
        borderLeftWidth: '1px',
        borderTopStyle: 'solid',
        borderRightStyle: 'solid',
        borderBottomStyle: 'solid',
        borderLeftStyle: 'solid',
        borderTopColor: tokens.colorNeutralStroke1,
        borderRightColor: tokens.colorNeutralStroke1,
        borderBottomColor: tokens.colorNeutralStroke1,
        borderLeftColor: tokens.colorNeutralStroke1,
        borderRadius: tokens.borderRadiusMedium,
        overflow: 'hidden',
        maxHeight: '240px',
        overflowY: 'auto',
    },
    resultItem: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalS,
        cursor: 'pointer',
        gap: tokens.spacingHorizontalM,
        backgroundColor: tokens.colorNeutralBackground1,
        ':hover': {
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
        ':focus-visible': {
            outline: `2px solid ${tokens.colorBrandStroke1}`,
            outlineOffset: '-2px',
        },
    },
    resultItemSelected: {
        backgroundColor: tokens.colorBrandBackground2,
        ':hover': {
            backgroundColor: tokens.colorBrandBackground2Hover,
        },
    },
    resultInfo: {
        display: 'flex',
        flexDirection: 'column',
        gap: '1px',
        minWidth: 0,
        flex: '1 1 auto',
    },
    resultName: {
        color: tokens.colorNeutralForeground1,
    },
    resultEmail: {
        color: tokens.colorNeutralForeground3,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
    },
    personIcon: {
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
        display: 'flex',
        alignItems: 'center',
        marginRight: tokens.spacingHorizontalS,
    },
    // -- Empty / loading / error states --
    stateMessage: {
        color: tokens.colorNeutralForeground3,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        textAlign: 'center',
    },
    // -- Selected contact chip --
    selectedChip: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalXS,
        borderRadius: tokens.borderRadiusCircular,
        backgroundColor: tokens.colorBrandBackground2,
        borderTopWidth: '1px',
        borderRightWidth: '1px',
        borderBottomWidth: '1px',
        borderLeftWidth: '1px',
        borderTopStyle: 'solid',
        borderRightStyle: 'solid',
        borderBottomStyle: 'solid',
        borderLeftStyle: 'solid',
        borderTopColor: tokens.colorBrandStroke1,
        borderRightColor: tokens.colorBrandStroke1,
        borderBottomColor: tokens.colorBrandStroke1,
        borderLeftColor: tokens.colorBrandStroke1,
        alignSelf: 'flex-start',
    },
    selectedChipName: {
        color: tokens.colorBrandForeground2,
    },
    spinnerRow: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
    },
});
// ---------------------------------------------------------------------------
// AssignCounselStep (exported)
// ---------------------------------------------------------------------------
export const AssignCounselStep = ({ dataService, selectedContact, onContactChange, }) => {
    const styles = useStyles();
    const [searchTerm, setSearchTerm] = React.useState('');
    const [results, setResults] = React.useState([]);
    const [loading, setLoading] = React.useState(false);
    const [searchError, setSearchError] = React.useState(null);
    const debounceRef = React.useRef(null);
    // -- Debounced search --
    React.useEffect(() => {
        if (debounceRef.current) {
            clearTimeout(debounceRef.current);
        }
        if (searchTerm.trim().length < 2) {
            setResults([]);
            setSearchError(null);
            return;
        }
        debounceRef.current = setTimeout(async () => {
            setLoading(true);
            setSearchError(null);
            try {
                const contacts = await searchContacts(dataService, searchTerm.trim());
                setResults(contacts);
            }
            catch {
                setSearchError('Search failed. Please try again.');
                setResults([]);
            }
            finally {
                setLoading(false);
            }
        }, 400);
        return () => {
            if (debounceRef.current) {
                clearTimeout(debounceRef.current);
            }
        };
    }, [searchTerm, dataService]);
    const handleSearchChange = React.useCallback((e) => {
        setSearchTerm(e.target.value);
        // Clear selected contact when user re-types
        if (selectedContact) {
            onContactChange(null);
        }
    }, [selectedContact, onContactChange]);
    const handleSelectContact = React.useCallback((contact) => {
        onContactChange(contact);
        setSearchTerm(contact.sprk_name);
        setResults([]);
    }, [onContactChange]);
    const handleClearContact = React.useCallback(() => {
        onContactChange(null);
        setSearchTerm('');
        setResults([]);
    }, [onContactChange]);
    // -- Render --
    const showResults = !loading && !selectedContact && results.length > 0 && searchTerm.trim().length >= 2;
    const showEmpty = !loading && !selectedContact && results.length === 0 && searchTerm.trim().length >= 2 && !searchError;
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.headerText },
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Assign Counsel"),
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Search for a contact to assign as lead counsel for this matter. Type at least 2 characters to search.")),
        React.createElement("div", { className: styles.searchWrapper },
            React.createElement(Input, { value: searchTerm, onChange: handleSearchChange, placeholder: "Search contacts by name...", contentBefore: React.createElement(SearchRegular, { "aria-hidden": "true" }), "aria-label": "Search contacts", autoComplete: "off" }),
            loading && (React.createElement("div", { className: styles.spinnerRow },
                React.createElement(Spinner, { size: "tiny", label: "Searching..." }))),
            searchError && (React.createElement(MessageBar, { intent: "error" },
                React.createElement(MessageBarBody, null, searchError))),
            showResults && (React.createElement("div", { className: styles.resultsList, role: "listbox", "aria-label": "Contact search results" }, results.map((contact) => (React.createElement("div", { key: contact.sprk_contactid, className: mergeClasses(styles.resultItem, selectedContact?.sprk_contactid === contact.sprk_contactid
                    ? styles.resultItemSelected
                    : undefined), role: "option", "aria-selected": selectedContact?.sprk_contactid === contact.sprk_contactid, tabIndex: 0, onClick: () => handleSelectContact(contact), onKeyDown: (e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        handleSelectContact(contact);
                    }
                } },
                React.createElement("span", { className: styles.personIcon, "aria-hidden": "true" },
                    React.createElement(PersonRegular, { fontSize: 18 })),
                React.createElement("div", { className: styles.resultInfo },
                    React.createElement(Text, { size: 300, weight: "semibold", className: styles.resultName }, contact.sprk_name),
                    contact.sprk_email && (React.createElement(Text, { size: 100, className: styles.resultEmail }, contact.sprk_email))),
                React.createElement(Button, { appearance: "subtle", size: "small", onClick: (e) => {
                        e.stopPropagation();
                        handleSelectContact(contact);
                    }, "aria-label": `Select ${contact.sprk_name}` }, "Select")))))),
            showEmpty && (React.createElement(Text, { size: 200, className: styles.stateMessage },
                "No contacts found matching \u201C",
                searchTerm,
                "\u201D."))),
        selectedContact && (React.createElement("div", { className: styles.selectedChip },
            React.createElement("span", { className: styles.personIcon, "aria-hidden": "true" },
                React.createElement(PersonRegular, { fontSize: 16 })),
            React.createElement(Text, { size: 300, weight: "semibold", className: styles.selectedChipName }, selectedContact.sprk_name),
            selectedContact.sprk_email && (React.createElement(Text, { size: 200, className: styles.selectedChipName },
                "\u00B7 ",
                selectedContact.sprk_email)),
            React.createElement(Button, { appearance: "subtle", size: "small", icon: React.createElement(DismissRegular, { fontSize: 14 }), onClick: handleClearContact, "aria-label": `Remove ${selectedContact.sprk_name}` })))));
};
//# sourceMappingURL=AssignCounselStep.js.map