/**
 * sprk_communicationconversationpage auth initializer.
 *
 * Thin consumer of the canonical `createCodePageAuthInitializer` factory from
 * `@spaarke/auth` (task 032, FR-14b) — mirrors
 * `src/solutions/LegalWorkspace/src/services/authInit.ts` (R2 task 054 / FR-20)
 * exactly, including the lazy-factory-construction rationale below.
 *
 * Lazy factory construction (mirrors LegalWorkspace/DailyBriefing reasoning):
 * this page's runtime config (`getMsalClientId`, `getTenantId`, `getBffBaseUrl`,
 * `getBffOAuthScope`) is NOT available at module load — it's populated by
 * `setRuntimeConfig(...)` in `main.tsx` AFTER `resolveRuntimeConfig()` resolves.
 * The factory captures config values eagerly in its closure, so factory
 * construction is deferred until first use of any exported method. `main.tsx`
 * blocks rendering on `ensureAuthInitialized()` completing (or failing) before
 * mounting `<App />`, so by the time `<ConversationWorkspace />` fires its
 * first `authenticatedFetch` call, auth is already initialized — this
 * eliminates the "Runtime config not initialized" race that a non-blocking
 * bootstrap (DailyBriefing's optional-AI-briefing pattern) would risk for a
 * page whose ENTIRE content is BFF-backed.
 *
 * @see ADR-028 - Spaarke Auth Architecture
 * @see ADR-010 - DI Minimalism
 * @see src/solutions/LegalWorkspace/src/services/authInit.ts — pattern source
 */

import { createCodePageAuthInitializer, type CodePageAuthInitializer } from '@spaarke/auth';
import {
  getBffBaseUrl,
  getBffOAuthScope,
  getMsalClientId,
  getTenantId as getRuntimeTenantId,
} from '../config/runtimeConfig';

let _initializer: CodePageAuthInitializer | null = null;

function getInitializer(): CodePageAuthInitializer {
  if (!_initializer) {
    _initializer = createCodePageAuthInitializer({
      clientId: getMsalClientId(),
      tenantId: getRuntimeTenantId(),
      bffBaseUrl: getBffBaseUrl(),
      bffApiScope: getBffOAuthScope(),
      proactiveRefresh: true,
      logLabel: 'CommunicationConversationPage',
    });
  }
  return _initializer;
}

export function ensureAuthInitialized(): Promise<void> {
  return getInitializer().ensureAuthInitialized();
}

export function authenticatedFetch(url: string, init?: RequestInit): Promise<Response> {
  return getInitializer().authenticatedFetch(url, init);
}

export function getTenantId(): Promise<string> {
  return getInitializer().getTenantId();
}
