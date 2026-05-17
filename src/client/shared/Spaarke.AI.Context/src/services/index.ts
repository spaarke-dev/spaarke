/**
 * @spaarke/ai-context — Services
 *
 * Service clients for AI context operations.
 * Extracted from SprkChat (Wave 1, tasks 010-012).
 *
 * All BFF URL construction MUST use buildBffApiUrl() from @spaarke/auth.
 * All non-streaming requests use authenticatedFetch() from @spaarke/auth.
 */

export { ChatApiClient } from './ChatApiClient';
