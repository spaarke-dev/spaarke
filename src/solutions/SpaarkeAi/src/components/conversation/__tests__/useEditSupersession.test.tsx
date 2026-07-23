/**
 * useEditSupersession.test.tsx — FR-17 undo/replace via ledger supersession (task 034).
 *
 * E2E DoD (project CLAUDE.md, non-waivable): the retraction/replace is proven through the REAL
 * interaction, not mocks — a REAL PaneEventBus carries the Flow-5 re-materialize signal, a REAL
 * `useComposeWorkspaceReceivers` receives it, and task 033's REAL `usePendingRedline` re-materializes
 * from current ledger state over a REAL headless TipTap editor. Only the HTTP boundary (the durable
 * supersession write) is stubbed here — its server half is proven through the wire by
 * `ComposeSupersedeEndpointContractTests` (WebApplicationFactory). Together they prove the full slice:
 * client hook → durable ledger write (server) → re-materialize from current ledger state (client).
 *
 * Covered:
 *   (a) "undo that" removes the prior redline via a SUPERSEDING ledger entry (prior marks gone +
 *       supersession recorded as a POST carrying supersedesRef — NOT a client DOM undo).
 *   (b) "try another approach" removes the prior redline and applies a FRESH proposal; the affordance
 *       renders under the dark theme (ADR-021).
 *   (c) both survive a simulated refresh — re-materialize from CURRENT ledger state (undo → nothing;
 *       replace → the fresh proposal).
 *   (d) superseding an already-superseded entry is an idempotent no-op (no further ledger write).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, act, renderHook } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';

// REAL PaneEventBus (ADR-030) — the same bus the shell mounts.
import { PaneEventBusProvider, useDispatchPaneEvent } from '@spaarke/ai-widgets';
// REAL FR-15 marks + FR-16 materialization + the workspace-leg receiver (all task 031/033/104 code).
import { InsertionMark } from '@spaarke/compose-components/widgets/marks/InsertionMark';
import { DeletionMark } from '@spaarke/compose-components/widgets/marks/DeletionMark';
import { CommentAnchorMark } from '@spaarke/compose-components/widgets/marks/CommentAnchorMark';
import { usePendingRedline } from '@spaarke/compose-components/widgets/hooks/usePendingRedline';
import { useComposeWorkspaceReceivers } from '@spaarke/compose-components/widgets/useComposeWorkspaceReceivers';

import { buildComposeApplyEvent } from '../composeApplyLeg';
import { useEditSupersession, EditSupersessionBar } from '../useEditSupersession';
import type { ComposeActionRequest } from '../useSerialActionQueue';

// ---------------------------------------------------------------------------
// Stub ledger + authenticatedFetch mock (the ONLY mocked boundary — HTTP).
// Replicates the server supersession logic so the client slice runs; the server
// half is proven through the wire by ComposeSupersedeEndpointContractTests.
// ---------------------------------------------------------------------------

interface LedgerRow {
  key: string;
  bindingId: string;
  turn: number;
  disposition: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  payload: any;
}

function makeAuthFetch(stub: { current: LedgerRow[] }): jest.Mock {
  return jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
    const jsonResponse = (status: number, data: unknown): Response =>
      ({ ok: status >= 200 && status < 300, status, json: async () => data } as Response);

    if (url.includes('/compose-outputs/supersede') && init?.method === 'POST') {
      const ref: string = JSON.parse(String(init.body)).supersedesRef;
      const prior = stub.current.find((r) => r.key === ref && r.disposition === 'compose');
      if (!prior) return jsonResponse(404, { error: 'not found' });

      const head = stub.current
        .filter((r) => r.disposition === 'compose' && r.bindingId === prior.bindingId)
        .reduce((a, b) => (b.turn > a.turn ? b : a));
      const isRetraction = prior.payload && prior.payload.retracted === true;

      // Idempotent no-op: the ref is no longer the head (already superseded) or is a retraction.
      if (head.key !== ref || isRetraction) {
        return jsonResponse(200, { key: head.key, supersedesRef: ref, outcome: 'noop' });
      }

      const turn = stub.current.reduce((m, r) => Math.max(m, r.turn), 0) + 1;
      const key = `${prior.bindingId}@t${turn}`;
      stub.current.push({
        key,
        bindingId: prior.bindingId,
        turn,
        disposition: 'compose',
        payload: { retracted: true, supersedes_ref: ref },
      });
      return jsonResponse(200, { key, supersedesRef: ref, outcome: 'superseded' });
    }

    // GET compose-outputs — the read projection.
    return jsonResponse(200, stub.current.filter((r) => r.disposition === 'compose'));
  });
}

// ---------------------------------------------------------------------------
// Harness — REAL bus + REAL editor + REAL usePendingRedline + REAL receiver.
// ---------------------------------------------------------------------------

interface HarnessApi {
  editor: Editor;
  supersession: ReturnType<typeof useEditSupersession>;
  reDispatch: jest.Mock;
  /** Materialize a stored ledger row through the REAL bus (Flow-5 apply → receiver → usePendingRedline). */
  applyFromLedger: (ledgerRef: string, bindingId: string) => void;
}
const api: { current: HarnessApi | null } = { current: null };

function makeEditor(): Editor {
  return new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, CommentAnchorMark],
    content: '<p>The quick fox.</p>',
  });
}

function Harness({ stub, authFetch }: { stub: { current: LedgerRow[] }; authFetch: jest.Mock }): React.JSX.Element {
  const editor = React.useMemo(() => makeEditor(), []);
  React.useEffect(() => () => editor.destroy(), [editor]);

  const redline = usePendingRedline(editor);
  const dispatch = useDispatchPaneEvent();

  // "try another approach" re-dispatch stub: writes a fresh proposal + re-materializes it via the bus.
  const reDispatch = React.useMemo(
    () =>
      jest.fn(async (request: ComposeActionRequest) => {
        const turn = stub.current.reduce((m, r) => Math.max(m, r.turn), 0) + 1;
        const key = `${request.bindingId}@t${turn}`;
        stub.current.push({
          key,
          bindingId: request.bindingId,
          turn,
          disposition: 'compose',
          payload: { target_text: 'quick', new_text: 'swift', match_mode: 'strict' },
        });
        dispatch('workspace', buildComposeApplyEvent(key, request.bindingId, 'sess-1') as never);
        return {};
      }),
    [dispatch, stub]
  );

  const supersession = useEditSupersession({
    bffBaseUrl: 'https://bff.example',
    getSessionId: () => 'sess-1',
    authenticatedFetch: authFetch as never,
    dispatchApply: (event) => dispatch('workspace', event as never),
  });

  // REAL workspace-leg receiver: materialize FROM the (stub) ledger by the event's ledgerRef —
  // exactly ComposeWorkspace's onAssistantInsert → materializeComposeDraftFromLedger contract.
  useComposeWorkspaceReceivers({
    onContextInsert: () => undefined,
    onAssistantInsert: (event) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const ref = (event as any).ledgerRef as string | undefined;
      if (!ref) return;
      const row = stub.current.find((r) => r.key === ref);
      if (row) redline.materialize(row.payload, { ledgerRef: row.key, bindingId: row.bindingId, turn: row.turn });
    },
  });

  api.current = {
    editor,
    supersession,
    reDispatch,
    applyFromLedger: (ledgerRef, bindingId) =>
      dispatch('workspace', buildComposeApplyEvent(ledgerRef, bindingId, 'sess-1') as never),
  };

  return (
    <EditSupersessionBar
      lastEdit={supersession.lastEdit}
      busy={supersession.busy}
      error={supersession.error}
      onUndo={() => {
        void supersession.undo();
      }}
      onTryAnother={() => {
        void supersession.tryAnother(reDispatch);
      }}
      onDismissError={supersession.clearError}
    />
  );
}

function renderHarness(): { stub: { current: LedgerRow[] }; authFetch: jest.Mock } {
  const stub = { current: [] as LedgerRow[] };
  const authFetch = makeAuthFetch(stub);
  render(
    <FluentProvider theme={webDarkTheme}>
      <PaneEventBusProvider>
        <Harness stub={stub} authFetch={authFetch} />
      </PaneEventBusProvider>
    </FluentProvider>
  );
  return { stub, authFetch };
}

/** Seed an applied `nimble` redline (b1@t1) via the REAL bus, then mark it the last applied edit. */
function seedAndApplyInitial(stub: { current: LedgerRow[] }): void {
  act(() => {
    stub.current.push({
      key: 'b1@t1',
      bindingId: 'b1',
      turn: 1,
      disposition: 'compose',
      payload: { target_text: 'quick', new_text: 'nimble', match_mode: 'strict' },
    });
  });
  // Materialize the prior redline through the REAL bus (Flow-5 apply → receiver → usePendingRedline).
  act(() => {
    api.current!.applyFromLedger('b1@t1', 'b1');
  });
  act(() => {
    api.current!.supersession.trackAppliedEdit({
      ledgerRef: 'b1@t1',
      bindingId: 'b1',
      request: { id: 'r1', bindingId: 'b1', args: {} },
    });
  });
}

afterEach(() => {
  api.current = null;
});

describe('useEditSupersession — FR-17 undo/replace via ledger supersession (real bus + real usePendingRedline)', () => {
  it('(a) undo removes the prior redline via a superseding ledger entry (NOT a DOM undo)', async () => {
    const { stub, authFetch } = renderHarness();
    seedAndApplyInitial(stub);

    // Prior redline is rendered from the ledger.
    expect(api.current!.editor.getHTML()).toContain('data-ledger-ref="b1@t1"');
    expect(api.current!.editor.getHTML()).toContain('nimble');
    expect(screen.getByRole('button', { name: /undo that/i })).toBeInTheDocument();

    await act(async () => {
      await api.current!.supersession.undo();
    });

    const html = api.current!.editor.getHTML();
    // The prior redline is gone — via a SUPERSEDING ledger entry (re-materialized), not a DOM undo.
    expect(html).not.toContain('data-ledger-ref="b1@t1"');
    expect(html).not.toContain('data-compose-mark="insertion"');
    expect(html).not.toContain('nimble');
    // Supersession RECORDED as a durable ledger write (the POST carried supersedesRef).
    const postCall = authFetch.mock.calls.find((c) => String(c[0]).includes('/compose-outputs/supersede'));
    expect(postCall).toBeDefined();
    expect(JSON.parse(String(postCall![1].body))).toMatchObject({ supersedesRef: 'b1@t1' });
    // A superseding retraction entry now exists in the ledger.
    expect(stub.current.some((r) => r.key === 'b1@t2' && r.payload.retracted === true)).toBe(true);
    // Affordance cleared.
    expect(api.current!.supersession.lastEdit).toBeNull();
  });

  it('(b) try another approach removes the prior redline and applies a fresh proposal (dark mode)', async () => {
    const { stub } = renderHarness();
    seedAndApplyInitial(stub);
    expect(api.current!.editor.getHTML()).toContain('nimble');
    // The affordance renders under the dark theme (ADR-021).
    expect(screen.getByRole('button', { name: /try another approach/i })).toBeInTheDocument();

    await act(async () => {
      await api.current!.supersession.tryAnother(api.current!.reDispatch);
    });

    const html = api.current!.editor.getHTML();
    expect(html).not.toContain('nimble'); // prior gone
    expect(html).toContain('swift'); // fresh proposal applied
    expect(api.current!.reDispatch).toHaveBeenCalledTimes(1);
    // Both the retraction and the fresh proposal are recorded in the ledger.
    expect(stub.current.some((r) => r.payload.retracted === true)).toBe(true);
    expect(stub.current.some((r) => r.payload.new_text === 'swift')).toBe(true);
  });

  it('(c) both survive a simulated refresh: re-materialize from CURRENT ledger state', () => {
    // After UNDO the head is the retraction (empty) → a clean reload renders NOTHING (durable).
    const undoneLedger: LedgerRow[] = [
      { key: 'b1@t1', bindingId: 'b1', turn: 1, disposition: 'compose', payload: { target_text: 'quick', new_text: 'nimble', match_mode: 'strict' } },
      { key: 'b1@t2', bindingId: 'b1', turn: 2, disposition: 'compose', payload: { retracted: true, supersedes_ref: 'b1@t1' } },
    ];
    const fresh1 = makeEditor();
    const { result: r1 } = renderHook(() => usePendingRedline(fresh1));
    const head1 = undoneLedger.reduce((a, b) => (b.turn > a.turn ? b : a));
    act(() => {
      r1.current.materialize(head1.payload, { ledgerRef: head1.key, bindingId: head1.bindingId, turn: head1.turn });
    });
    expect(fresh1.getHTML()).not.toContain('data-compose-mark'); // prior redline does NOT reappear
    expect(fresh1.getText()).toContain('quick'); // original text intact
    fresh1.destroy();

    // After REPLACE the head is the fresh proposal → a clean reload renders 'swift'.
    const replacedLedger: LedgerRow[] = [
      ...undoneLedger,
      { key: 'b1@t3', bindingId: 'b1', turn: 3, disposition: 'compose', payload: { target_text: 'quick', new_text: 'swift', match_mode: 'strict' } },
    ];
    const fresh2 = makeEditor();
    const { result: r2 } = renderHook(() => usePendingRedline(fresh2));
    const head2 = replacedLedger.reduce((a, b) => (b.turn > a.turn ? b : a));
    act(() => {
      r2.current.materialize(head2.payload, { ledgerRef: head2.key, bindingId: head2.bindingId, turn: head2.turn });
    });
    expect(fresh2.getHTML()).toContain('swift');
    expect(fresh2.getHTML()).toContain('data-ledger-ref="b1@t3"');
    fresh2.destroy();
  });

  it('(d) superseding an already-superseded entry is an idempotent no-op (no further ledger write)', async () => {
    const { stub, authFetch } = renderHarness();
    seedAndApplyInitial(stub);

    await act(async () => {
      await api.current!.supersession.undo();
    });
    expect(stub.current).toHaveLength(2); // b1@t1 + b1@t2 retraction

    // Re-target the SAME (now-superseded) prior edit and undo again.
    act(() => {
      api.current!.supersession.trackAppliedEdit({
        ledgerRef: 'b1@t1',
        bindingId: 'b1',
        request: { id: 'r1', bindingId: 'b1', args: {} },
      });
    });
    const before = authFetch.mock.calls.filter((c) => String(c[0]).includes('/supersede')).length;

    await act(async () => {
      await api.current!.supersession.undo();
    });

    // The call happened but returned a no-op — NO third ledger entry appended (idempotent).
    const after = authFetch.mock.calls.filter((c) => String(c[0]).includes('/supersede')).length;
    expect(after).toBe(before + 1);
    expect(stub.current).toHaveLength(2);
  });
});
