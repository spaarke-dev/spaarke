/**
 * SprkChatActionMenu - Command palette / action menu for the SprkChat component
 *
 * Triggered by typing "/" in the chat input or clicking an action button.
 * Displays available actions organized by category (Playbooks, Actions, Search,
 * Settings) with keyboard navigation (arrow keys + Enter) and fuzzy text filtering.
 *
 * Features:
 * - Grouped actions by category with section headers
 * - Keyboard navigation: ArrowUp/Down to move, Enter to select, Escape to close
 * - Text filtering by label and description (case-insensitive)
 * - Highlight matching text during filtering
 * - Keyboard shortcut hints
 * - Accessible (ARIA roles, focus management)
 *
 * This is a pure UI component — data fetching and action handling are wired
 * externally by the consuming component.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 */

import * as React from "react";
import {
    makeStyles,
    shorthands,
    tokens,
    Text,
    Spinner,
    MessageBar,
    MessageBarBody,
    mergeClasses,
} from "@fluentui/react-components";
import {
    BookRegular,
    FlashRegular,
    SearchRegular,
    SettingsRegular,
} from "@fluentui/react-icons";
import { IChatAction, ISprkChatActionMenuProps, ISprkChatActionMenuHandle, ChatActionCategory } from "./types";

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Category display configuration with order, label, and icon. */
const CATEGORY_CONFIG: ReadonlyArray<{
    key: ChatActionCategory;
    label: string;
    icon: React.ReactElement;
}> = [
    { key: "playbooks", label: "Playbooks", icon: React.createElement(BookRegular) },
    { key: "actions", label: "Actions", icon: React.createElement(FlashRegular) },
    { key: "search", label: "Search", icon: React.createElement(SearchRegular) },
    { key: "settings", label: "Settings", icon: React.createElement(SettingsRegular) },
];

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    overlay: {
        position: "absolute",
        bottom: "100%",
        left: 0,
        right: 0,
        zIndex: 1000,
        display: "flex",
        flexDirection: "column",
        maxHeight: "320px",
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...shorthands.border(tokens.strokeWidthThin, "solid", tokens.colorNeutralStroke1),
        backgroundColor: tokens.colorNeutralBackground1,
        boxShadow: tokens.shadow16,
        ...shorthands.overflow("hidden"),
    },
    listContainer: {
        flexGrow: 1,
        overflowY: "auto",
        overflowX: "hidden",
        ...shorthands.padding(tokens.spacingVerticalXS, "0"),
    },
    categoryHeader: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
        ...shorthands.padding(
            tokens.spacingVerticalXS,
            tokens.spacingHorizontalM
        ),
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        textTransform: "uppercase" as const,
        letterSpacing: "0.05em",
        userSelect: "none",
    },
    categoryIcon: {
        display: "flex",
        alignItems: "center",
        fontSize: tokens.fontSizeBase300,
    },
    menuItem: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.padding(
            tokens.spacingVerticalS,
            tokens.spacingHorizontalM
        ),
        cursor: "pointer",
        backgroundColor: tokens.colorTransparentBackground,
        ...shorthands.border("0", "none", tokens.colorTransparentStroke),
        width: "100%",
        textAlign: "left",
        color: tokens.colorNeutralForeground1,
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        ":hover": {
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
        ":focus-visible": {
            outlineWidth: tokens.strokeWidthThick,
            outlineStyle: "solid",
            outlineColor: tokens.colorStrokeFocus2,
            outlineOffset: `-${tokens.strokeWidthThick}`,
        },
    },
    menuItemActive: {
        backgroundColor: tokens.colorNeutralBackground1Selected,
    },
    menuItemDisabled: {
        color: tokens.colorNeutralForegroundDisabled,
        cursor: "not-allowed",
        ":hover": {
            backgroundColor: tokens.colorTransparentBackground,
        },
    },
    menuItemContent: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalXXS),
        flexGrow: 1,
        minWidth: 0,
    },
    menuItemLabel: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightRegular,
        color: "inherit",
        ...shorthands.overflow("hidden"),
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    },
    menuItemDescription: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        ...shorthands.overflow("hidden"),
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    },
    shortcutBadge: {
        flexShrink: 0,
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
        backgroundColor: tokens.colorNeutralBackground3,
        ...shorthands.padding(tokens.spacingVerticalXXS, tokens.spacingHorizontalXS),
        ...shorthands.borderRadius(tokens.borderRadiusSmall),
        ...shorthands.border(tokens.strokeWidthThin, "solid", tokens.colorNeutralStroke2),
        fontFamily: tokens.fontFamilyMonospace,
        whiteSpace: "nowrap",
    },
    emptyState: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        ...shorthands.padding(tokens.spacingVerticalL, tokens.spacingHorizontalM),
        color: tokens.colorNeutralForeground3,
    },
    loadingState: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        ...shorthands.padding(tokens.spacingVerticalL, tokens.spacingHorizontalM),
    },
    errorState: {
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalS),
    },
    highlight: {
        backgroundColor: tokens.colorPaletteYellowBackground2,
        color: tokens.colorNeutralForeground1,
        fontWeight: tokens.fontWeightSemibold,
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Filters actions by matching filterText against label and description (case-insensitive).
 */
function filterActions(actions: IChatAction[], filterText: string): IChatAction[] {
    if (!filterText) {
        return actions;
    }
    const needle = filterText.toLowerCase();
    return actions.filter((action) => {
        const labelMatch = action.label.toLowerCase().includes(needle);
        const descMatch = action.description
            ? action.description.toLowerCase().includes(needle)
            : false;
        return labelMatch || descMatch;
    });
}

/**
 * Groups filtered actions by category, preserving the standard category order.
 * Returns only categories that have at least one matching action.
 */
function groupByCategory(
    actions: IChatAction[]
): Array<{ category: ChatActionCategory; label: string; icon: React.ReactElement; items: IChatAction[] }> {
    const grouped: Array<{
        category: ChatActionCategory;
        label: string;
        icon: React.ReactElement;
        items: IChatAction[];
    }> = [];

    for (const config of CATEGORY_CONFIG) {
        const items = actions.filter((a) => a.category === config.key);
        if (items.length > 0) {
            grouped.push({
                category: config.key,
                label: config.label,
                icon: config.icon,
                items,
            });
        }
    }

    return grouped;
}

/**
 * Builds a flat list of selectable action items from grouped categories.
 * This flat list is used for keyboard navigation indexing.
 */
function buildFlatList(
    groups: Array<{ items: IChatAction[] }>
): IChatAction[] {
    const flat: IChatAction[] = [];
    for (const group of groups) {
        for (const item of group.items) {
            flat.push(item);
        }
    }
    return flat;
}

// ─────────────────────────────────────────────────────────────────────────────
// Sub-components
// ─────────────────────────────────────────────────────────────────────────────

interface IHighlightedTextProps {
    text: string;
    highlight: string;
    className?: string;
}

/**
 * Renders text with matching portions highlighted.
 * Splits on the first occurrence of `highlight` (case-insensitive) and wraps
 * the matching portion in a styled span.
 */
const HighlightedText: React.FC<IHighlightedTextProps> = ({ text, highlight, className }) => {
    const styles = useStyles();

    if (!highlight) {
        return React.createElement("span", { className }, text);
    }

    const lowerText = text.toLowerCase();
    const lowerHighlight = highlight.toLowerCase();
    const index = lowerText.indexOf(lowerHighlight);

    if (index === -1) {
        return React.createElement("span", { className }, text);
    }

    const before = text.substring(0, index);
    const match = text.substring(index, index + highlight.length);
    const after = text.substring(index + highlight.length);

    return React.createElement(
        "span",
        { className },
        before,
        React.createElement("span", { className: styles.highlight }, match),
        after
    );
};

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SprkChatActionMenu - Command palette / action menu with keyboard navigation.
 *
 * Displays a categorized list of available actions that can be filtered by
 * typing. Supports full keyboard navigation with ArrowUp/Down, Enter, and
 * Escape keys.
 *
 * @example
 * ```tsx
 * <SprkChatActionMenu
 *   actions={[
 *     { id: "search-docs", label: "Search documents", category: "search" },
 *     { id: "run-playbook", label: "Run playbook", category: "playbooks", shortcut: "Ctrl+P" },
 *   ]}
 *   isOpen={showActionMenu}
 *   onSelect={(action) => handleAction(action)}
 *   onDismiss={() => setShowActionMenu(false)}
 *   filterText={slashFilter}
 * />
 * ```
 */
export const SprkChatActionMenu = React.forwardRef<ISprkChatActionMenuHandle, ISprkChatActionMenuProps>(({
    actions,
    isOpen,
    onSelect,
    onDismiss,
    filterText = "",
    anchorRef,
    isLoading = false,
    errorMessage,
}, ref) => {
    const styles = useStyles();
    const menuRef = React.useRef<HTMLDivElement>(null);
    const [activeIndex, setActiveIndex] = React.useState<number>(0);

    // Filter and group actions
    const filteredActions = React.useMemo(
        () => filterActions(actions, filterText),
        [actions, filterText]
    );

    const groups = React.useMemo(
        () => groupByCategory(filteredActions),
        [filteredActions]
    );

    const flatList = React.useMemo(() => buildFlatList(groups), [groups]);

    // Find first non-disabled index in the flat list
    const findFirstEnabledIndex = React.useCallback((): number => {
        for (let i = 0; i < flatList.length; i++) {
            if (!flatList[i].disabled) {
                return i;
            }
        }
        return 0;
    }, [flatList]);

    // Reset active index when filter changes or menu opens
    React.useEffect(() => {
        if (isOpen) {
            setActiveIndex(findFirstEnabledIndex());
        }
    }, [isOpen, filterText, findFirstEnabledIndex]);

    // Close menu when clicking outside
    React.useEffect(() => {
        if (!isOpen) {
            return;
        }

        const handleClickOutside = (event: MouseEvent) => {
            const target = event.target as Node;
            const menuEl = menuRef.current;
            const anchorEl = anchorRef?.current;

            if (
                menuEl &&
                !menuEl.contains(target) &&
                (!anchorEl || !anchorEl.contains(target))
            ) {
                onDismiss();
            }
        };

        document.addEventListener("mousedown", handleClickOutside);
        return () => {
            document.removeEventListener("mousedown", handleClickOutside);
        };
    }, [isOpen, onDismiss, anchorRef]);

    // Find next non-disabled index in given direction
    const findNextEnabledIndex = React.useCallback(
        (currentIndex: number, direction: 1 | -1): number => {
            if (flatList.length === 0) {
                return 0;
            }
            let nextIndex = currentIndex;
            for (let i = 0; i < flatList.length; i++) {
                nextIndex = (nextIndex + direction + flatList.length) % flatList.length;
                if (!flatList[nextIndex].disabled) {
                    return nextIndex;
                }
            }
            return currentIndex;
        },
        [flatList]
    );

    // Expose imperative navigation methods so SprkChatInput can drive
    // keyboard navigation while the textarea retains focus.
    React.useImperativeHandle(ref, () => ({
        navigateUp: () => {
            setActiveIndex((prev) => findNextEnabledIndex(prev, -1));
        },
        navigateDown: () => {
            setActiveIndex((prev) => findNextEnabledIndex(prev, 1));
        },
        selectActive: () => {
            const selectedAction = flatList[activeIndex];
            if (selectedAction && !selectedAction.disabled) {
                onSelect(selectedAction);
            }
        },
    }), [findNextEnabledIndex, flatList, activeIndex, onSelect]);

    // Keyboard navigation handler
    const handleKeyDown = React.useCallback(
        (event: React.KeyboardEvent<HTMLDivElement>) => {
            switch (event.key) {
                case "ArrowDown": {
                    event.preventDefault();
                    event.stopPropagation();
                    setActiveIndex((prev) => findNextEnabledIndex(prev, 1));
                    break;
                }
                case "ArrowUp": {
                    event.preventDefault();
                    event.stopPropagation();
                    setActiveIndex((prev) => findNextEnabledIndex(prev, -1));
                    break;
                }
                case "Enter": {
                    event.preventDefault();
                    event.stopPropagation();
                    const selectedAction = flatList[activeIndex];
                    if (selectedAction && !selectedAction.disabled) {
                        onSelect(selectedAction);
                    }
                    break;
                }
                case "Escape": {
                    event.preventDefault();
                    event.stopPropagation();
                    onDismiss();
                    break;
                }
                default:
                    break;
            }
        },
        [activeIndex, flatList, findNextEnabledIndex, onSelect, onDismiss]
    );

    // Scroll active item into view
    React.useEffect(() => {
        if (!isOpen || flatList.length === 0) {
            return;
        }
        const activeAction = flatList[activeIndex];
        if (!activeAction) {
            return;
        }
        const activeElement = menuRef.current?.querySelector(
            `[data-action-id="${activeAction.id}"]`
        );
        if (activeElement) {
            activeElement.scrollIntoView({ block: "nearest" });
        }
    }, [activeIndex, flatList, isOpen]);

    // Handle item click
    const handleItemClick = React.useCallback(
        (action: IChatAction) => {
            if (!action.disabled) {
                onSelect(action);
            }
        },
        [onSelect]
    );

    // Handle item mouse enter — update active index to match hovered item
    const handleItemMouseEnter = React.useCallback(
        (action: IChatAction) => {
            const index = flatList.findIndex((a) => a.id === action.id);
            if (index >= 0) {
                setActiveIndex(index);
            }
        },
        [flatList]
    );

    if (!isOpen) {
        return null;
    }

    const hasResults = flatList.length > 0;

    // Determine content: loading → error → actions → empty
    const renderContent = (): React.ReactElement => {
        if (isLoading) {
            return React.createElement(
                "div",
                { className: styles.loadingState, "data-testid": "action-menu-loading" },
                React.createElement(Spinner, { size: "small", label: "Loading actions..." })
            );
        }

        if (errorMessage) {
            return React.createElement(
                "div",
                { className: styles.errorState, "data-testid": "action-menu-error" },
                React.createElement(
                    MessageBar,
                    { intent: "error" },
                    React.createElement(MessageBarBody, null, errorMessage)
                )
            );
        }

        if (hasResults) {
            return React.createElement(
                React.Fragment,
                null,
                ...groups.map((group) =>
                    React.createElement(
                        "div",
                        {
                            key: group.category,
                            role: "group",
                            "aria-label": group.label,
                        },
                        // Category header
                        React.createElement(
                            "div",
                            { className: styles.categoryHeader },
                            React.createElement(
                                "span",
                                { className: styles.categoryIcon },
                                group.icon
                            ),
                            React.createElement(Text, { size: 100 }, group.label)
                        ),
                        // Action items
                        group.items.map((action) => {
                            const flatIndex = flatList.findIndex(
                                (a) => a.id === action.id
                            );
                            const isActive = flatIndex === activeIndex;
                            const isDisabled = !!action.disabled;

                            return React.createElement(
                                "div",
                                {
                                    key: action.id,
                                    id: `action-menu-item-${action.id}`,
                                    "data-action-id": action.id,
                                    className: mergeClasses(
                                        styles.menuItem,
                                        isActive && styles.menuItemActive,
                                        isDisabled && styles.menuItemDisabled
                                    ),
                                    role: "option",
                                    "aria-selected": isActive,
                                    "aria-disabled": isDisabled,
                                    onClick: () => handleItemClick(action),
                                    onMouseEnter: () =>
                                        handleItemMouseEnter(action),
                                    "data-testid": `action-menu-item-${action.id}`,
                                },
                                // Label + description
                                React.createElement(
                                    "div",
                                    { className: styles.menuItemContent },
                                    React.createElement(HighlightedText, {
                                        text: action.label,
                                        highlight: filterText,
                                        className: styles.menuItemLabel,
                                    }),
                                    action.description
                                        ? React.createElement(
                                              HighlightedText,
                                              {
                                                  text: action.description,
                                                  highlight: filterText,
                                                  className:
                                                      styles.menuItemDescription,
                                              }
                                          )
                                        : null
                                ),
                                // Shortcut badge
                                action.shortcut
                                    ? React.createElement(
                                          "span",
                                          {
                                              className: styles.shortcutBadge,
                                              "aria-label": `Keyboard shortcut: ${action.shortcut}`,
                                          },
                                          action.shortcut
                                      )
                                    : null
                            );
                        })
                    )
                )
            );
        }

        return React.createElement(
            "div",
            { className: styles.emptyState, "data-testid": "action-menu-empty" },
            React.createElement(
                Text,
                { size: 200 },
                "No matching actions"
            )
        );
    };

    return React.createElement(
        "div",
        {
            ref: menuRef,
            className: styles.overlay,
            role: "listbox",
            "aria-label": "Action menu",
            "aria-activedescendant": hasResults && !isLoading && !errorMessage
                ? `action-menu-item-${flatList[activeIndex]?.id}`
                : undefined,
            tabIndex: 0,
            onKeyDown: handleKeyDown,
            "data-testid": "sprkchat-action-menu",
        },
        React.createElement(
            "div",
            { className: styles.listContainer },
            renderContent()
        )
    );
});

SprkChatActionMenu.displayName = "SprkChatActionMenu";

export default SprkChatActionMenu;
