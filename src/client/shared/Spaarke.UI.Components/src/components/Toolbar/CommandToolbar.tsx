/**
 * CommandToolbar - Enhanced toolbar with groups, overflow, and accessibility
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import * as React from "react";
import {
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  ToolbarGroup,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Tooltip,
  makeStyles,
  tokens,
  Spinner
} from "@fluentui/react-components";
import { MoreHorizontal20Regular } from "@fluentui/react-icons";
import { ICommand, ICommandContext } from "../../types/CommandTypes";
import { CommandExecutor } from "../../services/CommandExecutor";

export interface ICommandToolbarProps {
  commands: ICommand[];
  context: ICommandContext;
  onCommandExecuted?: (commandKey: string) => void;
  compact?: boolean; // Icon-only mode
  showOverflow?: boolean; // Enable overflow menu (default: true)
}

const useStyles = makeStyles({
  toolbar: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    minHeight: "44px"
  },
  toolbarCompact: {
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    minHeight: "36px"
  },
  shortcut: {
    marginLeft: tokens.spacingHorizontalM,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3
  }
});

export const CommandToolbar: React.FC<ICommandToolbarProps> = (props) => {
  const styles = useStyles();
  const [executingCommand, setExecutingCommand] = React.useState<string | null>(null);

  // Group commands
  const { primaryCommands, secondaryCommands, overflowCommands } = React.useMemo(() => {
    const primary: ICommand[] = [];
    const secondary: ICommand[] = [];
    const overflow: ICommand[] = [];

    props.commands.forEach((cmd) => {
      const group = cmd.group ?? "primary";
      if (group === "primary") primary.push(cmd);
      else if (group === "secondary") secondary.push(cmd);
      else overflow.push(cmd);
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
      overflowCommands: overflow
    };
  }, [props.commands, props.showOverflow]);

  // Execute command
  const handleCommandClick = React.useCallback(async (command: ICommand) => {
    setExecutingCommand(command.key);

    try {
      await CommandExecutor.execute(command, props.context);

      if (props.onCommandExecuted) {
        props.onCommandExecuted(command.key);
      }
    } catch (error) {
      console.error(`Command ${command.key} failed`, error);
    } finally {
      setExecutingCommand(null);
    }
  }, [props]);

  // Render command button
  const renderCommandButton = (command: ICommand) => {
    const canExecute = CommandExecutor.canExecute(command, props.context);
    const isExecuting = executingCommand === command.key;
    const showIconOnly = props.compact || command.iconOnly;

    const button = (
      <ToolbarButton
        key={command.key}
        icon={isExecuting ? <Spinner size="tiny" /> : command.icon}
        disabled={!canExecute || isExecuting}
        onClick={() => handleCommandClick(command)}
        aria-label={command.description || command.label}
        aria-keyshortcuts={command.keyboardShortcut}
      >
        {!showIconOnly && command.label}
      </ToolbarButton>
    );

    // Wrap with tooltip if icon-only or has description
    if (showIconOnly || command.description) {
      const tooltipContent = (
        <>
          {command.label}
          {command.description && <div>{command.description}</div>}
          {command.keyboardShortcut && (
            <span className={styles.shortcut}>{command.keyboardShortcut}</span>
          )}
        </>
      );

      return (
        <Tooltip key={command.key} content={tooltipContent} relationship="description">
          {button}
        </Tooltip>
      );
    }

    return button;
  };

  // Render overflow menu
  const renderOverflowMenu = () => {
    if (overflowCommands.length === 0) return null;

    return (
      <Menu>
        <MenuTrigger disableButtonEnhancement>
          <Tooltip content="More commands" relationship="label">
            <ToolbarButton
              aria-label="More commands"
              icon={<MoreHorizontal20Regular />}
            />
          </Tooltip>
        </MenuTrigger>
        <MenuPopover>
          <MenuList>
            {overflowCommands.map((command) => {
              const canExecute = CommandExecutor.canExecute(command, props.context);
              const isExecuting = executingCommand === command.key;

              return (
                <MenuItem
                  key={command.key}
                  icon={isExecuting ? <Spinner size="tiny" /> : command.icon}
                  disabled={!canExecute || isExecuting}
                  onClick={() => handleCommandClick(command)}
                  secondaryContent={command.keyboardShortcut}
                >
                  {command.label}
                </MenuItem>
              );
            })}
          </MenuList>
        </MenuPopover>
      </Menu>
    );
  };

  return (
    <Toolbar
      aria-label="Command toolbar"
      className={`${styles.toolbar} ${props.compact ? styles.toolbarCompact : ""}`}
    >
      {/* Primary commands */}
      {primaryCommands.length > 0 && (
        <ToolbarGroup>
          {primaryCommands.map((command) => (
            <React.Fragment key={command.key}>
              {renderCommandButton(command)}
              {command.dividerAfter && <ToolbarDivider />}
            </React.Fragment>
          ))}
        </ToolbarGroup>
      )}

      {/* Divider between primary and secondary */}
      {primaryCommands.length > 0 && secondaryCommands.length > 0 && (
        <ToolbarDivider />
      )}

      {/* Secondary commands */}
      {secondaryCommands.length > 0 && (
        <ToolbarGroup>
          {secondaryCommands.map((command) => renderCommandButton(command))}
        </ToolbarGroup>
      )}

      {/* Overflow menu */}
      {overflowCommands.length > 0 && (
        <>
          {(primaryCommands.length > 0 || secondaryCommands.length > 0) && (
            <ToolbarDivider />
          )}
          <ToolbarGroup>
            {renderOverflowMenu()}
          </ToolbarGroup>
        </>
      )}
    </Toolbar>
  );
};
