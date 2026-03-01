/**
 * Command Palette Component - Slash command menu for AI Assistant
 *
 * Shows a filterable list of available commands when user types '/'.
 * Inspired by VS Code command palette and Claude Code slash commands.
 *
 * Features:
 * - Fuzzy search filtering
 * - Keyboard navigation (Arrow keys, Enter, Escape)
 * - Grouped by category
 * - Shows shortcuts where available
 *
 * @version 2.0.0 (Code Page migration)
 */

import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Text,
    mergeClasses,
} from "@fluentui/react-components";
import {
    BrainCircuit20Regular,
    Grid20Regular,
    Library20Regular,
    PlayCircle20Regular,
    Question20Regular,
} from "@fluentui/react-icons";
import {
    filterCommands,
    CATEGORY_LABELS,
    CATEGORY_ORDER,
    type SlashCommand,
} from "./commands";

// ============================================================================
// Styles
// ============================================================================

const useStyles = makeStyles({
    container: {
        position: "absolute",
        bottom: "100%",
        left: 0,
        right: 0,
        maxHeight: "280px",
        overflowY: "auto",
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        boxShadow: tokens.shadow16,
        zIndex: 1001,
        marginBottom: tokens.spacingVerticalXS,
    },
    categoryHeader: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(
            tokens.spacingVerticalXS,
            tokens.spacingHorizontalM
        ),
        backgroundColor: tokens.colorNeutralBackground3,
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        position: "sticky",
        top: 0,
        zIndex: 1,
    },
    categoryIcon: {
        fontSize: "16px",
    },
    commandItem: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.padding(
            tokens.spacingVerticalS,
            tokens.spacingHorizontalM
        ),
        cursor: "pointer",
        ":hover": {
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
    },
    commandItemSelected: {
        backgroundColor: tokens.colorBrandBackground2,
        ":hover": {
            backgroundColor: tokens.colorBrandBackground2Hover,
        },
    },
    commandHeader: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
    commandName: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    commandSlash: {
        color: tokens.colorNeutralForeground4,
        fontFamily: "monospace",
    },
    commandLabel: {
        fontWeight: tokens.fontWeightSemibold,
    },
    commandShortcut: {
        color: tokens.colorNeutralForeground4,
        fontSize: tokens.fontSizeBase100,
        fontFamily: "monospace",
        backgroundColor: tokens.colorNeutralBackground3,
        ...shorthands.padding("2px", tokens.spacingHorizontalXS),
        ...shorthands.borderRadius(tokens.borderRadiusSmall),
    },
    commandDescription: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
        marginTop: "2px",
    },
    emptyState: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        ...shorthands.padding(tokens.spacingVerticalL),
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
    },
    hint: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.padding(
            tokens.spacingVerticalXS,
            tokens.spacingHorizontalM
        ),
        backgroundColor: tokens.colorNeutralBackground2,
        ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke1),
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
    },
    hintKey: {
        fontFamily: "monospace",
        backgroundColor: tokens.colorNeutralBackground3,
        ...shorthands.padding("1px", "4px"),
        ...shorthands.borderRadius(tokens.borderRadiusSmall),
        marginRight: "4px",
    },
});

// ============================================================================
// Category Icons
// ============================================================================

const CATEGORY_ICONS: Record<SlashCommand["category"], React.ReactNode> = {
    nodes: <BrainCircuit20Regular />,
    canvas: <Grid20Regular />,
    scopes: <Library20Regular />,
    test: <PlayCircle20Regular />,
    help: <Question20Regular />,
};

// ============================================================================
// Props
// ============================================================================

export interface CommandPaletteProps {
    /** Current filter query (text after /) */
    query: string;
    /** Called when a command is selected */
    onSelectCommand: (command: SlashCommand, args: string) => void;
    /** Called when palette should close */
    onClose: () => void;
    /** Whether the palette is visible */
    isOpen: boolean;
}

// ============================================================================
// Component
// ============================================================================

export const CommandPalette: React.FC<CommandPaletteProps> = ({
    query,
    onSelectCommand,
    onClose,
    isOpen,
}) => {
    const styles = useStyles();
    const containerRef = useRef<HTMLDivElement>(null);
    const [selectedIndex, setSelectedIndex] = useState(0);

    // Filter commands based on query
    const filteredCommands = useMemo(() => filterCommands(query), [query]);

    // Group filtered commands by category
    const groupedCommands = useMemo(() => {
        const groups: Record<string, SlashCommand[]> = {};

        for (const cmd of filteredCommands) {
            if (!groups[cmd.category]) {
                groups[cmd.category] = [];
            }
            groups[cmd.category].push(cmd);
        }

        return groups;
    }, [filteredCommands]);

    // Flat list for keyboard navigation
    const flatCommands = useMemo(() => {
        const result: SlashCommand[] = [];
        for (const category of CATEGORY_ORDER) {
            if (groupedCommands[category]) {
                result.push(...groupedCommands[category]);
            }
        }
        return result;
    }, [groupedCommands]);

    // Reset selection when query changes
    useEffect(() => {
        setSelectedIndex(0);
    }, [query]);

    // Scroll selected item into view
    useEffect(() => {
        if (!containerRef.current || flatCommands.length === 0) return;

        const selectedElement = containerRef.current.querySelector(
            `[data-index="${selectedIndex}"]`
        );
        if (selectedElement) {
            selectedElement.scrollIntoView({ block: "nearest" });
        }
    }, [selectedIndex, flatCommands.length]);

    // Handle keyboard navigation
    const handleKeyDown = useCallback(
        (e: KeyboardEvent) => {
            if (!isOpen || flatCommands.length === 0) return;

            switch (e.key) {
                case "ArrowDown":
                    e.preventDefault();
                    setSelectedIndex((prev) =>
                        prev < flatCommands.length - 1 ? prev + 1 : 0
                    );
                    break;
                case "ArrowUp":
                    e.preventDefault();
                    setSelectedIndex((prev) =>
                        prev > 0 ? prev - 1 : flatCommands.length - 1
                    );
                    break;
                case "Enter":
                    e.preventDefault();
                    if (flatCommands[selectedIndex]) {
                        onSelectCommand(flatCommands[selectedIndex], "");
                    }
                    break;
                case "Escape":
                    e.preventDefault();
                    onClose();
                    break;
                case "Tab":
                    // Tab completes the command name
                    e.preventDefault();
                    if (flatCommands[selectedIndex]) {
                        onSelectCommand(flatCommands[selectedIndex], "");
                    }
                    break;
            }
        },
        [isOpen, flatCommands, selectedIndex, onSelectCommand, onClose]
    );

    // Attach keyboard listener
    useEffect(() => {
        if (isOpen) {
            window.addEventListener("keydown", handleKeyDown);
            return () => window.removeEventListener("keydown", handleKeyDown);
        }
    }, [isOpen, handleKeyDown]);

    // Handle item click
    const handleItemClick = useCallback(
        (command: SlashCommand) => {
            onSelectCommand(command, "");
        },
        [onSelectCommand]
    );

    if (!isOpen) return null;

    // Track global index for keyboard nav
    let globalIndex = 0;

    return (
        <div className={styles.container} ref={containerRef} data-command-palette>
            {flatCommands.length === 0 ? (
                <div className={styles.emptyState}>
                    No commands found for &quot;{query}&quot;
                </div>
            ) : (
                <>
                    {CATEGORY_ORDER.map((category) => {
                        const commands = groupedCommands[category];
                        if (!commands || commands.length === 0) return null;

                        return (
                            <div key={category}>
                                <div className={styles.categoryHeader}>
                                    <span className={styles.categoryIcon}>
                                        {CATEGORY_ICONS[category]}
                                    </span>
                                    <Text>{CATEGORY_LABELS[category]}</Text>
                                </div>
                                {commands.map((cmd) => {
                                    const index = globalIndex++;
                                    const isSelected = index === selectedIndex;

                                    return (
                                        <div
                                            key={cmd.name}
                                            data-index={index}
                                            className={mergeClasses(
                                                styles.commandItem,
                                                isSelected && styles.commandItemSelected
                                            )}
                                            onClick={() => handleItemClick(cmd)}
                                            onMouseEnter={() => setSelectedIndex(index)}
                                            role="option"
                                            aria-selected={isSelected}
                                        >
                                            <div className={styles.commandHeader}>
                                                <div className={styles.commandName}>
                                                    <span className={styles.commandSlash}>/</span>
                                                    <Text className={styles.commandLabel}>
                                                        {cmd.name}
                                                    </Text>
                                                    {cmd.argsHint && (
                                                        <Text
                                                            size={200}
                                                            style={{
                                                                color: tokens.colorNeutralForeground4,
                                                            }}
                                                        >
                                                            {cmd.argsHint}
                                                        </Text>
                                                    )}
                                                </div>
                                                {cmd.shortcut && (
                                                    <span className={styles.commandShortcut}>
                                                        {cmd.shortcut}
                                                    </span>
                                                )}
                                            </div>
                                            <Text className={styles.commandDescription}>
                                                {cmd.description}
                                            </Text>
                                        </div>
                                    );
                                })}
                            </div>
                        );
                    })}
                </>
            )}
            <div className={styles.hint}>
                <span>
                    <span className={styles.hintKey}>&#8593;&#8595;</span> navigate
                    <span className={styles.hintKey}>&#8629;</span> select
                    <span className={styles.hintKey}>esc</span> close
                </span>
            </div>
        </div>
    );
};

export default CommandPalette;
