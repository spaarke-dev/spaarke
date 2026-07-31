/**
 * useAgreementReviewGate.test.ts — task 021 (spec FR-07/FR-08/FR-09 interactive path).
 *
 * Drives the gate controller's branch logic with a mocked classify dispatcher (the
 * `@spaarke/ui-components` package boundary — same strategy as
 * useConsumerChips.surface-launch.test.tsx) and a mocked Xrm.WebApi-backed
 * `IDataService` registry read. Proves:
 *   - auto-proceed: near-certain confidence -> orient (mountFileInCompose with
 *     activeWorkType) + dispatch the review with the classified subDomain, NO chips.
 *   - confirm: below-threshold -> chips shown, NO dispatch until the confirm chip fires.
 *   - composite: choice-of-lens chips incl. "Both" -> "Both" dispatches SEQUENTIALLY,
 *     once per candidate (ADR-016 — never concurrent).
 *   - non-agreement: explicit decline message + the general-review escape hatch chip —
 *     NEVER a silent decline, never a fabricated review.
 *   - no-double-ask (ADR-041): a resolved file re-dispatches directly on a repeat call,
 *     without re-invoking the classifier.
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
  it("near-certain confidence orients (activeWorkType) + dispatches the review with the classified subDomain, no chips", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.92 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-1", "acme-nda.pdf");
    });

    expect(deps.mountFileInCompose).toHaveBeenCalledWith("file-1", "acme-nda.pdf", "agreement-analysis");
    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-1"], subDomain: "nda" },
      resultLabel: "NDA",
    });
    expect(deps.acceptChips).not.toHaveBeenCalled();
  });
});

describe("useAgreementReviewGate — confirm (below threshold)", () => {
  it("shows confirm chips and does NOT dispatch until the confirm chip fires", async () => {
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
    expect(wire).toHaveLength(2);
    expect(wire[0].targetBindingId).toBe(LOCAL_CHIP.agreementReviewConfirm);
    expect(wire[1].targetBindingId).toBe(LOCAL_CHIP.agreementReviewGeneral);

    // Clicking "Yes, review as NDA" now dispatches.
    act(() => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewConfirm);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-2"], subDomain: "nda" },
      resultLabel: "NDA",
    });
  });

  it("'pick-another' (general) dispatches the fallback pack, resolved via IsFallback — not a hardcoded literal", async () => {
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
      slots: { fileIds: ["file-3"], subDomain: "general" },
      resultLabel: "General Agreement",
    });
  });
});

describe("useAgreementReviewGate — composite (choice of lens)", () => {
  it("shows one chip per candidate + Both; clicking a single lens dispatches ONLY that pack", async () => {
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
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-4"], subDomain: "employment" },
      resultLabel: "Employment",
    });
  });

  it("'Both' dispatches SEQUENTIALLY, once per candidate (ADR-016 — never concurrent)", async () => {
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
    const deps = makeDeps({
      dispatchReviewBinding: jest.fn(async (_bindingId: string, args) => {
        callOrder.push((args?.slots as Record<string, unknown>)?.subDomain as string);
      }),
    });
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-5", "hybrid.pdf");
    });

    await act(async () => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewBoth);
      // let the internal sequential await chain settle
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(2);
    expect(callOrder).toEqual(["employment", "nda"]);
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
  it("a resolved file re-dispatches directly on a repeat call, without re-invoking the classifier", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.95 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-7", "acme-nda.pdf");
    });
    expect(classifyDispatcherMock).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1);

    await act(async () => {
      await result.current.runGate("file-7", "acme-nda.pdf");
    });
    // Classifier NOT re-invoked; the review dispatched again directly from the cached decision.
    expect(classifyDispatcherMock).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(2);
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

    // Still only ONE classify call — the second invocation was a silent no-op, and only ONE review
    // dispatch resulted (not a double-spend).
    expect(classifyDispatcherMock).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1);
  });
});

describe("useAgreementReviewGate — task 031 DEF-09 session routing", () => {
  it("auto-proceed threads the resolved document session as sessionIdOverride", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.92 }] })
    );
    const awaitDocumentSessionId = jest.fn<Promise<string | null>, [string]>().mockResolvedValue("doc-session-A");
    const deps = makeDeps({ awaitDocumentSessionId });
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-r1", "acme-nda.pdf");
    });

    expect(awaitDocumentSessionId).toHaveBeenCalledWith("file-r1");
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-r1"], subDomain: "nda" },
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
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewConfirm);
    });
    await waitFor(() => expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1));
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-r2"], subDomain: "nda" },
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
    await act(async () => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewBoth);
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

    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-r4"], subDomain: "nda" },
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

describe("useAgreementReviewGate — task 023 runExplicit (FR-09 explicit door, deterministic bind)", () => {
  it("binds the explicit subDomain DETERMINISTICALLY — no chips, no gate, mounts + dispatches immediately", async () => {
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
      await result.current.runExplicit("file-e1", "acme.pdf", "employment");
    });

    expect(deps.mountFileInCompose).toHaveBeenCalledWith("file-e1", "acme.pdf", "agreement-analysis");
    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-e1"], subDomain: "employment" },
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
      await result.current.runExplicit("file-e2", "acme.pdf", "employment");
      // let the non-blocking sanity check's .then() settle
      await Promise.resolve();
      await Promise.resolve();
    });

    // The dispatch was still bound to the EXPLICIT choice (employment), never re-routed to "nda".
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-e2"], subDomain: "employment" },
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
      await result.current.runExplicit("file-e3", "acme.pdf", "employment");
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
      await result.current.runExplicit("file-e4", "acme.pdf", "employment");
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(deps.dispatchReviewBinding).toHaveBeenCalledTimes(1);
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-e4"], subDomain: "employment" },
      resultLabel: "Employment",
    });
    expect(deps.enqueueAssistantMessage).not.toHaveBeenCalled();
    expect(deps.inject).not.toHaveBeenCalled();
  });

  it("classifier-unavailable-never-blocks: no classifyBindingId still dispatches the explicit review", async () => {
    const deps = makeDeps({ classifyBindingId: null });
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runExplicit("file-e5", "acme.pdf", "nda");
    });

    expect(classifyDispatcherMock).not.toHaveBeenCalled();
    expect(deps.dispatchReviewBinding).toHaveBeenCalledWith("review-binding-1", {
      slots: { fileIds: ["file-e5"], subDomain: "nda" },
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
      await result.current.runExplicit("file-e6", "acme.pdf", "employment");
      await Promise.resolve();
    });
    expect(classifyDispatcherMock).toHaveBeenCalledTimes(1);

    await act(async () => {
      await result.current.runExplicit("file-e6", "acme.pdf", "employment");
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
      await result.current.runExplicit("file-e7", "acme.pdf", "employment");
      await Promise.resolve();
    });

    expect(result.current.getLastResolvedSubDomainKey()).toBeNull();
  });
});

describe("useAgreementReviewGate — task 023 getLastResolvedSubDomainKey (classifier-path lookup-write seam)", () => {
  it("is null before any classifier resolution", () => {
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));
    expect(result.current.getLastResolvedSubDomainKey()).toBeNull();
  });

  it("tracks the auto-proceed classifier resolution", async () => {
    classifyDispatcherMock.mockResolvedValue(
      classifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: "nda", confidence: 0.92 }] })
    );
    const deps = makeDeps();
    const { result } = renderHook(() => useAgreementReviewGate(deps));

    await act(async () => {
      await result.current.runGate("file-k1", "acme.pdf");
    });

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
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewConfirm);
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
    await act(async () => {
      result.current.handleGateChipAction(LOCAL_CHIP.agreementReviewBoth);
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
    expect(result.current.getLastResolvedSubDomainKey()).toBe("nda");

    act(() => {
      result.current.resetForSession();
    });
    expect(result.current.getLastResolvedSubDomainKey()).toBeNull();
  });
});
