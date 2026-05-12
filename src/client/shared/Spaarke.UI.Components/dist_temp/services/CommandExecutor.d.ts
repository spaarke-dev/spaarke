/**
 * CommandExecutor - Executes commands with error handling
 */
import { ICommand, ICommandContext } from '../types/CommandTypes';
export declare class CommandExecutor {
    /**
     * Execute a command with error handling and confirmation
     */
    static execute(command: ICommand, context: ICommandContext): Promise<void>;
    /**
     * Check if command can be executed (validation only, no execution)
     */
    static canExecute(command: ICommand, context: ICommandContext): boolean;
}
//# sourceMappingURL=CommandExecutor.d.ts.map