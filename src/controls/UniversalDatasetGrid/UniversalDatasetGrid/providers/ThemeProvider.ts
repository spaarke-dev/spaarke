import * as React from 'react';
import * as ReactDOM from 'react-dom/client';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

/**
 * Wraps the PCF control in Fluent UI theme provider.
 *
 * Provides centralized theme management for all Fluent UI components
 * used in the Universal Dataset Grid control.
 */
export class ThemeProvider {
    private root: ReactDOM.Root | null = null;
    private providerContainer: HTMLElement | null = null;
    private contentContainer: HTMLElement | null = null;

    /**
     * Initialize theme provider and render into container.
     *
     * @param container - PCF control container element
     */
    public initialize(container: HTMLDivElement): void {
        console.log('[ThemeProvider] initialize() called', { container });

        if (!container) {
            throw new Error('Container is null or undefined');
        }

        // Create provider container
        this.providerContainer = document.createElement('div');
        this.providerContainer.style.cssText =
            'height: 100%; display: flex; flex-direction: column;';
        this.providerContainer.className = 'universal-grid-container';

        // Add to PCF container first
        container.appendChild(this.providerContainer);
        console.log('[ThemeProvider] Provider container appended to DOM');

        // Create React 18 root
        this.root = ReactDOM.createRoot(this.providerContainer);
        console.log('[ThemeProvider] React root created');

        // Render FluentProvider with theme
        this.root.render(
            React.createElement(
                FluentProvider,
                {
                    theme: webLightTheme,
                    style: {
                        height: '100%',
                        display: 'flex',
                        flexDirection: 'column'
                    }
                },
                React.createElement('div', {
                    ref: (el: HTMLElement | null) => {
                        console.log('[ThemeProvider] Content container ref callback', { el });
                        this.contentContainer = el;
                    },
                    style: {
                        flex: 1,
                        display: 'flex',
                        flexDirection: 'column',
                        overflow: 'auto'
                    },
                    className: 'universal-grid-content'
                })
            )
        );

        console.log('[ThemeProvider] FluentProvider rendered');
    }

    /**
     * Get the content container where control components should render.
     *
     * All grid content, command bars, and dialogs should render into this container
     * to inherit the Fluent UI theme context.
     *
     * @returns Content container element
     * @throws Error if theme provider not initialized
     */
    public getContentContainer(): HTMLElement {
        if (!this.contentContainer) {
            throw new Error('Theme provider not initialized - call initialize() first');
        }
        return this.contentContainer;
    }

    /**
     * Check if theme provider is initialized.
     *
     * @returns True if initialized, false otherwise
     */
    public isInitialized(): boolean {
        return this.contentContainer !== null;
    }

    /**
     * Clean up theme provider and unmount React components.
     *
     * Should be called from the PCF control's destroy() method.
     */
    public destroy(): void {
        if (this.root) {
            this.root.unmount();
            this.root = null;
        }
        this.providerContainer = null;
        this.contentContainer = null;
    }
}
