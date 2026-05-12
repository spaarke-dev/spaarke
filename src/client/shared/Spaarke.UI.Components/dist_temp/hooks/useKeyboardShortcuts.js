import { useEffect } from 'react';
import { CommandExecutor } from '../services/CommandExecutor';
/**
 * Hook to register keyboard shortcuts for commands
 */
export function useKeyboardShortcuts(options) {
    const { commands, context, enabled = true } = options;
    useEffect(() => {
        if (!enabled)
            return;
        const handleKeyDown = async (event) => {
            // Build shortcut key (e.g., "Ctrl+N", "F5", "Delete")
            const parts = [];
            if (event.ctrlKey || event.metaKey)
                parts.push('Ctrl');
            if (event.shiftKey)
                parts.push('Shift');
            if (event.altKey)
                parts.push('Alt');
            // Map key codes to friendly names
            let keyName = event.key;
            if (keyName === ' ')
                keyName = 'Space';
            if (keyName.length === 1)
                keyName = keyName.toUpperCase();
            parts.push(keyName);
            const shortcut = parts.join('+');
            // Find command with matching shortcut
            const command = commands.find(cmd => cmd.keyboardShortcut === shortcut);
            if (!command)
                return;
            // Check if command can execute
            if (!CommandExecutor.canExecute(command, context))
                return;
            // Prevent default browser behavior
            event.preventDefault();
            event.stopPropagation();
            // Execute command
            try {
                await CommandExecutor.execute(command, context);
            }
            catch (error) {
                console.error(`Keyboard shortcut ${shortcut} failed`, error);
            }
        };
        window.addEventListener('keydown', handleKeyDown);
        return () => {
            window.removeEventListener('keydown', handleKeyDown);
        };
    }, [commands, context, enabled]);
}
//# sourceMappingURL=useKeyboardShortcuts.js.map