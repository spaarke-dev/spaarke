/**
 * useEmailComposeActions.test.tsx (email-communication-solution-r5 task 036)
 *
 * Covers the task's closed acceptance set:
 *   - per-mode recipient prefill (reply / reply-all / forward / new)
 *   - send flows through the EXISTING BFF path + `onSent` refresh seam
 *   - NEGATIVE: no forked composer — every action mounts the canonical
 *     `SendEmailDialog` (verified by React-element identity, not a DOM string
 *     match, so a fork under a different name would fail this test)
 *   - NEGATIVE: no new BFF surface — the send request hits the SAME
 *     `/api/communications/send` path `SendEmailDialog.characterize.test.tsx`
 *     pins for the canonical composer
 *   - "Open full form" → `INavigationService.openRecordModal('sprk_communication', id)`
 *   - dark mode (ADR-021), rendered via the real `SendEmailDialog`
 *
 * jsdom polyfills below are SCOPED TO THIS FILE (not the package-wide
 * `jest.setup.ts`) — deliberately, to avoid touching a shared config file
 * mid-wave while sibling P3b tasks (033/034/035) are also landing. They
 * mirror the (already-proven) polyfills `Spaarke.UI.Components/jest.setup.js`
 * adds for the SAME Fluent v9 + Lexical `RichTextEditor` stack this test
 * exercises via the real `SendEmailDialog`.
 */
if (typeof window !== 'undefined' && !window.matchMedia) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: jest.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: jest.fn(),
      removeListener: jest.fn(),
      addEventListener: jest.fn(),
      removeEventListener: jest.fn(),
      dispatchEvent: jest.fn(),
    })),
  });
}
if (typeof globalThis.ResizeObserver === 'undefined') {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}
if (typeof Range !== 'undefined' && typeof Range.prototype.getBoundingClientRect !== 'function') {
  Range.prototype.getBoundingClientRect = function () {
    return { x: 0, y: 0, top: 0, left: 0, right: 0, bottom: 0, width: 0, height: 0, toJSON: () => ({}) } as DOMRect;
  };
}
if (typeof Range !== 'undefined' && typeof Range.prototype.getClientRects !== 'function') {
  Range.prototype.getClientRects = function () {
    return [] as unknown as DOMRectList;
  };
}

import * as React from 'react';
import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { SendEmailDialog } from '@spaarke/ui-components';
import type { IDataService, INavigationService } from '@spaarke/ui-components';
import { useEmailComposeActions } from '../useEmailComposeActions';
import type { EmailComposeActionsDeps } from '../EmailComposeActions.types';

const BFF = 'https://bff.example.com';

function makeDataService(record: Record<string, unknown>): IDataService {
  return {
    createRecord: jest.fn(),
    retrieveRecord: jest.fn().mockResolvedValue(record),
    retrieveMultipleRecords: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  };
}

function makeNavigationService(): INavigationService {
  return {
    openRecord: jest.fn().mockResolvedValue(undefined),
    openRecordModal: jest.fn().mockResolvedValue(undefined),
    openDialog: jest.fn().mockResolvedValue({ confirmed: true }),
    closeDialog: jest.fn(),
    openLookup: jest.fn().mockResolvedValue([]),
  };
}

function makeDeps(overrides: Partial<EmailComposeActionsDeps> = {}): EmailComposeActionsDeps {
  return {
    authenticatedFetch: jest
      .fn()
      .mockResolvedValue({ ok: true, json: async () => ({ communicationId: 'new-comm-1' }) } as Response),
    bffBaseUrl: BFF,
    dataService: makeDataService({}),
    navigationService: makeNavigationService(),
    ...overrides,
  };
}

const MULTI_RECIPIENT_RECORD = {
  sprk_from: 'jane.doe@example.com',
  sprk_to: 'to1@example.com;to2@example.com',
  sprk_cc: 'cc1@example.com',
  sprk_subject: 'Quarterly filing',
  sprk_body: '<p>Original body</p>',
  'createdon@OData.Community.Display.V1.FormattedValue': '7/1/2026 9:00 AM',
};

describe('useEmailComposeActions — per-mode recipient prefill (FR-09)', () => {
  it('Reply prefills the sender in initialTo only', async () => {
    const dataService = makeDataService(MULTI_RECIPIENT_RECORD);
    const { result } = renderHook(() => useEmailComposeActions(makeDeps({ dataService })));

    act(() => result.current.actions.onReply?.('comm-1'));
    await waitFor(() => expect(result.current.composerDialog.props.initialTo).toEqual(['jane.doe@example.com']));

    expect(result.current.composerDialog.props.mode).toBe('reply');
    expect(result.current.composerDialog.props.initialCc).toBeUndefined();
    expect(dataService.retrieveRecord).toHaveBeenCalledWith(
      'sprk_communication',
      'comm-1',
      expect.stringContaining('$select=')
    );
  });

  it('Reply All prefills the sender in initialTo and everyone else (To+Cc, deduped) in initialCc', async () => {
    const dataService = makeDataService(MULTI_RECIPIENT_RECORD);
    const { result } = renderHook(() => useEmailComposeActions(makeDeps({ dataService })));

    act(() => result.current.actions.onReplyAll?.('comm-1'));
    await waitFor(() => expect(result.current.composerDialog.props.initialCc).toBeDefined());

    expect(result.current.composerDialog.props.mode).toBe('reply'); // engine mode (reply-all shares the engine's 'reply' chrome)
    expect(result.current.composerDialog.props.initialTo).toEqual(['jane.doe@example.com']);
    expect(result.current.composerDialog.props.initialCc).toEqual(
      expect.arrayContaining(['to1@example.com', 'to2@example.com', 'cc1@example.com'])
    );
    expect(result.current.composerDialog.props.initialCc).toHaveLength(3);
  });

  it('Forward opens with EMPTY recipients and a quoted body', async () => {
    const dataService = makeDataService(MULTI_RECIPIENT_RECORD);
    const { result } = renderHook(() => useEmailComposeActions(makeDeps({ dataService })));

    act(() => result.current.actions.onForward?.('comm-1'));
    await waitFor(() => expect(result.current.composerDialog.props.initialBody).toBeDefined());

    expect(result.current.composerDialog.props.mode).toBe('forward');
    expect(result.current.composerDialog.props.initialTo).toBeUndefined();
    expect(result.current.composerDialog.props.initialCc).toBeUndefined();
    expect(result.current.composerDialog.props.initialBody).toEqual(expect.stringContaining('Original body'));
    expect(result.current.composerDialog.props.initialSubject).toBe('Fwd: Quarterly filing');
  });

  it('New opens FULLY empty compose — no record read at all', () => {
    const dataService = makeDataService(MULTI_RECIPIENT_RECORD);
    const { result } = renderHook(() => useEmailComposeActions(makeDeps({ dataService })));

    act(() => result.current.actions.onNew?.());

    expect(result.current.composerDialog.props.mode).toBe('compose');
    expect(result.current.composerDialog.props.initialTo).toBeUndefined();
    expect(result.current.composerDialog.props.initialSubject).toBeUndefined();
    expect(result.current.composerDialog.props.initialBody).toBeUndefined();
    expect(result.current.composerDialog.props.communicationId).toBeUndefined();
    expect(dataService.retrieveRecord).not.toHaveBeenCalled();
  });
});

describe('useEmailComposeActions — FR-10 thread-preserving bodyOverride (BINDING invariant)', () => {
  const AI_DRAFT = '<p>Thanks — I will review and revert by Friday.</p>';
  // The source record's body (`MULTI_RECIPIENT_RECORD.sprk_body`) is the quoted thread that MUST survive.
  const THREAD_MARKER = 'Original body';

  it('exposes the imperative openComposer entry point (the seam tasks 025/023 call)', () => {
    const { result } = renderHook(() => useEmailComposeActions(makeDeps()));
    expect(typeof result.current.openComposer).toBe('function');
  });

  it.each(['reply', 'replyAll', 'forward'] as const)(
    '%s: openComposer({ bodyOverride }) composes [AI draft] + [separator] + [quoted thread] — never a whole-body replace',
    async mode => {
      const dataService = makeDataService(MULTI_RECIPIENT_RECORD);
      const { result } = renderHook(() => useEmailComposeActions(makeDeps({ dataService })));

      act(() => result.current.openComposer(mode, 'comm-1', { bodyOverride: AI_DRAFT }));
      await waitFor(() => expect(result.current.composerDialog.props.initialBody).toBeDefined());

      const initialBody = result.current.composerDialog.props.initialBody as string;
      // Draft seeds the authored region (top of the body)...
      expect(initialBody.startsWith(AI_DRAFT)).toBe(true);
      // ...and the quoted previous thread is STILL present below it (invariant held).
      expect(initialBody).toContain(THREAD_MARKER);
      // DEFECT guard: a whole-body-replace would make the body === the draft (thread dropped).
      expect(initialBody).not.toBe(AI_DRAFT);
      expect(initialBody.length).toBeGreaterThan(AI_DRAFT.length);
    }
  );

  it('REGRESSION: openComposer WITHOUT a bodyOverride is byte-identical to the toolbar action (dual-mount parity)', async () => {
    const dataService = makeDataService(MULTI_RECIPIENT_RECORD);

    const viaOpen = renderHook(() => useEmailComposeActions(makeDeps({ dataService })));
    act(() => viaOpen.result.current.openComposer('reply', 'comm-1'));
    await waitFor(() => expect(viaOpen.result.current.composerDialog.props.initialBody).toBeDefined());
    const openBody = viaOpen.result.current.composerDialog.props.initialBody;

    const viaAction = renderHook(() => useEmailComposeActions(makeDeps({ dataService })));
    act(() => viaAction.result.current.actions.onReply?.('comm-1'));
    await waitFor(() => expect(viaAction.result.current.composerDialog.props.initialBody).toBeDefined());
    const actionBody = viaAction.result.current.composerDialog.props.initialBody;

    // The auto-draft seam and the manual toolbar action derive the SAME body when no draft is injected —
    // the existing standalone-mount behavior is unchanged.
    expect(openBody).toBe(actionBody);
    expect(openBody).toContain(THREAD_MARKER);
  });
});

describe('useEmailComposeActions — NEGATIVE: no forked composer', () => {
  it.each(['onReply', 'onReplyAll', 'onForward'] as const)(
    '%s mounts the CANONICAL SendEmailDialog (element identity), never a second composer',
    handlerName => {
      const { result } = renderHook(() => useEmailComposeActions(makeDeps()));
      act(() => (result.current.actions[handlerName] as (id: string) => void)('comm-1'));
      expect(result.current.composerDialog.type).toBe(SendEmailDialog);
    }
  );

  it('New also mounts the CANONICAL SendEmailDialog', () => {
    const { result } = renderHook(() => useEmailComposeActions(makeDeps()));
    act(() => result.current.actions.onNew?.());
    expect(result.current.composerDialog.type).toBe(SendEmailDialog);
  });

  it("exposes only onReply/onReplyAll/onForward/onNew — onArchive/onCreate are left unwired (out of this task's scope)", () => {
    const { result } = renderHook(() => useEmailComposeActions(makeDeps()));
    expect(result.current.actions.onArchive).toBeUndefined();
    expect(result.current.actions.onCreate).toBeUndefined();
  });
});

describe('useEmailComposeActions — "Open full form" (FR-15)', () => {
  it('opens the OOB sprk_communication Email main form as an 85% navigateTo modal via INavigationService.openRecordModal', async () => {
    const navigationService = makeNavigationService();
    const { result } = renderHook(() => useEmailComposeActions(makeDeps({ navigationService })));

    await act(async () => {
      await result.current.openFullForm('comm-42');
    });

    expect(navigationService.openRecordModal).toHaveBeenCalledWith('sprk_communication', 'comm-42');
  });

  it('degrades to a console warning (no throw) when the host navigationService has no openRecordModal', async () => {
    const navigationService: INavigationService = {
      openRecord: jest.fn(),
      openDialog: jest.fn(),
      closeDialog: jest.fn(),
      openLookup: jest.fn(),
    };
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    const { result } = renderHook(() => useEmailComposeActions(makeDeps({ navigationService })));

    await expect(result.current.openFullForm('comm-42')).resolves.toBeUndefined();
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('Open full form'));
    warnSpy.mockRestore();
  });
});

describe('useEmailComposeActions — send via the EXISTING BFF path + reading-pane refresh', () => {
  function ReplyHarness({ deps }: { deps: EmailComposeActionsDeps }) {
    const { actions, composerDialog } = useEmailComposeActions(deps);
    React.useEffect(() => {
      actions.onReply?.('comm-1');
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);
    return <>{composerDialog}</>;
  }

  it('clicking Send (on a pre-filled Reply) invokes authenticatedFetch against the SAME /api/communications/send path the canonical composer already uses (no new send/compose endpoint), and onSent fires the reading-pane refresh seam', async () => {
    const authenticatedFetch = jest
      .fn()
      .mockResolvedValue({ ok: true, json: async () => ({ communicationId: 'new-comm-1' }) } as Response);
    const onSent = jest.fn();
    const dataService = makeDataService(MULTI_RECIPIENT_RECORD);
    const deps = makeDeps({ authenticatedFetch, onSent, dataService });

    render(
      <FluentProvider theme={webLightTheme}>
        <ReplyHarness deps={deps} />
      </FluentProvider>
    );

    // Reply pre-fills To (sender) + subject + quoted body — passes validation, so
    // Send is reachable without any additional test-authored field entry.
    await screen.findByDisplayValue('Re: Quarterly filing');
    fireEvent.click(screen.getByRole('button', { name: /send/i }));

    await waitFor(() => expect(authenticatedFetch).toHaveBeenCalled());
    const [url] = authenticatedFetch.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${BFF}/api/communications/send`);

    await waitFor(() => expect(onSent).toHaveBeenCalledWith('new-comm-1'));
  });
});

describe('useEmailComposeActions — Dark Mode (ADR-021)', () => {
  it('renders the composer dialog correctly under a dark FluentProvider theme with no console errors', () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    const deps = makeDeps();

    function DarkHarness() {
      const { actions, composerDialog } = useEmailComposeActions(deps);
      React.useEffect(() => {
        actions.onNew?.();
        // eslint-disable-next-line react-hooks/exhaustive-deps
      }, []);
      return <>{composerDialog}</>;
    }

    render(
      <FluentProvider theme={webDarkTheme}>
        <DarkHarness />
      </FluentProvider>
    );

    // modalType="alert" (owner UAT #12 — no light-dismiss so a click-away can't discard a
    // draft) renders role="alertdialog" rather than "dialog".
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});
