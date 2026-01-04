/**
 * ToolbarPlugin - Rich text formatting toolbar for Lexical editor
 *
 * Provides formatting buttons using Fluent UI v9 components:
 * - Text formatting: Bold, Italic, Underline, Strikethrough
 * - Block formatting: Headings (H1, H2, H3)
 * - Lists: Ordered, Unordered
 * - Indentation: Indent, Outdent
 * - Links: Insert/Edit hyperlinks
 * - History: Undo, Redo
 */

import * as React from "react";
import { useCallback, useEffect, useState } from "react";
import {
    makeStyles,
    tokens,
    Toolbar,
    ToolbarButton,
    ToolbarDivider,
    Popover,
    PopoverTrigger,
    PopoverSurface,
    Input,
    Button,
    Label,
    Menu,
    MenuTrigger,
    MenuPopover,
    MenuList,
    MenuItem,
    MenuDivider
} from "@fluentui/react-components";
import {
    TextBoldRegular,
    TextItalicRegular,
    TextUnderlineRegular,
    TextStrikethroughRegular,
    TextHeader1Regular,
    TextHeader2Regular,
    TextHeader3Regular,
    TextBulletListRegular,
    TextNumberListLtrRegular,
    TextIndentIncreaseRegular,
    TextIndentDecreaseRegular,
    LinkRegular,
    ArrowUndoRegular,
    ArrowRedoRegular,
    MoreHorizontalRegular
} from "@fluentui/react-icons";

import { useLexicalComposerContext } from "@lexical/react/LexicalComposerContext";
import {
    $getSelection,
    $isRangeSelection,
    CAN_REDO_COMMAND,
    CAN_UNDO_COMMAND,
    COMMAND_PRIORITY_CRITICAL,
    FORMAT_TEXT_COMMAND,
    REDO_COMMAND,
    UNDO_COMMAND,
    SELECTION_CHANGE_COMMAND,
    INDENT_CONTENT_COMMAND,
    OUTDENT_CONTENT_COMMAND
} from "lexical";
import { $isHeadingNode, $createHeadingNode, HeadingTagType } from "@lexical/rich-text";
import {
    INSERT_ORDERED_LIST_COMMAND,
    INSERT_UNORDERED_LIST_COMMAND,
    REMOVE_LIST_COMMAND,
    $isListNode,
    ListNode
} from "@lexical/list";
import { $setBlocksType } from "@lexical/selection";
import { $getNearestNodeOfType } from "@lexical/utils";
import { $isLinkNode, TOGGLE_LINK_COMMAND } from "@lexical/link";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

interface ToolbarPluginProps {
    isDarkMode?: boolean;
}

type BlockType = "paragraph" | "h1" | "h2" | "h3" | "quote" | "ul" | "ol";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    toolbar: {
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground2,
        padding: "4px 8px",
        flexWrap: "nowrap",
        gap: "2px",
        overflow: "hidden"
    },
    toolbarDark: {
        backgroundColor: tokens.colorNeutralBackground4
    },
    buttonActive: {
        backgroundColor: tokens.colorNeutralBackground1Pressed
    },
    menuItemActive: {
        backgroundColor: tokens.colorNeutralBackground1Hover
    },
    linkPopover: {
        display: "flex",
        flexDirection: "column",
        gap: "8px",
        padding: "8px",
        minWidth: "280px"
    },
    linkActions: {
        display: "flex",
        gap: "8px",
        justifyContent: "flex-end"
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export function ToolbarPlugin({ isDarkMode = false }: ToolbarPluginProps): React.ReactElement {
    const [editor] = useLexicalComposerContext();
    const styles = useStyles();

    // State for formatting
    const [isBold, setIsBold] = useState(false);
    const [isItalic, setIsItalic] = useState(false);
    const [isUnderline, setIsUnderline] = useState(false);
    const [isStrikethrough, setIsStrikethrough] = useState(false);
    const [blockType, setBlockType] = useState<BlockType>("paragraph");
    const [canUndo, setCanUndo] = useState(false);
    const [canRedo, setCanRedo] = useState(false);
    const [isLink, setIsLink] = useState(false);
    const [linkUrl, setLinkUrl] = useState("");
    const [isLinkPopoverOpen, setIsLinkPopoverOpen] = useState(false);

    // Update toolbar state based on selection
    const updateToolbar = useCallback(() => {
        const selection = $getSelection();
        if ($isRangeSelection(selection)) {
            // Text formatting
            setIsBold(selection.hasFormat("bold"));
            setIsItalic(selection.hasFormat("italic"));
            setIsUnderline(selection.hasFormat("underline"));
            setIsStrikethrough(selection.hasFormat("strikethrough"));

            // Check if selection is inside a link
            const node = selection.anchor.getNode();
            const parent = node.getParent();
            if ($isLinkNode(parent)) {
                setIsLink(true);
                setLinkUrl(parent.getURL());
            } else if ($isLinkNode(node)) {
                setIsLink(true);
                setLinkUrl(node.getURL());
            } else {
                setIsLink(false);
                setLinkUrl("");
            }

            // Block type
            const anchorNode = selection.anchor.getNode();
            const element = anchorNode.getKey() === "root"
                ? anchorNode
                : anchorNode.getTopLevelElementOrThrow();
            const elementKey = element.getKey();
            const elementDOM = editor.getElementByKey(elementKey);

            if (elementDOM !== null) {
                if ($isListNode(element)) {
                    const parentList = $getNearestNodeOfType(anchorNode, ListNode);
                    const type = parentList
                        ? parentList.getListType()
                        : element.getListType();
                    setBlockType(type === "number" ? "ol" : "ul");
                } else {
                    const type = $isHeadingNode(element)
                        ? element.getTag()
                        : element.getType();
                    if (type === "h1" || type === "h2" || type === "h3") {
                        setBlockType(type);
                    } else if (type === "quote") {
                        setBlockType("quote");
                    } else {
                        setBlockType("paragraph");
                    }
                }
            }
        }
    }, [editor]);

    // Register listeners
    useEffect(() => {
        return editor.registerCommand(
            SELECTION_CHANGE_COMMAND,
            () => {
                updateToolbar();
                return false;
            },
            COMMAND_PRIORITY_CRITICAL
        );
    }, [editor, updateToolbar]);

    useEffect(() => {
        return editor.registerCommand(
            CAN_UNDO_COMMAND,
            (payload) => {
                setCanUndo(payload);
                return false;
            },
            COMMAND_PRIORITY_CRITICAL
        );
    }, [editor]);

    useEffect(() => {
        return editor.registerCommand(
            CAN_REDO_COMMAND,
            (payload) => {
                setCanRedo(payload);
                return false;
            },
            COMMAND_PRIORITY_CRITICAL
        );
    }, [editor]);

    // Format handlers
    const formatBold = () => {
        editor.dispatchCommand(FORMAT_TEXT_COMMAND, "bold");
    };

    const formatItalic = () => {
        editor.dispatchCommand(FORMAT_TEXT_COMMAND, "italic");
    };

    const formatUnderline = () => {
        editor.dispatchCommand(FORMAT_TEXT_COMMAND, "underline");
    };

    const formatStrikethrough = () => {
        editor.dispatchCommand(FORMAT_TEXT_COMMAND, "strikethrough");
    };

    const formatHeading = (headingType: HeadingTagType) => {
        editor.update(() => {
            const selection = $getSelection();
            if ($isRangeSelection(selection)) {
                $setBlocksType(selection, () => $createHeadingNode(headingType));
            }
        });
    };

    const formatBulletList = () => {
        if (blockType !== "ul") {
            editor.dispatchCommand(INSERT_UNORDERED_LIST_COMMAND, undefined);
        } else {
            editor.dispatchCommand(REMOVE_LIST_COMMAND, undefined);
        }
    };

    const formatNumberedList = () => {
        if (blockType !== "ol") {
            editor.dispatchCommand(INSERT_ORDERED_LIST_COMMAND, undefined);
        } else {
            editor.dispatchCommand(REMOVE_LIST_COMMAND, undefined);
        }
    };

    const indent = () => {
        editor.dispatchCommand(INDENT_CONTENT_COMMAND, undefined);
    };

    const outdent = () => {
        editor.dispatchCommand(OUTDENT_CONTENT_COMMAND, undefined);
    };

    const insertLink = () => {
        if (isLink) {
            // If already a link, populate with current URL for editing
            setIsLinkPopoverOpen(true);
        } else {
            // New link
            setLinkUrl("");
            setIsLinkPopoverOpen(true);
        }
    };

    const applyLink = () => {
        if (linkUrl.trim()) {
            // Ensure URL has protocol
            let url = linkUrl.trim();
            if (!/^https?:\/\//i.test(url)) {
                url = "https://" + url;
            }
            editor.dispatchCommand(TOGGLE_LINK_COMMAND, url);
        } else {
            // Remove link if URL is empty
            editor.dispatchCommand(TOGGLE_LINK_COMMAND, null);
        }
        setIsLinkPopoverOpen(false);
    };

    const removeLink = () => {
        editor.dispatchCommand(TOGGLE_LINK_COMMAND, null);
        setIsLinkPopoverOpen(false);
        setLinkUrl("");
    };

    const undo = () => {
        editor.dispatchCommand(UNDO_COMMAND, undefined);
    };

    const redo = () => {
        editor.dispatchCommand(REDO_COMMAND, undefined);
    };

    const toolbarClass = isDarkMode
        ? `${styles.toolbar} ${styles.toolbarDark}`
        : styles.toolbar;

    // Using native title attributes instead of Tooltip to avoid portal rendering issues in PCF
    return (
        <Toolbar className={toolbarClass} size="small">
            {/* Undo/Redo */}
            <ToolbarButton
                icon={<ArrowUndoRegular />}
                onClick={undo}
                disabled={!canUndo}
                aria-label="Undo"
                title="Undo (Ctrl+Z)"
            />
            <ToolbarButton
                icon={<ArrowRedoRegular />}
                onClick={redo}
                disabled={!canRedo}
                aria-label="Redo"
                title="Redo (Ctrl+Y)"
            />

            <ToolbarDivider />

            {/* Text Formatting */}
            <ToolbarButton
                icon={<TextBoldRegular />}
                onClick={formatBold}
                className={isBold ? styles.buttonActive : undefined}
                aria-label="Bold"
                aria-pressed={isBold}
                title="Bold (Ctrl+B)"
            />
            <ToolbarButton
                icon={<TextItalicRegular />}
                onClick={formatItalic}
                className={isItalic ? styles.buttonActive : undefined}
                aria-label="Italic"
                aria-pressed={isItalic}
                title="Italic (Ctrl+I)"
            />
            <ToolbarButton
                icon={<TextUnderlineRegular />}
                onClick={formatUnderline}
                className={isUnderline ? styles.buttonActive : undefined}
                aria-label="Underline"
                aria-pressed={isUnderline}
                title="Underline (Ctrl+U)"
            />
            <ToolbarButton
                icon={<TextStrikethroughRegular />}
                onClick={formatStrikethrough}
                className={isStrikethrough ? styles.buttonActive : undefined}
                aria-label="Strikethrough"
                aria-pressed={isStrikethrough}
                title="Strikethrough"
            />

            <ToolbarDivider />

            {/* Headings */}
            <ToolbarButton
                icon={<TextHeader1Regular />}
                onClick={() => formatHeading("h1")}
                className={blockType === "h1" ? styles.buttonActive : undefined}
                aria-label="Heading 1"
                aria-pressed={blockType === "h1"}
                title="Heading 1"
            />
            <ToolbarButton
                icon={<TextHeader2Regular />}
                onClick={() => formatHeading("h2")}
                className={blockType === "h2" ? styles.buttonActive : undefined}
                aria-label="Heading 2"
                aria-pressed={blockType === "h2"}
                title="Heading 2"
            />
            <ToolbarButton
                icon={<TextHeader3Regular />}
                onClick={() => formatHeading("h3")}
                className={blockType === "h3" ? styles.buttonActive : undefined}
                aria-label="Heading 3"
                aria-pressed={blockType === "h3"}
                title="Heading 3"
            />

            <ToolbarDivider />

            {/* Lists */}
            <ToolbarButton
                icon={<TextBulletListRegular />}
                onClick={formatBulletList}
                className={blockType === "ul" ? styles.buttonActive : undefined}
                aria-label="Bullet List"
                aria-pressed={blockType === "ul"}
                title="Bullet List"
            />
            <ToolbarButton
                icon={<TextNumberListLtrRegular />}
                onClick={formatNumberedList}
                className={blockType === "ol" ? styles.buttonActive : undefined}
                aria-label="Numbered List"
                aria-pressed={blockType === "ol"}
                title="Numbered List"
            />

            <ToolbarDivider />

            {/* Overflow Menu - Less frequent tools */}
            <Menu>
                <MenuTrigger disableButtonEnhancement>
                    <ToolbarButton
                        icon={<MoreHorizontalRegular />}
                        aria-label="More formatting options"
                        title="More formatting options"
                    />
                </MenuTrigger>
                <MenuPopover>
                    <MenuList>
                        <MenuItem
                            icon={<TextIndentDecreaseRegular />}
                            onClick={outdent}
                        >
                            Decrease Indent
                        </MenuItem>
                        <MenuItem
                            icon={<TextIndentIncreaseRegular />}
                            onClick={indent}
                        >
                            Increase Indent
                        </MenuItem>
                        <MenuDivider />
                        <MenuItem
                            icon={<LinkRegular />}
                            onClick={insertLink}
                            className={isLink ? styles.menuItemActive : undefined}
                        >
                            {isLink ? "Edit Link" : "Insert Link"}
                        </MenuItem>
                        {isLink && (
                            <MenuItem onClick={removeLink}>
                                Remove Link
                            </MenuItem>
                        )}
                    </MenuList>
                </MenuPopover>
            </Menu>

            {/* Link URL Popover - opens when link menu item is clicked */}
            <Popover
                open={isLinkPopoverOpen}
                onOpenChange={(_, data) => setIsLinkPopoverOpen(data.open)}
                positioning="below-start"
            >
                <PopoverTrigger disableButtonEnhancement>
                    <span style={{ display: "none" }} />
                </PopoverTrigger>
                <PopoverSurface>
                    <div className={styles.linkPopover}>
                        <Label htmlFor="link-url">URL</Label>
                        <Input
                            id="link-url"
                            placeholder="https://example.com"
                            value={linkUrl}
                            onChange={(_, data) => setLinkUrl(data.value)}
                            onKeyDown={(e) => {
                                if (e.key === "Enter") {
                                    e.preventDefault();
                                    applyLink();
                                }
                            }}
                            autoFocus
                        />
                        <div className={styles.linkActions}>
                            <Button appearance="secondary" onClick={() => setIsLinkPopoverOpen(false)}>
                                Cancel
                            </Button>
                            <Button appearance="primary" onClick={applyLink}>
                                {isLink ? "Update" : "Insert"}
                            </Button>
                        </div>
                    </div>
                </PopoverSurface>
            </Popover>
        </Toolbar>
    );
}

export default ToolbarPlugin;
