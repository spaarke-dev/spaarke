/**
 * PlaybookLibraryHardSlash.test.ts — R7 task 094 / FR-18
 *
 * Verifies the `/playbooks` hard slash wiring that opens the Playbook Library
 * modal in browse mode from the spaarke-ai chat surface (consumer surface 1
 * of 3 — chat / briefing / ad-hoc).
 *
 * Per task 093 audit Q6 (PRIMARY recommendation): `/playbooks` reuses the
 * existing Pillar 8 hard-slash infrastructure rather than introducing a new
 * UI surface (toolbar button, etc.). This test asserts:
 *
 *   1. `/playbooks` is registered in CommandRouter's HardSlashes vocabulary.
 *   2. CommandRouter.parse('/playbooks') classifies as hard slash.
 *   3. executeHardSlash dispatches `/playbooks` to ctx.openLibraryModal.
 *   4. Telemetry emits `command='/playbooks', outcome='executed'`.
 *   5. ZERO BFF fetch calls (pure host-side affordance per ADR-013 —
 *      modal opening preserves Path A.5 routing downstream via the
 *      existing `sprk_playbooklibrary` Code Page wrapper).
 *   6. Latency < 100ms (Pillar 8 / Phase D exit criterion 2).
 *   7. The mock host callback receives no arguments — browse mode (empty
 *      sessionAttachmentIds) is the intended affordance.
 *
 * Mock surface (ADR-038 compliant):
 *   - Mock `ctx.openLibraryModal` (the host-injected boundary). This is the
 *     CONTRACT we're testing — the executor invokes it as a thunk.
 *   - Real `parse()` from CommandRouter (pure function — don't mock).
 *   - Mock telemetry sink (standard test-fixture pattern from
 *     HardSlashExecutor.test.ts).
 *   - DO NOT mock `executeHardSlash` — that's the SUT.
 *   - DO NOT mount React components — the affordance contract is at the
 *     executor level; ConversationPane integration is exercised by the
 *     parent composition.integration.test surface.
 *
 * @see HardSlashExecutor.ts execPlaybooks — module under test
 * @see CommandRouter.ts HardSlashes — vocabulary registry
 * @see projects/spaarke-ai-platform-unification-r7/notes/spikes/playbook-library-modal-audit.md
 */

import { parse, HardSlashes } from '../CommandRouter';
import {
  executeHardSlash,
  TELEMETRY_HARD_SLASH_INVOKED,
  type ExecutorContext,
  type TelemetrySink,
} from '../HardSlashExecutor';

// ---------------------------------------------------------------------------
// Minimal ExecutorContext factory — only the surfaces /playbooks touches are
// instrumented; other fields are stub no-ops to keep the test focused.
// ---------------------------------------------------------------------------

interface PlaybookCtxBundle {
  ctx: ExecutorContext;
  telemetryEvents: Array<{ name: string; properties: Record<string, unknown> }>;
  openLibraryModalCalls: number;
  fetchCalls: number;
}

function makeMinimalCtx(): PlaybookCtxBundle {
  const telemetryEvents: Array<{ name: string; properties: Record<string, unknown> }> = [];
  let openLibraryModalCalls = 0;
  let fetchCalls = 0;

  const telemetry: TelemetrySink = {
    emit(eventName, properties) {
      telemetryEvents.push({ name: eventName, properties: { ...properties } });
    },
  };

  const ctx: ExecutorContext = {
    bffBaseUrl: 'https://bff.test',
    authenticatedFetch: async () => {
      fetchCalls++;
      return new Response('{}', { status: 200 });
    },
    sessionId: 'session-1',
    paneEventBus: {
      dispatch: () => undefined,
      subscribe: () => () => undefined,
    } as unknown as ExecutorContext['paneEventBus'],
    setHelpOpen: () => undefined,
    clearLocalConversation: () => undefined,
    createNewSession: async () => 'session-new-1',
    getConversationHistory: () => [],
    getFocusedTabId: () => null,
    activeMatterId: null,
    downloadBlob: () => undefined,
    openLibraryModal: () => {
      openLibraryModalCalls++;
    },
    telemetry,
  };

  return {
    ctx,
    telemetryEvents,
    get openLibraryModalCalls() {
      return openLibraryModalCalls;
    },
    get fetchCalls() {
      return fetchCalls;
    },
  } as unknown as PlaybookCtxBundle;
}

// ---------------------------------------------------------------------------
// 1. Vocabulary registration
// ---------------------------------------------------------------------------

describe('R7 task 094 — /playbooks hard slash vocabulary', () => {
  it('registers /playbooks in HardSlashes (audit Q6 PRIMARY recommendation)', () => {
    expect(HardSlashes).toContain('/playbooks');
  });

  it('parse("/playbooks") classifies as hard slash', () => {
    const intent = parse('/playbooks');
    expect(intent.command).toBe('/playbooks');
    expect(intent.isHardSlash).toBe(true);
    expect(intent.isSoftSlash).toBe(false);
  });

  it('parse is case-insensitive ("/PLAYBOOKS" → hard slash)', () => {
    const intent = parse('/PLAYBOOKS');
    expect(intent.command).toBe('/playbooks');
    expect(intent.isHardSlash).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// 2. Executor dispatch contract
// ---------------------------------------------------------------------------

describe('R7 task 094 — executeHardSlash(/playbooks) dispatch', () => {
  it('invokes ctx.openLibraryModal exactly once', async () => {
    const bundle = makeMinimalCtx();
    const intent = parse('/playbooks');
    const result = await executeHardSlash(intent, bundle.ctx);

    expect(result.outcome).toBe('executed');
    expect(result.message).toBeNull();
    expect(bundle.openLibraryModalCalls).toBe(1);
  });

  it('emits TELEMETRY_HARD_SLASH_INVOKED with command=/playbooks outcome=executed', async () => {
    const bundle = makeMinimalCtx();
    const intent = parse('/playbooks');
    await executeHardSlash(intent, bundle.ctx);

    const invokedEvents = bundle.telemetryEvents.filter(
      (e) => e.name === TELEMETRY_HARD_SLASH_INVOKED,
    );
    expect(invokedEvents).toHaveLength(1);
    expect(invokedEvents[0].properties.command).toBe('/playbooks');
    expect(invokedEvents[0].properties.outcome).toBe('executed');
    // ADR-015: timestamp present, no user raw text leaked.
    expect(invokedEvents[0].properties.timestamp).toMatch(
      /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/,
    );
    expect(JSON.stringify(invokedEvents[0].properties)).not.toContain('/playbooks ');
  });

  it('makes ZERO BFF fetch calls (modal is pure host-side, ADR-013 + ADR-029)', async () => {
    const bundle = makeMinimalCtx();
    const intent = parse('/playbooks');
    await executeHardSlash(intent, bundle.ctx);

    expect(bundle.fetchCalls).toBe(0);
  });

  it('executes in <100ms under mocked host (Phase D exit criterion 2)', async () => {
    const bundle = makeMinimalCtx();
    const intent = parse('/playbooks');
    const t0 = performance.now();
    await executeHardSlash(intent, bundle.ctx);
    const elapsed = performance.now() - t0;
    expect(elapsed).toBeLessThan(100);
  });
});

// ---------------------------------------------------------------------------
// 3. Failure isolation — host throws should not crash the executor
// ---------------------------------------------------------------------------

describe('R7 task 094 — /playbooks failure isolation', () => {
  it('degrades to failed-unknown if host openLibraryModal throws', async () => {
    const ctx: ExecutorContext = {
      bffBaseUrl: 'https://bff.test',
      authenticatedFetch: async () => new Response('{}', { status: 200 }),
      sessionId: 'session-1',
      paneEventBus: {
        dispatch: () => undefined,
        subscribe: () => () => undefined,
      } as unknown as ExecutorContext['paneEventBus'],
      setHelpOpen: () => undefined,
      clearLocalConversation: () => undefined,
      createNewSession: async () => 'session-new-1',
      getConversationHistory: () => [],
      getFocusedTabId: () => null,
      activeMatterId: null,
      downloadBlob: () => undefined,
      openLibraryModal: () => {
        throw new Error('synthetic host failure');
      },
      telemetry: {
        emit: () => undefined,
      },
    };
    const intent = parse('/playbooks');
    const result = await executeHardSlash(intent, ctx);

    // The dispatcher's outer try/catch absorbs any executor throw and
    // returns failed-unknown — surface stays intact per ADR-038 boundary.
    expect(result.outcome).toBe('failed-unknown');
    expect(result.errorCode).toBeDefined();
  });
});
