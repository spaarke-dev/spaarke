/**
 * Universal Dataset Grid PCF Control
 * Version 2.0.3 - Single React Root Architecture with Fluent UI v9
 */

import * as React from 'react';
import * as ReactDOM from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { UniversalDatasetGridRoot } from "./components/UniversalDatasetGridRoot";
import { resolveTheme } from "./providers/ThemeProvider";
import { DEFAULT_GRID_CONFIG, GridConfiguration } from "./types";

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private root: ReactDOM.Root | null = null;
    private notifyOutputChanged: () => void;
    private config: GridConfiguration;

    constructor() {
        console.log('[UniversalDatasetGrid] Constructor');
        this.config = DEFAULT_GRID_CONFIG;
    }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        console.log('[UniversalDatasetGrid] Init - Creating single React root');

        this.notifyOutputChanged = notifyOutputChanged;

        // Create single React root
        this.root = ReactDOM.createRoot(container);

        // Render React tree
        this.renderReactTree(context);

        console.log('[UniversalDatasetGrid] Init complete');
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        console.log('[UniversalDatasetGrid] UpdateView - Re-rendering with new props');

        // Just re-render with new context - React handles the updates
        this.renderReactTree(context);
    }

    public destroy(): void {
        console.log('[UniversalDatasetGrid] Destroy - Unmounting React root');

        if (this.root) {
            this.root.unmount();
            this.root = null;
        }
    }

    public getOutputs(): IOutputs {
        return {};
    }

    /**
     * Render the React component tree.
     * Called from init() and updateView().
     */
    private renderReactTree(context: ComponentFramework.Context<IInputs>): void {
        if (!this.root) {
            console.error('[UniversalDatasetGrid] Cannot render - root not initialized');
            return;
        }

        const theme = resolveTheme(context);

        this.root.render(
            React.createElement(
                FluentProvider,
                { theme },
                React.createElement(UniversalDatasetGridRoot, {
                    context,
                    notifyOutputChanged: this.notifyOutputChanged,
                    config: this.config
                })
            )
        );
    }
}
