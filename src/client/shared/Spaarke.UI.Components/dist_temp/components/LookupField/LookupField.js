/**
 * LookupField.tsx
 * Reusable search-as-you-type lookup field for entity reference searches.
 *
 * Layout:
 *   ┌───────────────────────────────────────────────┐
 *   │ [Search input: "lit..."]                   [x] │
 *   ├───────────────────────────────────────────────┤
 *   │  Litigation                                    │
 *   │  Licensing                                     │
 *   │  Litigation Support                            │
 *   └───────────────────────────────────────────────┘
 *   — OR —
 *   Selected: [Litigation] [x]
 *
 * Constraints:
 *   - Fluent v9: Input, Text, Button, Spinner
 *   - makeStyles with semantic tokens — ZERO hardcoded colors
 *   - Full keyboard support (arrow keys, Enter, Escape)
 */
import * as React from 'react';
import { Input, Text, Button, Spinner, Field, makeStyles, tokens, mergeClasses } from '@fluentui/react-components';
import { DismissRegular, SearchRegular } from '@fluentui/react-icons';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    wrapper: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXXS,
    },
    labelRow: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXXS,
    },
    requiredMark: {
        color: tokens.colorPaletteRedForeground1,
    },
    resultsList: {
        display: 'flex',
        flexDirection: 'column',
        gap: '1px',
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
        maxHeight: '200px',
        overflowY: 'auto',
        marginTop: tokens.spacingVerticalXXS,
    },
    resultItem: {
        display: 'flex',
        alignItems: 'center',
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        cursor: 'pointer',
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
        ':hover': {
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
        ':focus-visible': {
            outlineStyle: 'solid',
            outlineWidth: '2px',
            outlineColor: tokens.colorBrandStroke1,
            outlineOffset: '-2px',
        },
    },
    resultItemHighlighted: {
        backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    selectedChip: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalXXS,
        paddingBottom: tokens.spacingVerticalXXS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalXXS,
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
        borderTopColor: tokens.colorBrandStroke2,
        borderRightColor: tokens.colorBrandStroke2,
        borderBottomColor: tokens.colorBrandStroke2,
        borderLeftColor: tokens.colorBrandStroke2,
        alignSelf: 'flex-start',
        marginTop: tokens.spacingVerticalXXS,
    },
    selectedChipName: {
        color: tokens.colorBrandForeground2,
    },
    spinnerRow: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
    },
    emptyText: {
        color: tokens.colorNeutralForeground3,
        paddingTop: tokens.spacingVerticalS,
        textAlign: 'center',
    },
});
// ---------------------------------------------------------------------------
// LookupField (exported)
// ---------------------------------------------------------------------------
export const LookupField = ({ label, required, placeholder, value, onChange, onSearch, labelExtra, minSearchLength = 1, }) => {
    const styles = useStyles();
    const [searchTerm, setSearchTerm] = React.useState('');
    const [results, setResults] = React.useState([]);
    const [loading, setLoading] = React.useState(false);
    const [showResults, setShowResults] = React.useState(false);
    const [highlightedIndex, setHighlightedIndex] = React.useState(-1);
    const debounceRef = React.useRef(null);
    const wrapperRef = React.useRef(null);
    // ── Debounced search ──────────────────────────────────────────────────
    React.useEffect(() => {
        if (debounceRef.current) {
            clearTimeout(debounceRef.current);
        }
        if (searchTerm.trim().length < minSearchLength) {
            setResults([]);
            setShowResults(false);
            return;
        }
        debounceRef.current = setTimeout(async () => {
            setLoading(true);
            try {
                const items = await onSearch(searchTerm.trim());
                setResults(items);
                setShowResults(items.length > 0);
                setHighlightedIndex(-1);
            }
            catch (err) {
                console.error('[LookupField] Search error:', label, err);
                setResults([]);
                setShowResults(false);
            }
            finally {
                setLoading(false);
            }
        }, 300);
        return () => {
            if (debounceRef.current) {
                clearTimeout(debounceRef.current);
            }
        };
    }, [searchTerm, onSearch, minSearchLength]);
    // ── Close results on outside click ────────────────────────────────────
    React.useEffect(() => {
        const handleClickOutside = (e) => {
            if (wrapperRef.current && !wrapperRef.current.contains(e.target)) {
                setShowResults(false);
            }
        };
        document.addEventListener('mousedown', handleClickOutside);
        return () => document.removeEventListener('mousedown', handleClickOutside);
    }, []);
    // ── Handlers ──────────────────────────────────────────────────────────
    const handleSearchChange = React.useCallback((e) => {
        setSearchTerm(e.target.value);
        if (value) {
            onChange(null);
        }
    }, [value, onChange]);
    const handleSelect = React.useCallback((item) => {
        onChange(item);
        setSearchTerm(item.name);
        setResults([]);
        setShowResults(false);
    }, [onChange]);
    const handleClear = React.useCallback(() => {
        onChange(null);
        setSearchTerm('');
        setResults([]);
        setShowResults(false);
    }, [onChange]);
    const handleKeyDown = React.useCallback((e) => {
        if (!showResults || results.length === 0)
            return;
        if (e.key === 'ArrowDown') {
            e.preventDefault();
            setHighlightedIndex(prev => (prev < results.length - 1 ? prev + 1 : 0));
        }
        else if (e.key === 'ArrowUp') {
            e.preventDefault();
            setHighlightedIndex(prev => (prev > 0 ? prev - 1 : results.length - 1));
        }
        else if (e.key === 'Enter' && highlightedIndex >= 0) {
            e.preventDefault();
            handleSelect(results[highlightedIndex]);
        }
        else if (e.key === 'Escape') {
            setShowResults(false);
        }
    }, [showResults, results, highlightedIndex, handleSelect]);
    const handleFocus = React.useCallback(() => {
        if (results.length > 0 && !value) {
            setShowResults(true);
        }
    }, [results.length, value]);
    // ── Render label ──────────────────────────────────────────────────────
    const renderLabel = () => (React.createElement("span", { className: styles.labelRow },
        label,
        required && (React.createElement("span", { "aria-hidden": "true", className: styles.requiredMark }, ' *')),
        labelExtra));
    const showEmpty = !loading && !value && results.length === 0 && searchTerm.trim().length >= minSearchLength;
    return (React.createElement("div", { className: styles.wrapper, ref: wrapperRef },
        React.createElement(Field, { label: renderLabel(), required: required }, value ? (React.createElement("div", { className: styles.selectedChip },
            React.createElement(Text, { size: 200, weight: "semibold", className: styles.selectedChipName }, value.name),
            React.createElement(Button, { appearance: "subtle", size: "small", icon: React.createElement(DismissRegular, { fontSize: 14 }), onClick: handleClear, "aria-label": `Clear ${label}` }))) : (React.createElement(Input, { value: searchTerm, onChange: handleSearchChange, onKeyDown: handleKeyDown, onFocus: handleFocus, placeholder: placeholder ?? `Search ${label.toLowerCase()}...`, contentBefore: React.createElement(SearchRegular, { "aria-hidden": "true" }), "aria-label": label, autoComplete: "off" }))),
        loading && (React.createElement("div", { className: styles.spinnerRow },
            React.createElement(Spinner, { size: "tiny", label: "Searching..." }))),
        showResults && !value && (React.createElement("div", { className: styles.resultsList, role: "listbox", "aria-label": `${label} search results` }, results.map((item, index) => (React.createElement("div", { key: item.id, className: mergeClasses(styles.resultItem, index === highlightedIndex ? styles.resultItemHighlighted : undefined), role: "option", "aria-selected": index === highlightedIndex, tabIndex: 0, onClick: () => handleSelect(item), onKeyDown: e => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    handleSelect(item);
                }
            } },
            React.createElement(Text, { size: 200 }, item.name)))))),
        showEmpty && (React.createElement(Text, { size: 100, className: styles.emptyText }, "No results found"))));
};
LookupField.displayName = 'LookupField';
//# sourceMappingURL=LookupField.js.map