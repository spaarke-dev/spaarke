/**
 * @spaarke/ai-context - AI context providers, service clients, and hooks
 *
 * Standards: ADR-012 (shared library rules), ADR-020 (versioning)
 * Version: 1.0.0
 *
 * NOT PCF-safe — this library uses React 19 APIs.
 * Consumers: SpaarkeAi Code Page, future AI-enabled Code Pages.
 */

// Types
export * from './types';

// Hooks
export * from './hooks';

// Services
export * from './services';

// Providers
export * from './providers';
