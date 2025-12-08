import { IInputs, IOutputs } from "./generated/ManifestTypes";

/**
 * ThemeEnforcer PCF Control
 *
 * Lightweight control that enforces user theme preference on app load.
 * Checks localStorage for stored theme and redirects if URL flag doesn't match.
 *
 * No visible UI - just runs init logic and outputs status.
 *
 * @see projects/mda-darkmode-theme/spec.md
 */
export class ThemeEnforcer implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private static readonly STORAGE_KEY = 'spaarke-theme';
    private static readonly DARK_MODE_FLAG = 'flags=themeOption%3Ddarkmode';

    private notifyOutputChanged: () => void;
    private themeStatus: string = 'initialized';
    private hasRedirected: boolean = false;

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.notifyOutputChanged = notifyOutputChanged;

        // Run theme enforcement on init
        this.enforceTheme();

        console.log('[ThemeEnforcer] Initialized, status:', this.themeStatus);
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        // Re-check on view updates (in case of navigation without full reload)
        if (!this.hasRedirected) {
            this.enforceTheme();
        }
    }

    private enforceTheme(): void {
        try {
            // Get the top window (PCF runs in iframe)
            const topWindow = window.top || window;
            const currentUrl = topWindow.location.href;
            const storedTheme = localStorage.getItem(ThemeEnforcer.STORAGE_KEY) || 'auto';
            const hasDarkFlag = currentUrl.indexOf(ThemeEnforcer.DARK_MODE_FLAG) !== -1;

            console.log('[ThemeEnforcer] Checking - stored:', storedTheme, 'hasDarkFlag:', hasDarkFlag);

            // If user wants dark but URL doesn't have flag, redirect to add it
            if (storedTheme === 'dark' && !hasDarkFlag) {
                const separator = currentUrl.indexOf('?') !== -1 ? '&' : '?';
                const newUrl = currentUrl + separator + ThemeEnforcer.DARK_MODE_FLAG;

                console.log('[ThemeEnforcer] Redirecting to dark mode:', newUrl);
                this.themeStatus = 'redirecting-to-dark';
                this.hasRedirected = true;
                this.notifyOutputChanged();

                topWindow.location.href = newUrl;
                return;
            }

            // If user wants light/auto but URL has dark flag, redirect to remove it
            if ((storedTheme === 'light' || storedTheme === 'auto') && hasDarkFlag) {
                const newUrl = currentUrl
                    .replace('&' + ThemeEnforcer.DARK_MODE_FLAG, '')
                    .replace('?' + ThemeEnforcer.DARK_MODE_FLAG + '&', '?')
                    .replace('?' + ThemeEnforcer.DARK_MODE_FLAG, '');

                console.log('[ThemeEnforcer] Redirecting to light mode:', newUrl);
                this.themeStatus = 'redirecting-to-light';
                this.hasRedirected = true;
                this.notifyOutputChanged();

                topWindow.location.href = newUrl;
                return;
            }

            // Theme matches URL, no action needed
            this.themeStatus = hasDarkFlag ? 'dark-mode-active' : 'light-mode-active';
            this.notifyOutputChanged();

        } catch (error) {
            console.error('[ThemeEnforcer] Error:', error);
            this.themeStatus = 'error';
            this.notifyOutputChanged();
        }
    }

    public getOutputs(): IOutputs {
        return {
            themeStatus: this.themeStatus
        };
    }

    public destroy(): void {
        // No cleanup needed
    }
}
