/**
 * CommandToolbar - Enhanced toolbar with groups, overflow, and accessibility
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */
import * as React from 'react';
import { Toolbar, ToolbarButton, ToolbarDivider, ToolbarGroup, Menu, MenuTrigger, MenuPopover, MenuList, MenuItem, Tooltip, makeStyles, tokens, Spinner, } from '@fluentui/react-components';
import { MoreHorizontal20Regular } from '@fluentui/react-icons';
import { CommandExecutor } from '../../services/CommandExecutor';
const useStyles = makeStyles({
    toolbar: {
        backgroundColor: tokens.colorNeutralBackground1,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        minHeight: '44px',
    },
    toolbarCompact: {
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        minHeight: '36px',
    },
    shortcut: {
        marginLeft: tokens.spacingHorizontalM,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
});
export const CommandToolbar = props => {
    const styles = useStyles();
    const [executingCommand, setExecutingCommand] = React.useState(null);
    // Group commands
    const { primaryCommands, secondaryCommands, overflowCommands } = React.useMemo(() => {
        const primary = [];
        const secondary = [];
        const overflow = [];
        props.commands.forEach(cmd => {
            const group = cmd.group ?? 'primary';
            if (group === 'primary')
                primary.push(cmd);
            else if (group === 'secondary')
                secondary.push(cmd);
            else
                overflow.push(cmd);
        });
        // Auto-overflow: If >8 commands total, move secondary to overflow
        const showOverflow = props.showOverflow ?? true;
        if (showOverflow && primary.length + secondary.length > 8) {
            overflow.unshift(...secondary);
            secondary.length = 0;
        }
        return {
            primaryCommands: primary,
            secondaryCommands: secondary,
            overflowCommands: overflow,
        };
    }, [props.commands, props.showOverflow]);
    // Execute command
    const handleCommandClick = React.useCallback(async (command) => {
        setExecutingCommand(command.key);
        try {
            await CommandExecutor.execute(command, props.context);
            if (props.onCommandExecuted) {
                props.onCommandExecuted(command.key);
            }
        }
        catch (error) {
            console.error(`Command ${command.key} failed`, error);
        }
        finally {
            setExecutingCommand(null);
        }
    }, [props]);
    // Render command button
    const renderCommandButton = (command) => {
        const canExecute = CommandExecutor.canExecute(command, props.context);
        const isExecuting = executingCommand === command.key;
        const showIconOnly = props.compact || command.iconOnly;
        const button = (React.createElement(ToolbarButton, { key: command.key, icon: isExecuting ? React.createElement(Spinner, { size: "tiny" }) : command.icon, disabled: !canExecute || isExecuting, onClick: () => handleCommandClick(command), "aria-label": command.description || command.label, "aria-keyshortcuts": command.keyboardShortcut }, !showIconOnly && command.label));
        // Wrap with tooltip if icon-only or has description
        if (showIconOnly || command.description) {
            const tooltipContent = (React.createElement(React.Fragment, null,
                command.label,
                command.description && React.createElement("div", null, command.description),
                command.keyboardShortcut && React.createElement("span", { className: styles.shortcut }, command.keyboardShortcut)));
            return (React.createElement(Tooltip, { key: command.key, content: tooltipContent, relationship: "description" }, button));
        }
        return button;
    };
    // Render overflow menu
    const renderOverflowMenu = () => {
        if (overflowCommands.length === 0)
            return null;
        return (React.createElement(Menu, null,
            React.createElement(MenuTrigger, { disableButtonEnhancement: true },
                React.createElement(Tooltip, { content: "More commands", relationship: "label" },
                    React.createElement(ToolbarButton, { "aria-label": "More commands", icon: React.createElement(MoreHorizontal20Regular, null) }))),
            React.createElement(MenuPopover, null,
                React.createElement(MenuList, null, overflowCommands.map(command => {
                    const canExecute = CommandExecutor.canExecute(command, props.context);
                    const isExecuting = executingCommand === command.key;
                    return (React.createElement(MenuItem, { key: command.key, icon: isExecuting ? React.createElement(Spinner, { size: "tiny" }) : command.icon, disabled: !canExecute || isExecuting, onClick: () => handleCommandClick(command), secondaryContent: command.keyboardShortcut }, command.label));
                })))));
    };
    return (React.createElement(Toolbar, { "aria-label": "Command toolbar", className: `${styles.toolbar} ${props.compact ? styles.toolbarCompact : ''}` },
        primaryCommands.length > 0 && (React.createElement(ToolbarGroup, null, primaryCommands.map(command => (React.createElement(React.Fragment, { key: command.key },
            renderCommandButton(command),
            command.dividerAfter && React.createElement(ToolbarDivider, null)))))),
        primaryCommands.length > 0 && secondaryCommands.length > 0 && React.createElement(ToolbarDivider, null),
        secondaryCommands.length > 0 && (React.createElement(ToolbarGroup, null, secondaryCommands.map(command => renderCommandButton(command)))),
        overflowCommands.length > 0 && (React.createElement(React.Fragment, null,
            (primaryCommands.length > 0 || secondaryCommands.length > 0) && React.createElement(ToolbarDivider, null),
            React.createElement(ToolbarGroup, null, renderOverflowMenu())))));
};
//# sourceMappingURL=CommandToolbar.js.map