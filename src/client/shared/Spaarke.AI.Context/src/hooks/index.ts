/**
 * @spaarke/ai-context — Hooks
 *
 * React hooks for AI context consumption.
 * Extracted from SprkChat (Wave 1, tasks 010-012).
 *
 * All hooks are standalone — no imports from SprkChat internals.
 * All BFF calls use buildBffApiUrl() + authenticatedFetch() via ChatApiClient.
 */

export { useChatSession } from './useChatSession';
export type { UseChatSessionOptions } from './useChatSession';

export { useChatContextMapping } from './useChatContextMapping';
export type { UseChatContextMappingOptions } from './useChatContextMapping';

export { useChatPlaybooks } from './useChatPlaybooks';
export type { UseChatPlaybooksOptions } from './useChatPlaybooks';

export { useSseStream, parseSseEvent } from './useSseStream';
