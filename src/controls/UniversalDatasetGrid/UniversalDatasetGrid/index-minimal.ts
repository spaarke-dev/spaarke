/**
 * Universal Dataset Grid PCF Control - MINIMAL TEST VERSION
 * Purpose: Verify control CAN execute before adding React/Fluent
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private context: ComponentFramework.Context<IInputs>;

    constructor() {
        console.log('🟢 [MINIMAL] Constructor called - Control CAN execute!');
    }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        console.log('🟢 [MINIMAL] Init called - Control IS working!');
        console.log('[MINIMAL] Context:', context);
        console.log('[MINIMAL] Container:', container);

        this.container = container;
        this.context = context;

        // Simple visual confirmation
        container.innerHTML = `
            <div style="padding: 20px; background: #e8f5e9; border: 2px solid #4caf50; border-radius: 4px;">
                <h2 style="color: #2e7d32; margin: 0 0 10px 0;">✅ Universal Dataset Grid - Control Working!</h2>
                <p style="margin: 0; color: #1b5e20;">
                    <strong>Status:</strong> Control loaded successfully and is executing.<br>
                    <strong>Next:</strong> Ready to add React + Fluent UI v9.
                </p>
            </div>
        `;
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        console.log('🟢 [MINIMAL] UpdateView called');
        this.context = context;
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        console.log('🟢 [MINIMAL] Destroy called');
    }
}
