/**
 * CommandExecutor - Executes commands with error handling
 */
export class CommandExecutor {
    /**
     * Execute a command with error handling and confirmation
     */
    static async execute(command, context) {
        try {
            // Validation: Check if selection required
            if (command.requiresSelection && context.selectedRecords.length === 0) {
                throw new Error(`${command.label} requires at least one record to be selected`);
            }
            // Validation: Check single selection
            if (command.requiresSelection && !command.multiSelectSupport && context.selectedRecords.length > 1) {
                throw new Error(`${command.label} can only be performed on a single record`);
            }
            // Confirmation dialog (if required)
            if (command.confirmationMessage) {
                const confirmed = window.confirm(command.confirmationMessage);
                if (!confirmed) {
                    return; // User cancelled
                }
            }
            // Execute command
            await command.handler(context);
        }
        catch (error) {
            console.error(`Command '${command.key}' failed:`, error);
            throw error; // Re-throw for caller to handle
        }
    }
    /**
     * Check if command can be executed (validation only, no execution)
     */
    static canExecute(command, context) {
        if (command.requiresSelection && context.selectedRecords.length === 0) {
            return false;
        }
        if (command.requiresSelection && !command.multiSelectSupport && context.selectedRecords.length > 1) {
            return false;
        }
        return true;
    }
}
//# sourceMappingURL=CommandExecutor.js.map