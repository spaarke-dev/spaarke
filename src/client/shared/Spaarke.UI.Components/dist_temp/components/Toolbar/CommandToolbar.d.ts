/**
 * CommandToolbar - Enhanced toolbar with groups, overflow, and accessibility
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */
import * as React from 'react';
import { ICommand, ICommandContext } from '../../types/CommandTypes';
export interface ICommandToolbarProps {
    commands: ICommand[];
    context: ICommandContext;
    onCommandExecuted?: (commandKey: string) => void;
    compact?: boolean;
    showOverflow?: boolean;
}
export declare const CommandToolbar: React.FC<ICommandToolbarProps>;
//# sourceMappingURL=CommandToolbar.d.ts.map