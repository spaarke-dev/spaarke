import { ICommand, ICommandContext } from '../types/CommandTypes';
export interface UseKeyboardShortcutsOptions {
    commands: ICommand[];
    context: ICommandContext;
    enabled?: boolean;
}
/**
 * Hook to register keyboard shortcuts for commands
 */
export declare function useKeyboardShortcuts(options: UseKeyboardShortcutsOptions): void;
//# sourceMappingURL=useKeyboardShortcuts.d.ts.map