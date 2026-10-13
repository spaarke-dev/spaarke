/**
 * Entry point of the `sprk_/scripts/bff_auth.js` web resource bundle (built by
 * `scripts/build-webresource.mjs`). Installs `Spaarke.BffAuth` on the form's window.
 * Not exported from the package index: only the bundle runs it.
 */
import { installBffAuth } from './bffAuth';

installBffAuth(window as unknown as Window & { Spaarke?: Record<string, unknown> });
