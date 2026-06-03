/**
 * dailyBriefing.registration.ts — SectionRegistration FACTORY for the Daily
 * Briefing section.
 *
 * Hoisted from `src/solutions/LegalWorkspace/src/sections/dailyBriefing/` in task 069.
 * Per ADR-012 + ADR-028, this module exports a `createDailyBriefingRegistration`
 * factory that takes the consumer's `authenticatedFetch` and optional
 * `tenantId` / `onRateLimitError` callback, and returns a fully-formed
 * `SectionRegistration` ready to drop into a section registry array.
 *
 * Why a factory (not a static registration like other sections)?
 *   - Other section registrations in `LegalWorkspace/src/sections/` close over
 *     LegalWorkspace-local helpers (sprk_event queries, etc.) and remain
 *     solution-local per ADR-012.
 *   - Daily Briefing is the ONE legal-workspace section whose data layer is a
 *     pure BFF AI call (no Dataverse entity strings), so it can live in the
 *     shared lib. But it still needs `authenticatedFetch` injected — the
 *     factory pattern provides that without forcing the shared lib to import
 *     `@spaarke/auth` (which would violate the peer-deps contract).
 *
 * Usage (consumer side):
 *   ```ts
 *   const dailyBriefingRegistration = createDailyBriefingRegistration({
 *     authenticatedFetch,    // from @spaarke/auth or local wrapper
 *     tenantId,              // optional — for cache scoping (ADR-014)
 *     onRateLimitError,      // optional — telemetry routing
 *   });
 *   // dailyBriefingRegistration is then placed in SECTION_REGISTRY alongside
 *   // other section registrations.
 *   ```
 *
 * Implements FR-15. Available in both standalone LegalWorkspace AND the
 * SpaarkeAi WorkspacePane Home tab embed (FR-25) via the shared lib barrel.
 *
 * Constraints:
 *   - ADR-012: Context-agnostic factory — no solution-local imports.
 *   - ADR-013: Data is consumed via the existing BFF
 *     `/api/ai/daily-briefing/narrate` endpoint inside `useDailyBriefing` —
 *     no new BFF service. The consumer-supplied `authenticatedFetch` handles
 *     base URL + auth.
 *   - ADR-014 / ADR-016 / ADR-028: TTL cache + rate-limit-aware error shape +
 *     auth are all encapsulated inside `useDailyBriefing` / `DailyBriefingSection`.
 *   - ADR-021: All styling via Fluent v9 tokens inside DailyBriefingSection.
 *   - ADR-025: Icon (`SparkleRegular`) sourced from `@fluentui/react-icons` v9.
 *
 * Default height: "medium" per FR-15 → mapped to "325px" to match sibling AI
 * "Latest Updates" content sections (visually consistent medium pane).
 */

import * as React from 'react';
import { SparkleRegular } from '@fluentui/react-icons';
import type { SectionRegistration, SectionFactoryContext, ContentSectionConfig } from '../../types';
import { DailyBriefingSection } from './DailyBriefingSection';
import type { NarrateRequest } from './useDailyBriefing';

// ---------------------------------------------------------------------------
// Factory options
// ---------------------------------------------------------------------------

/**
 * Inputs to `createDailyBriefingRegistration`. The consumer supplies its
 * own `authenticatedFetch` (per function-based auth contract, ADR-028).
 */
export interface CreateDailyBriefingRegistrationOptions {
  /**
   * Per-request authenticated fetch wrapper. Required. The supplied function
   * is forwarded to `DailyBriefingSection` → `useDailyBriefing`.
   */
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
  /**
   * Optional tenant identifier (Azure AD GUID) used to scope the in-memory
   * TTL cache key (ADR-014). When omitted, the cache falls back to an
   * anonymous key.
   */
  tenantId?: string;
  /**
   * Optional callback fired exactly once per 429 failure transition.
   * Consumers inject this to route the event through their dedicated
   * telemetry helper (LegalWorkspace `trackEvent` / SpaarkeAi
   * `logTelemetryError`).
   */
  onRateLimitError?: (properties: Record<string, unknown>) => void;
  /**
   * Optional programmatic notification-context loader (task 086 / Round 4
   * Fix 3). When supplied, the factory forwards it to `DailyBriefingSection`
   * → `useDailyBriefing` so the narrate endpoint receives the same populated
   * payload the standalone Daily Briefing Code Page sends (categories +
   * priorityItems + per-channel items via Xrm.WebApi). When omitted, the
   * legacy empty-payload contract is preserved (BFF returns empty bullets
   * → empty-state UI).
   *
   * SpaarkeAi's `WorkspaceHomeTab` supplies a callback that mirrors the
   * standalone code page's `useNotificationData` →
   * `buildNarrationRequest` data path so the embedded Daily Briefing
   * returns real AI bullets on cold load. LegalWorkspace's shim does NOT
   * supply this (preserves FR-25 / NFR-10).
   */
  loadNotificationContext?: () => Promise<NarrateRequest | null>;
}

// ---------------------------------------------------------------------------
// Factory
// ---------------------------------------------------------------------------

/**
 * Create a Daily Briefing `SectionRegistration` bound to consumer-supplied
 * auth + optional telemetry routing.
 *
 * The returned registration's factory closure captures the supplied options
 * and forwards them as props to `DailyBriefingSection`.
 */
export function createDailyBriefingRegistration(options: CreateDailyBriefingRegistrationOptions): SectionRegistration {
  const { authenticatedFetch, tenantId, onRateLimitError, loadNotificationContext } = options;

  return {
    id: 'daily-briefing',
    label: 'Daily Briefing',
    description: 'AI-curated highlights from your day',
    icon: SparkleRegular,
    category: 'ai',
    // "medium" per FR-15 — mapped to 325px (matches Latest Updates sibling)
    defaultHeight: '325px',

    factory(_context: SectionFactoryContext): ContentSectionConfig {
      return {
        id: 'daily-briefing',
        type: 'content',
        title: 'Daily Briefing',
        style: {},
        renderContent: () =>
          React.createElement(DailyBriefingSection, {
            authenticatedFetch,
            tenantId,
            onRateLimitError,
            loadNotificationContext,
          }),
      };
    },
  };
}

export default createDailyBriefingRegistration;
