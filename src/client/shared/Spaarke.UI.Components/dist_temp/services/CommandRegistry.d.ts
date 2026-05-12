/**
 * CommandRegistry - Manages built-in and custom commands
 */
import { ICommand, IEntityPrivileges } from '../types/CommandTypes';
/**
 * Built-in command handlers
 */
export declare class CommandRegistry {
    /**
     * Create new record
     */
    static createCommand(): ICommand;
    /**
     * Open selected record
     */
    static openCommand(): ICommand;
    /**
     * Delete selected records
     */
    static deleteCommand(): ICommand;
    /**
     * Refresh dataset
     */
    static refreshCommand(): ICommand;
    /**
     * Upload file (example custom command)
     */
    static uploadCommand(): ICommand;
    /**
     * Get command by key
     */
    static getCommand(key: string): ICommand | undefined;
    /**
     * Get multiple commands by keys, filtered by user privileges
     */
    static getCommands(keys: string[], privileges?: IEntityPrivileges): ICommand[];
    /**
     * Get commands including custom commands from entity configuration
     */
    static getCommandsWithCustom(keys: string[], entityLogicalName: string, privileges?: IEntityPrivileges): ICommand[];
    /**
     * Check if user has required privilege for a command
     */
    private static hasRequiredPrivilege;
}
//# sourceMappingURL=CommandRegistry.d.ts.map