/**
 * Universal Dataset Grid PCF Control
 * Version 2.0.7 - Single React Root Architecture with Fluent UI v9
 */

import * as React from 'react';
import * as ReactDOM from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { UniversalDatasetGridRoot } from "./components/UniversalDatasetGridRoot";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { resolveTheme } from "./providers/ThemeProvider";
import { DEFAULT_GRID_CONFIG, GridConfiguration } from "./types";
import { logger } from "./utils/logger";

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private root: ReactDOM.Root | null = null;
    private notifyOutputChanged: () => void;
    private config: GridConfiguration;

    constructor() {
        logger.info('Control', 'Constructor called');
        this.config = DEFAULT_GRID_CONFIG;
    }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        try {
            logger.info('Control', 'Init - Creating single React root');

            this.notifyOutputChanged = notifyOutputChanged;

            // Create single React root
            this.root = ReactDOM.createRoot(container);

            // Render React tree
            this.renderReactTree(context);

            logger.info('Control', 'Init complete');
        } catch (error) {
            logger.error('Control', 'Init failed', error);
            throw error;
        }
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        try {
            logger.debug('Control', 'UpdateView - Re-rendering with new props');

            // Just re-render with new context - React handles the updates
            this.renderReactTree(context);
        } catch (error) {
            logger.error('Control', 'UpdateView failed', error);
        }
    }

    public destroy(): void {
        try {
            logger.info('Control', 'Destroy - Unmounting React root');

            if (this.root) {
                this.root.unmount();
                this.root = null;
            }
        } catch (error) {
            logger.error('Control', 'Destroy failed', error);
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
            logger.error('Control', 'Cannot render - root not initialized');
            return;
        }

        try {
            const theme = resolveTheme(context);

            this.root.render(
                React.createElement(
                    FluentProvider,
                    { theme },
                    React.createElement(
                        ErrorBoundary,
                        null,
                        React.createElement(UniversalDatasetGridRoot, {
                            context,
                            notifyOutputChanged: this.notifyOutputChanged,
                            config: this.config
                        })
                    )
                )
            );
        } catch (error) {
            logger.error('Control', 'Render failed', error);
            throw error;
        }
    }
}
