/**
 * CustomCommandFactory - Creates ICommand instances from JSON configuration
 */
import { ICommand } from '../types/CommandTypes';
import { ICustomCommandConfiguration } from '../types/EntityConfigurationTypes';
export declare class CustomCommandFactory {
    /**
     * Create ICommand from custom command configuration
     */
    static createCommand(key: string, config: ICustomCommandConfiguration): ICommand;
    /**
     * Execute custom command based on action type
     */
    private static executeCustomCommand;
    /**
     * Execute Custom API
     */
    private static executeCustomApi;
    /**
     * Execute Action (bound or unbound)
     */
    private static executeAction;
    /**
     * Execute Function
     */
    private static executeFunction;
    /**
     * Execute Workflow (Power Automate Flow)
     */
    private static executeWorkflow;
    /**
     * Interpolate parameter values with context tokens
     */
    private static interpolateParameters;
    /**
     * Interpolate string tokens
     */
    private static interpolateString;
    /**
     * Get icon from icon name
     */
    private static getIcon;
}
//# sourceMappingURL=CustomCommandFactory.d.ts.map