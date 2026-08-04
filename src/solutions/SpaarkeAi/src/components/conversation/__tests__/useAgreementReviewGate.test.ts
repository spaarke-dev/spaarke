/**
 * useAgreementReviewGate.test.ts — task 021 (spec FR-07/FR-08/FR-09 interactive path), UPDATED by
 * task 070 (UAT2 review-depth selector).
 *
 * Drives the gate controller's branch logic with a mocked classify dispatcher (the
 * `@spaarke/ui-components` package boundary — same strategy as
 * useConsumerChips.surface-launch.test.tsx) and a mocked Xrm.WebApi-backed
 * `IDataService` registry read. Proves:
 *   - auto-proceed: near-certain confidence -> orient (mountFileInCompose with
 *     activeWorkType) is DEFERRED until a Quick/Thorough depth-choice turn is answered
 *     (task 070 — a single extra chip turn, never a double-ask).
 *   - confirm: below-threshold -> Quick/Thorough type-confirm chips + general (task 070
 *     folds depth into the SAME turn), NO dispatch until one fires.
 *   - composite: choice-of-lens chips incl. "Both" -> picking a lens or "Both" inserts a
 *     FOLLOW-UP depth-choice turn (task 070); "Both" dispatches SEQUENTIALLY once answered,
 *     once per candidate (ADR-016 — never concurrent).
 *   - non-agreement: explicit decline message + the general-review escape hatch chip —
 *     NEVER a silent decline, never a fabricated review.
 *   - no-double-ask (ADR-041): a resolved file re-dispatches directly on a repeat call,
 *     without re-invoking the classifier or re-asking depth (defaults to Thorough).
 *   - runExplicit (task 023, extended by task 070): reviewDepth PROVIDED (wizard auto-run)
 *     dispatches immediately, no ask; reviewDepth OMITTED (TEXT door) inserts ONE
 *     depth-choice turn before dispatching.
 */

import { renderHook, act, waitFor } from "@testing-library/react";
import type { DispatchConsumerResult, IChatMessage, IDataService } from "@spaarke/ui-components";

const classifyDispatcherMock = jest.fn<Promise<DispatchConsumerResult>, [string, unknown]>();

jest.mock("@spaarke/ui-components", () => {
  const actual = jest.requireActual("@spaarke/ui-components");
  return {
    ...actual,
    createConsumerDispatcher: () => classifyDispatcherMock,
  };
});

// eslint-disable-next-line import/first
import { useAgreementReviewGate, type AgreementReviewGateDeps } from "../useAgreementReviewGate";
// eslint-disable-next-line import/first
import { LOCAL_CHIP, buildAgreementReviewLensChipId } from "../localActionChips";

function makeDataService(
  entities: Record<string, unknown>[] = [
    { sprk_key: "nda", sprk_name: "NDA", sprk_isfallback: false, sprk_confidencethreshold: null },
    { sprk_key: "employment", sprk_name: "Employment", sprk_isfallback: false, sprk_confidencethreshold: null },
    { sprk_key: "general", sprk_name: "General Agreement", sprk_isfallback: true, sprk_confidencethreshold: null },
  ]
): IDataService {
  return {
    createRecord: jest.fn(),
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities }),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  } as unknown as IDataService;
}

function makeDeps(overrides?: Partial<AgreementReviewGateDeps>): AgreementReviewGateDeps {
  return {
    bffBaseUrl: "https://bff.test",
    getAccessToken: async () => "tok",
    getSessionId: () => "session-1",
    dispatch: jest.fn(),
    dataService: makeDataService(),
    classifyBindingId: "classify-binding-1",
    reviewBindingId: "review-binding-1",
    mountFileInCompose: jest.fn().mockReturnValue(true),
    dispatchReviewBinding: jest.fn().mockResolvedValue(undefined),
    acceptChips: jest.fn(),
    enqueueAssistantMessage: jest.fn<void, [IChatMessage]>(),
    inject: jest.fn<void, [IChatMessage]>(),
    // task 031 (DEF-09 routing): defaults to "no document session" (undefined override) — every
    // pre-031 assertion below expects the EXACT pre-031 dispatchReviewBinding call shape; Jest's
    // toHaveBeenCalledWith ignores `undefined`-valued keys, so `sessionIdOverride: undefined` still
    // matches those. Dedicated routing tests override this to a resolved session id.
    awaitDocumentSessionId: jest.fn<Promise<string | null>, [string]>().mockResolvedValue(null),
    ...overrides,
  };
}

function classifyResult(payload: unknown): DispatchConsumerResult {
  return { streamId: "s", status: "complete", disposition: "informational", result: payload } as DispatchConsumerResult;
}

beforeEach(() => {
  classifyDispatcherMock.mockReset();
});

describe("useAgreementReviewGate — auto-proceed", () => {
  it("near-certain confidence inserts ONE depth-choice turn (task 070) — no dispatch, no orientation yet", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.92 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-1", "acme-nda.pdf");
    });

    expect(deps.dispatchReviewBinding).not.toHaveBeenCalled();
    expect(deps.mountFileInCompose).not.toHaveBeenCalled();
    expect(deps.enqueueAssistantMessage).toHaveBeenCalledWith(
      expect.objectContaining({ content: expect.stringContaining("NDA") })
    );
    expect(deps.acceptChips).toHaveBeenCalledTimes(1);
    const wire = (deps.acceptChips as jest.Mock).mock.calls[0][0] as Array<Record<string, unknown>>;
    expect(wire).toHaveLength(2);
    expect(wire[0].targetBindingId).toBe(LOCAL_CHIP.agreementReviewDepthQuick);
    expect(wire[1].targetBindingId).toBe(LOCAL_CHIP.agreementReviewDepthThorough);
  });

  it("clicking Thorough orients (activeWorkType) + dispatches with reviewDepth:'thorough'", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.92 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-1t", "acme-nda.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthThorough);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));

    expect(deps.mountFileInCompose).toHaveBeenCalledWith("file-1t", "acme-nda.pdf", "agreement-analysis");
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-1t"], subDomain: "nda", reviewDepth: "thorough" },
      resultLabel: "NDA",
    });
  });

  it("clicking Quick dispatches with reviewDepth:'quick'", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.92 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-1q", "acme-nda.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthQuick);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));

    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-1q"], subDomain: "nda", reviewDepth: "quick" },
      resultLabel: "NDA",
    });
  });
});

describe("useAgreementReviewGate — confirm (below threshold)", () => {
  it("shows Quick/Thorough type-confirm chips + general in ONE turn (task 070); does NOT dispatch until a chip fires", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.5 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-2", "unknown.pdf");
    });

    expect(deps.dispatchReviewBinding).not.toHaveBeenCalled();
    expect(deps.enqueueAssistantMessage).toHaveBeenCalledWith(
      expect.objectContaining({ content: expect.stringContaining("NDA") })
    );
    expect(deps.acceptChips).toHaveBeenCalledTimes(1);
    const wire = (deps.acceptChips as jest.Mock).mock.calls[0][0] as Array<Record<string, unknown>>;
    expect(wire).toHaveLength(3);
    expect(wire[0].targetBindingId).toBe(LOCAL_CHIP.agreementReviewConfirmQuick);
    expect(wire[1].targetBindingId).toBe(LOCAL_CHIP.agreementReviewConfirmThorough);
    expect(wire[2].targetBindingId).toBe(LOCAL_CHIP.agreementReviewGeneral);

    // Clicking "Review as NDA — Thorough" dispatches at Thorough.
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewConfirmThorough);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-2"], subDomain: "nda", reviewDepth: "thorough" },
      resultLabel: "NDA",
    });
  });

  it("clicking the Quick type-confirm chip dispatches at reviewDepth:'quick'", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.5 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-2q", "unknown.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewConfirmQuick);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-2q"], subDomain: "nda", reviewDepth: "quick" },
      resultLabel: "NDA",
    });
  });

  it("'pick-another' (general) dispatches the fallback pack at the DEFAULT depth (Thorough) — deliberately not depth-split", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.5 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-3", "unknown.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewGeneral);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-3"], subDomain: "general", reviewDepth: "thorough" },
      resultLabel: "General Agreement",
    });
  });
});

describe("useAgreementReviewGate — composite (choice of lens)", () => {
  it("shows one chip per candidate + Both; clicking a single lens inserts a FOLLOW-UP depth-choice turn (task 070), not an immediate dispatch", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({
        isAgreement: true,
        composite: true,
        candidates: [
          { subDomainKey: "employment", confidence: 0.6 },
          { subDomainKey: "nda", confidence: 0.55 },
        ],
      })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-4", "hybrid.pdf");
    });

    expect(deps.dispatchReviewBinding).not.toHaveBeenCalled();
    const wire = (deps.acceptChips as jest.Mock).mock.calls[0][0] as Array<Record<string, unknown>>;
    expect(wire).toHaveLength(3);

    act(() => {
      result.current.handleGateChipAction(buildAgreementReviewLensChipId("employment"));
    });
    // Picking a lens does NOT dispatch yet — it arms a SECOND, depth-choice turn.
    expect(deps.dispatchReviewBinding).not.toHaveBeenCalled();
    expect(deps.acceptChips).toHaveBeenCalledTimes(2);
    const depthWire = (deps.acceptChips as jest.Mock).mock.calls[1][0] as Array<Record<string, unknown>>;
    expect(depthWire[0].targetBindingId).toBe(LOCAL_CHIP.agreementReviewDepthQuick);
    expect(depthWire[1].targetBindingId).toBe(LOCAL_CHIP.agreementReviewDepthThorough);

    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthThorough);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-4"], subDomain: "employment", reviewDepth: "thorough" },
      resultLabel: "Employment",
    });
  });

  it("'Both' inserts a depth-choice turn; once answered, dispatches SEQUENTIALLY at the picked depth, once per candidate (ADR-016 — never concurrent)", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({
        isAgreement: true,
        composite: true,
        candidates: [
          { subDomainKey: "employment", confidence: 0.6 },
          { subDomainKey: "nda", confidence: 0.55 },
        ],
      })
    );
    const callOrder: string[] = [];
    const depthsSeen: string[] = [];
    const deps = makeDeps({
      dispatchReviewBinding: jest.fn(async (_bindingId: string, args) => {
        const slots = args?.slots as Record<string, unknown>;
        callOrder.push(slots?.subDomain as string);
        depthsSeen.push(slots?.reviewDepth as string);
      }),
    });
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-5", "hybrid.pdf");
    });

    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewBoth);
    });
    expect(deps.dispatchReviewBinding).not.toHaveBeenCalled();

    await act(async () => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthQuick);
      // let the internal sequential await chain settle
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(2);
    expect(callOrder).toEqual(["employment", "nda"]);
    expect(depthsSeen).toEqual(["quick", "quick"]);
  });
});

describe("useAgreementReviewGate — non-agreement (never a silent decline)", () => {
  it("posts an explicit decline + the general-review escape hatch chip; no dispatch", async () => {
    classifyDispatcherMock.mockResolvedValue(classifyResult({ isAgreement: false, composite: false, candidates: [] }));
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-6", "invoice.pdf");
    });

    expect(deps.dispatchReviewBinding).not.toHaveBeenCalled();
    expect(deps.mountFileInCompose).not.toHaveBeenCalled();
    expect(deps.enqueueAssistantMessage).toHaveBeenCalledWith(
      expect.objectContaining({ content: expect.stringContaining("doesn't look like an agreement") })
    );
    const wire = (deps.acceptChips as jest.Mock).mock.calls[0][0] as Array<Record<string, unknown>>;
    expect(wire).toHaveLength(1);
    expect(wire[0].targetBindingId).toBe(LOCAL_CHIP.agreementReviewGeneral);
  });
});

describe("useAgreementReviewGate — no-double-ask (ADR-041)", () => {
  it("a resolved file re-dispatches directly on a repeat call, without re-invoking the classifier or re-asking depth (defaults to Thorough)", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.95 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-7", "acme-nda.pdf");
    });
    expect(classifyDispatcherMock).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).not.toHaveBeenCalled(); // task 070: depth-choice pending

    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthThorough);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));

    await act(async () => {
      await result.current.runGate("file-7", "acme-nda.pdf");
    });
    // Classifier NOT re-invoked; the review dispatched again directly from the cached decision, and
    // no new depth question is asked (defaults to Thorough for the repeat — task 070).
    expect(classifyDispatcherMock).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(2);
    expect(deps.dispatchReviewBinding).toHaveBeenLastCalledWith("review-binding-1", {
      slots: { fileIds: ["file-7"], subDomain: "nda", reviewDepth: "thorough" },
      resultLabel: "NDA",
    });
  });
});

describe("useAgreementReviewGate — concurrent double-invocation (in-flight guard)", () => {
  it("a SECOND runGate call for the same file WHILE the first is still classifying is a no-op", async () => {
    let resolveClassify!: (v: DispatchConsumerResult) => void;
    classifyDispatcherMock.mockReturnValue(
      new Promise<DispatchConsumerResult>((resolve) => {
        resolveClassify = resolve;
      })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    // Fire two rapid calls for the SAME file before the classify dispatch resolves.
    let firstDone = false;
    let secondDone = false;
    const first = result.current.runGate("file-9", "acme-nda.pdf").then(() => {
      firstDone = true;
    });
    const second = result.current.runGate("file-9", "acme-nda.pdf").then(() => {
      secondDone = true;
    });

    // Neither has resolved yet — only ONE classify dispatch should have been issued.
    expect(classifyDispatcherMock).toHaveBeenCalledTimes(1);
    expect(firstDone).toBe(false);
    expect(secondDone).toBe(false);

    await act(async () => {
      resolveClassify(
        classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.95 }] })
      );
      await Promise.all([first, second]);
    });

    // Still only ONE classify call — the second invocation was a silent no-op. task 070: auto-proceed
    // now shows a depth-choice turn instead of dispatching immediately — assert exactly ONE such turn
    // was shown (not two), proving the in-flight guard still prevents a duplicate ask.
    expect(classifyDispatcherMock).toHaveBeenCalledTimes(1);
    expect(deps.acceptChips).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).not.toHaveBeenCalled();
  });
});

describe("useAgreementReviewGate — task 031 DEF-09 session routing", () => {
  it("auto-proceed threads the resolved document session as sessionIdOverride (once the depth choice is answered)", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.92 }] })
    );
    const awaitDocumentSessionId = jest.fn<Promise<string | null>, [string]>().mockResolvedValue("doc-session-A");
    const deps = makeDeps({ awaitDocumentSessionId });
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-r1", "acme-nda.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthThorough);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));

    expect(awaitDocumentSessionId).toHaveBeenCalledWith("file-r1");
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-r1"], subDomain: "nda", reviewDepth: "thorough" },
      resultLabel: "NDA",
      sessionIdOverride: "doc-session-A",
    });
  });

  it("the confirm-chip dispatch also threads the resolved document session", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.5 }] })
    );
    const awaitDocumentSessionId = jest.fn<Promise<string | null>, [string]>().mockResolvedValue("doc-session-B");
    const deps = makeDeps({ awaitDocumentSessionId });
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-r2", "unknown.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewConfirmThorough);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-r2"], subDomain: "nda", reviewDepth: "thorough" },
      resultLabel: "NDA",
      sessionIdOverride: "doc-session-B",
    });
  });

  it("'Both' resolves the document session ONCE and threads it to EVERY sequential pack", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({
        isAgreement: true,
        composite: true,
        candidates: [
          { subDomainKey: "employment", confidence: 0.6 },
          { subDomainKey: "nda", confidence: 0.55 },
        ],
      })
    );
    const awaitDocumentSessionId = jest.fn<Promise<string | null>, [string]>().mockResolvedValue("doc-session-C");
    const deps = makeDeps({ awaitDocumentSessionId });
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-r3", "hybrid.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewBoth);
    });
    await act(async () => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthThorough);
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(awaitDocumentSessionId).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(2);
    const calls = (deps.dispatchReviewBinding as jest.Mock).mock.calls;
    expect(calls[0][1]).toMatchObject({ sessionIdOverride: "doc-session-C" });
    expect(calls[1][1]).toMatchObject({ sessionIdOverride: "doc-session-C" });
  });

  it("degrades gracefully (undefined sessionIdOverride) when the document session never establishes", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.92 }] })
    );
    const awaitDocumentSessionId = jest.fn<Promise<string | null>, [string]>().mockResolvedValue(null);
    const deps = makeDeps({ awaitDocumentSessionId });
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-r4", "acme-nda.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthThorough);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));

    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-r4"], subDomain: "nda", reviewDepth: "thorough" },
      resultLabel: "NDA",
      sessionIdOverride: undefined,
    });
  });
});

describe("useAgreementReviewGate — classify unavailable", () => {
  it("degrades gracefully (no bindingId resolved) — no dispatch, no throw", async () => {
    const deps = makeDeps({ classifyBindingId: null });
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-8", "doc.pdf");
    });

    expect(classifyDispatcherMock).not.toHaveBeenCalled();
    expect(deps.dispatchReviewBinding).not.toHaveBeenCalled();
    expect(deps.inject).toHaveBeenCalled();
  });
});

describe("useAgreementReviewGate — task 023 runExplicit, reviewDepth PROVIDED (deterministic bind, no ask)", () => {
  it("binds the explicit subDomain DETERMINISTICALLY — no chips, no gate, mounts + dispatches immediately at the provided depth", async () => {
    // The classifier is never awaited before dispatch — resolve it after a tick so we can assert
    // dispatch already happened.
    let resolveClassify!: (v: DispatchConsumerResult) => void;
    classifyDispatcherMock.mockReturnValue(
      new Promise<DispatchConsumerResult>((resolve) => {
        resolveClassify = resolve;
      })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-e1", "acme.pdf", "employment", "thorough");
    });

    expect(deps.mountFileInCompose).toHaveBeenCalledWith("file-e1", "acme.pdf", "agreement-analysis");
    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-e1"], subDomain: "employment", reviewDepth: "thorough" },
      resultLabel: "Employment",
    });
    // NO chips, NO gate message — deterministic bind, never a classification question.
    expect(deps.acceptChips).not.toHaveBeenCalled();
    expect(deps.enqueueAssistantMessage).not.toHaveBeenCalled();

    // Let the still-pending classifier settle so the test doesn't leave a dangling promise.
    await act(async () => {
      resolveClassify(classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "employment", confidence: 0.9 }] }));
      await Promise.resolve();
    });
  });

  it("mismatch-warns: a high-confidence differing top candidate surfaces an informational notice, WITHOUT re-routing the dispatch", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.95 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-e2", "acme.pdf", "employment", "thorough");
      // let the non-blocking sanity check's .then() settle
      await Promise.resolve();
      await Promise.resolve();
    });

    // The dispatch was still bound to the EXPLICIT choice (employment), never re-routed to "nda".
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-e2"], subDomain: "employment", reviewDepth: "thorough" },
      resultLabel: "Employment",
    });
    expect(deps.enqueueAssistantMessage).toHaveBeenCalledWith(
      expect.objectContaining({ content: expect.stringContaining("NDA") })
    );
    // No chips were ever rendered for the sanity notice (ADR-041: informational only).
    expect(deps.acceptChips).not.toHaveBeenCalled();
  });

  it("no notice when the classifier agrees with the explicit choice", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "employment", confidence: 0.95 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-e3", "acme.pdf", "employment", "thorough");
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(deps.enqueueAssistantMessage).not.toHaveBeenCalled();
  });

  it("classifier-error-never-blocks: a rejected classify dispatch never blocks/surfaces on the explicit run", async () => {
    classifyDispatcherMock.mockRejectedValue(new Error("boom"));
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-e4", "acme.pdf", "employment", "thorough");
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-e4"], subDomain: "employment", reviewDepth: "thorough" },
      resultLabel: "Employment",
    });
    expect(deps.enqueueAssistantMessage).not.toHaveBeenCalled();
    expect(deps.inject).not.toHaveBeenCalled();
  });

  it("classifier-unavailable-never-blocks: no classifyBindingId still dispatches the explicit review", async () => {
    const deps = makeDeps({ classifyBindingId: null });
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-e5", "acme.pdf", "nda", "thorough");
    });

    expect(classifyDispatcherMock).not.toHaveBeenCalled();
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-e5"], subDomain: "nda", reviewDepth: "thorough" },
      resultLabel: "NDA",
    });
  });

  it("no-double-ask: a repeat runExplicit call for the SAME file re-dispatches directly without re-running the sanity check", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "employment", confidence: 0.95 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-e6", "acme.pdf", "employment", "thorough");
      await Promise.resolve();
    });
    expect(classifyDispatcherMock).toHaveBeenCalledTimes(1);

    await act(async () => {
      await result.current.runExplicit("file-e6", "acme.pdf", "employment", "thorough");
    });
    // Classifier NOT re-invoked on the repeat call; review dispatched again directly.
    expect(classifyDispatcherMock).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(2);
  });

  it("does NOT update getLastResolvedSubDomainKey (an explicit bind already has a persisted lookup from its own door)", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "employment", confidence: 0.95 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-e7", "acme.pdf", "employment", "thorough");
      await Promise.resolve();
    });

    expect(result.current.getLastResolvedSubDomainKey()).toBeNull();
  });
});

describe("useAgreementReviewGate — task 070 runExplicit, reviewDepth OMITTED (TEXT-door ask)", () => {
  it("omitting reviewDepth inserts ONE depth-choice turn — no dispatch yet; the sanity check still fires in parallel", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "employment", confidence: 0.9 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-d1", "acme.pdf", "employment");
    });

    expect(deps.dispatchReviewBinding).not.toHaveBeenCalled();
    expect(deps.enqueueAssistantMessage).toHaveBeenCalledWith(
      expect.objectContaining({ content: expect.stringContaining("Employment") })
    );
    expect(deps.acceptChips).toHaveBeenCalledTimes(1);
    const wire = (deps.acceptChips as jest.Mock).mock.calls[0][0] as Array<Record<string, unknown>>;
    expect(wire[0].targetBindingId).toBe(LOCAL_CHIP.agreementReviewDepthQuick);
    expect(wire[1].targetBindingId).toBe(LOCAL_CHIP.agreementReviewDepthThorough);
  });

  it("clicking Quick dispatches immediately at reviewDepth:'quick', still bound to the explicit subDomain", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "employment", confidence: 0.9 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-d2", "acme.pdf", "employment");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthQuick);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));

    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-d2"], subDomain: "employment", reviewDepth: "quick" },
      resultLabel: "Employment",
    });
  });

  it("does NOT update getLastResolvedSubDomainKey when the depth choice resolves (explicit-door bind, not a classifier resolution)", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "employment", confidence: 0.9 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-d3", "acme.pdf", "employment");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthThorough);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));

    expect(result.current.getLastResolvedSubDomainKey()).toBeNull();
  });
});

describe("useAgreementReviewGate — task 070 runExplicit, reviewDepth provided (wizard auto-run, no ask)", () => {
  it("dispatches IMMEDIATELY at the provided depth — no chip turn (FR-17: no manual re-upload, review auto-runs)", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "employment", confidence: 0.9 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-w1", "acme.pdf", "employment", "quick");
    });

    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-w1"], subDomain: "employment", reviewDepth: "quick" },
      resultLabel: "Employment",
    });
    expect(deps.acceptChips).not.toHaveBeenCalled();
  });
});

describe("useAgreementReviewGate — task 023 getLastResolvedSubDomainKey (classifier-path lookup-write seam)", () => {
  it("is null before any classifier resolution", () => {
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));
    expect(result.current.getLastResolvedSubDomainKey()).toBeNull();
  });

  it("tracks the auto-proceed classifier resolution once the depth choice is answered", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.92 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-k1", "acme.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthThorough);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));

    expect(result.current.getLastResolvedSubDomainKey()).toBe("nda");
  });

  it("tracks a confirm-chip acceptance", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.5 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-k2", "acme.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewConfirmThorough);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));

    expect(result.current.getLastResolvedSubDomainKey()).toBe("nda");
  });

  it("does NOT track 'both' (ambiguous for a single-valued lookup)", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({
        isAgreement: true,
        composite: true,
        candidates: [
          { subDomainKey: "employment", confidence: 0.6 },
          { subDomainKey: "nda", confidence: 0.55 },
        ],
      })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-k3", "hybrid.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewBoth);
    });
    await act(async () => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthThorough);
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(result.current.getLastResolvedSubDomainKey()).toBeNull();
  });

  it("resetForSession clears the tracked classifier resolution", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.92 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-k4", "acme.pdf");
    });
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewDepthThorough);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));
    expect(result.current.getLastResolvedSubDomainKey()).toBe("nda");

    act(() => {
      result.current.resetForSession();
    });
    expect(result.current.getLastResolvedSubDomainKey()).toBeNull();
  });
});
