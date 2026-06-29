/**
 * BrowsePlaybooksCard.test.tsx — R7 Wave 9 task 096 / FR-18.
 *
 * Asserts the 9th "Browse Playbooks" action card on the LegalWorkspace
 * Get Started grid is correctly configured and that its click handler
 * opens the Playbook Library Code Page in BROWSE mode (no intent param).
 * This is consumer surface 3 of 3 for FR-18 — closes the ≥3 surfaces
 * acceptance criterion.
 *
 * Pattern parity with task 094 (`PlaybookLibraryHardSlash.test.ts` in
 * SpaarkeAi) and task 095 (`PlaybookLibraryAffordance.test.tsx` in
 * Spaarke.DailyBriefing.Components):
 *   - Pure config + factory test (no DOM render of WorkspaceShell).
 *   - Asserts AFFORDANCE PRESENCE (config row exists with the right id
 *     + label + icon + ariaLabel) + CALLBACK INVOCATION (factory's
 *     `onCardClick['browse-playbooks']` calls `ctx.onOpenWizard` with
 *     `'sprk_playbooklibrary'` and NO data arg → browse mode).
 *
 * Test classification per ADR-038 §7 (KEEP): MAINTAIN-class config test —
 * pins the public contract for the 3rd FR-18 consumer surface (browse
 * mode without intent). Removing this test would lose the regression
 * detection net for FR-18's closure invariant.
 *
 * Runtime status (mirrors FeedTodoSyncContext.test.tsx in this same
 * solution): LegalWorkspace does not yet have a test runner configured.
 * This file is authored against the standard @testing-library/react +
 * Vitest API so it can be picked up when the package adds a runner.
 * Until then the file documents the contract and serves as a vetted
 * reference for the wiring shape consumers must respect.
 *
 * NFR-03: pure unit test — no Dataverse, no MSAL, no /narrate.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

import type {
  SectionFactoryContext,
  ActionCardSectionConfig,
} from '@spaarke/ui-components';
import { ACTION_CARD_CONFIGS } from '../getStartedConfig';
import { getStartedRegistration } from '../../../sections/getStarted.registration';

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

function makeStubContext(overrides: Partial<SectionFactoryContext> = {}): SectionFactoryContext {
  return {
    webApi: {} as any,
    userId: 'test-user',
    service: {} as any,
    bffBaseUrl: 'https://localhost:5001',
    onNavigate: jest.fn(),
    onOpenWizard: jest.fn(),
    onBadgeCountChange: jest.fn(),
    onRefetchReady: jest.fn(),
    onExpandSection: jest.fn(),
    onOpenDocumentsDialog: jest.fn(),
    businessUnitId: 'test-bu',
    ...overrides,
  } as SectionFactoryContext;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('Get Started — Browse Playbooks card (R7 Wave 9 task 096 / FR-18 surface 3 of 3)', () => {
  it('config grid contains the "browse-playbooks" card with the canonical contract', () => {
    const card = ACTION_CARD_CONFIGS.find((c) => c.id === 'browse-playbooks');
    expect(card).toBeDefined();
    expect(card?.label).toBe('Browse Playbooks');
    // Icon is a React component — assert it is a function (FluentIcon).
    expect(typeof card?.icon).toBe('function');
    // Aria-label is explicit + descriptive per LegalWorkspace section conventions.
    expect(card?.ariaLabel).toMatch(/Playbook Library/i);
  });

  it('the 9th card is appended at the END of the ordered config (after schedule-new-meeting)', () => {
    // Order matters — the Get Started row renders the cards in this sequence,
    // and the "Browse Playbooks" affordance should be discoverable LAST so the
    // intent-driven cards (existing UX) remain in the prime first-row slots.
    const ids = ACTION_CARD_CONFIGS.map((c) => c.id);
    expect(ids[ids.length - 1]).toBe('browse-playbooks');
    expect(ids).toHaveLength(9);
  });

  it('factory wires onCardClick["browse-playbooks"] to ctx.onOpenWizard("sprk_playbooklibrary") in BROWSE mode (no intent)', () => {
    const ctx = makeStubContext();
    const config = getStartedRegistration.factory(ctx) as ActionCardSectionConfig;

    const handler = config.onCardClick?.['browse-playbooks'];
    expect(handler).toBeDefined();
    expect(typeof handler).toBe('function');

    // Fire the handler — must call onOpenWizard with the playbook library
    // Code Page web resource name and NO data arg (browse mode).
    handler!();
    expect(ctx.onOpenWizard).toHaveBeenCalledTimes(1);
    expect(ctx.onOpenWizard).toHaveBeenCalledWith('sprk_playbooklibrary');
  });

  it('intent-driven cards (send-email-message, schedule-new-meeting) still pass intent data — back-compat regression guard', () => {
    // Adding the 9th card must NOT break the existing 8th + 7th cards' contract.
    // These intent-driven handlers were established in R3+; the FR-18 surface 3
    // wire-up is purely additive.
    const ctx = makeStubContext();
    const config = getStartedRegistration.factory(ctx) as ActionCardSectionConfig;

    config.onCardClick?.['send-email-message']?.();
    expect(ctx.onOpenWizard).toHaveBeenLastCalledWith(
      'sprk_playbooklibrary',
      'intent=email-compose'
    );

    config.onCardClick?.['schedule-new-meeting']?.();
    expect(ctx.onOpenWizard).toHaveBeenLastCalledWith(
      'sprk_playbooklibrary',
      'intent=meeting-schedule'
    );

    // Browse-playbooks remains the ONLY no-intent invocation — distinguishes
    // surface 3's browse-mode contract from the intent-mode prior art.
    config.onCardClick?.['browse-playbooks']?.();
    expect(ctx.onOpenWizard).toHaveBeenLastCalledWith('sprk_playbooklibrary');
  });
});
