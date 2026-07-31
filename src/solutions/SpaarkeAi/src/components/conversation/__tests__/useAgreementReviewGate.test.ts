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
