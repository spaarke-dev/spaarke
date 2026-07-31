/**
 * useAgreementReviewGate — the interactive document-driven classifier + orientation +
 * confirmation gate controller (task 021, spec FR-07/FR-08/FR-09 interactive half;
 * design Lens 3d).
 *
 * Extracted from ConversationPane.tsx (a hot file sequenced across 021→031→041→042)
 * to minimize the host's footprint — mirrors the established decomposition pattern
 * (useConsumerChips.tsx / useAttachments.ts / useCommandRouting.ts / etc.). The host
 * wires this controller's `runGate` from the review-intent text interceptor
 * (`detectAgreementReviewIntent`, agreementReviewRouting.ts) and its
 * `handleGateChipAction` from the local-chip router (`onLocalChipAction`).
 *
 * Pipeline (per file):
 *   1. classify — dispatch the `agreement-classify` Binding (task 020, Reasoning tier)
 *      via a RAW consumer-dispatcher call (no chat rendering, no chip re-arm — the
 *      structured `{isAgreement, candidates[], composite, reasoning}` result is
 *      consumed here, never shown verbatim to the user).
 *   2. gate — `resolveAgreementReviewGateDecision` (pure, agreementReviewRouting.ts)
 *      branches on the result + the `sprk_agreementtype` registry's per-type
 *      confidence thresholds (read client-side via the host-context Xrm.WebApi —
 *      the SAME direct-read pattern the hub's wizard picker uses; no BFF round-trip).
 *   3. orient + dispatch — mount the file in Compose with `activeWorkType:
 *      'agreement-analysis'` (scopes `getToolsForSurface`) and dispatch the review
 *      Binding (`nda-review` consumerType, generalized per agreements-r1) with a
 *      `subDomain` slot — threaded server-side (SessionDispatchOrchestrator ->
 *      LinearRunContext.KnowledgeSourceIds -> ActionRunner) to bind the classified
 *      type's own knowledge pack, per §11 additive extension (021 server touch).
 *
 * Gate-state (ADR-041 "no double-ask"): once a file's gate resolves (auto-proceed,
 * confirmed, or a composite lens chosen), the resolved subDomain is cached per
 * fileId — a repeat "review this document" for the SAME file re-dispatches directly
 * without re-classifying or re-asking.
 *
 * @see ./agreementReviewRouting.ts — the pure detection/decision/message/chip logic.
 * @see ./localActionChips.ts — the reserved `local:agreement-review-*` chip ids.
 */

import * as React from "react";
import { createConsumerDispatcher, type IChatMessage, type IDataService } from "@spaarke/ui-components";
import type { WorkspacePaneEvent } from "@spaarke/ai-widgets";
import {
  AGREEMENT_REVIEW_COMPOSITE_MESSAGE,
  AGREEMENT_REVIEW_NON_AGREEMENT_MESSAGE,
  buildAgreementReviewCompositeChips,
  buildAgreementReviewConfirmChips,
  buildAgreementReviewConfirmMessage,
  buildAgreementReviewNonAgreementChips,
  displayNameFor,
  isAgreementClassifyResult,
  resolveAgreementReviewGateDecision,
  resolveFallbackKey,
  toConsumerChipWire,
  type AgreementClassifyCandidate,
  type AgreementReviewGateDecision,
  type AgreementTypeRegistryEntry,
} from "./agreementReviewRouting";
import {
  LOCAL_CHIP,
  decodeAgreementReviewLensChipId,
} from "./localActionChips";
import { makeLocalAssistantMessage } from "./summarizeRouting";

/** The `activeWorkType` value the orientation write sets (spec FR-07/design Lens 3d). */
const AGREEMENT_ANALYSIS_WORK_TYPE = "agreement-analysis";

export interface AgreementReviewGateDeps {
  bffBaseUrl: string;
  getAccessToken: () => Promise<string>;
  getSessionId: () => string | null;
  dispatch: (channel: "workspace", event: WorkspacePaneEvent) => void;
  /** Host-context Xrm.WebApi data service (e.g. `createXrmDataService()`) — the registry read. */
  dataService: IDataService;
  /** `agreement-classify` consumerType's bindingId, resolved via the SAME capability-discovery seam revise/draft/summarize/nda-review already use. Null until resolved / if unavailable. */
  classifyBindingId: string | null;
  /** `nda-review` consumerType's bindingId (the SAME generalized review Binding the "Review an NDA" card dispatches). */
  reviewBindingId: string | null;
  /** Opens/refreshes the Compose tab for a session file, threading `activeWorkType` (task 041/021). */
  mountFileInCompose: (sessionFileId: string, fileName?: string, activeWorkType?: string) => boolean;
  /**
   * The review dispatch — `useConsumerChips().dispatchBinding` (chips.dispatchBinding), reused
   * so the review's rich handling (NDA-shaped completion message + advisory-comments
   * materialization via the shared `onDispatchResult` bridge + chip re-arm) applies identically
   * whether the review was triggered by a chip click or this gate. Returns a Promise so "both"
   * can sequence multiple dispatches (ADR-016).
   */
  dispatchReviewBinding: (
    bindingId: string,
    args?: { slots?: Record<string, unknown>; resultLabel?: string; sessionIdOverride?: string }
  ) => Promise<void>;
  /** Renders the gate's chip strip through the SAME wire-parse every carrier uses (acceptChips). */
  acceptChips: (raw: unknown) => void;
  enqueueAssistantMessage: (message: IChatMessage) => void;
  inject: (message: IChatMessage) => void;
  /**
   * task 031 (DEF-09 routing): resolves the mounted file's REAL document session (backfilled by
   * `registerComposeActiveDocument` — see `documentSessionWaiter.ts`), so the review dispatch(es)
   * below can target it via `sessionIdOverride` — the WRITE (review's compose-disposition
   * SessionOutput) and the redline-materialize READ (`ComposeWorkspace`'s compose-outputs read)
   * must coincide. Resolves `null` (never rejects) when the document session never establishes —
   * the dispatch then proceeds on the bound chat session (pre-031 behavior), never blocking.
   */
  awaitDocumentSessionId: (fileId: string) => Promise<string | null>;
}

export interface AgreementReviewGateController {
  /** Entry point: a review-intent text message + an available session file. */
  runGate: (fileId: string, fileName: string | undefined) => Promise<void>;
  /** Routes a `local:agreement-review-*` chip click back to the pending gate decision. */
  handleGateChipAction: (actionId: string) => void;
  /** True while classification is in flight (optional "Working…" affordance). */
  classifying: boolean;
  /** Clears per-session gate state (call on new-session, mirrors the other refs' session resets). */
  resetForSession: () => void;
}

/** Per-fileId resolved gate outcome — the "no double-ask" cache (ADR-041). */
interface ResolvedGateEntry {
  readonly subDomainKey: string;
  readonly displayName: string;
}

/** The pending (unanswered) gate decision awaiting a chip click. */
interface PendingGate {
  readonly fileId: string;
  readonly fileName: string | undefined;
  readonly decision: AgreementReviewGateDecision;
  readonly registry: readonly AgreementTypeRegistryEntry[];
}

export function useAgreementReviewGate(deps: AgreementReviewGateDeps): AgreementReviewGateController {
  const {
    bffBaseUrl,
    getAccessToken,
    getSessionId,
    dispatch,
    dataService,
    classifyBindingId,
    reviewBindingId,
    mountFileInCompose,
    dispatchReviewBinding,
    acceptChips,
    enqueueAssistantMessage,
    inject,
    awaitDocumentSessionId,
  } = deps;

  const [classifying, setClassifying] = React.useState(false);

  // Cached registry read (Xrm.WebApi) — one fetch per session, lazy (only when the gate first runs).
  const registryRef = React.useRef<readonly AgreementTypeRegistryEntry[] | null>(null);
  // Per-fileId resolved decisions — "no double-ask" (ADR-041).
  const resolvedRef = React.useRef<Map<string, ResolvedGateEntry>>(new Map());
  // The single unanswered gate awaiting a chip click (single dispatch decision per turn, mirrors
  // useConsumerChips' chip-consumption invariant).
  const pendingRef = React.useRef<PendingGate | null>(null);
  // Code-review self-check (Step 9.5): `resolvedRef` alone only prevents a double-ask AFTER a gate
  // has settled — it does not prevent a rapid double-invocation (e.g. the user submits "review this
  // document" twice before the first classify round-trip returns) from running the classifier twice
  // for the SAME file concurrently. This in-flight set closes that gap.
  const inFlightRef = React.useRef<Set<string>>(new Set());

  // Raw classify dispatcher — deliberately SEPARATE from useConsumerChips' internal dispatcher: the
  // classify Action's structured JSON result must never be rendered as a chat message or re-arm the
  // chip strip (that would leak `{isAgreement, candidates, composite, reasoning}` verbatim into the
  // transcript). Same construction shape useConsumerChips uses internally (§11 — not a new primitive).
  const classifyDispatcher = React.useMemo(
    () =>
      createConsumerDispatcher({
        bffBaseUrl,
        getSessionId,
        getAccessToken,
        publishPaneEvent: (channel, event) => dispatch(channel as "workspace", event as WorkspacePaneEvent),
        // task 021: no renderer subscribes to the classify result's paced section-reveal (it is
        // never shown to the user — see the module doc) — suppress it so the dispatch settles the
        // instant the terminal chunk arrives instead of paying the D-F5 progressive-reveal pacing
        // delay for output nobody renders.
        suppressWorkspaceSectionBridge: true,
      }),
    [bffBaseUrl, getSessionId, getAccessToken, dispatch]
  );

  const loadRegistry = React.useCallback(async (): Promise<readonly AgreementTypeRegistryEntry[]> => {
    if (registryRef.current) return registryRef.current;
    try {
      const result = await dataService.retrieveMultipleRecords(
        "sprk_agreementtype",
        "?$select=sprk_key,sprk_name,sprk_isfallback,sprk_confidencethreshold&$filter=statecode eq 0"
      );
      const rows: AgreementTypeRegistryEntry[] = (result.entities ?? [])
        .map((e) => ({
          key: typeof e.sprk_key === "string" ? e.sprk_key : "",
          name: typeof e.sprk_name === "string" ? e.sprk_name : "",
          isFallback: e.sprk_isfallback === true,
          confidenceThreshold: typeof e.sprk_confidencethreshold === "number" ? e.sprk_confidencethreshold : null,
        }))
        .filter((r) => r.key.length > 0);
      registryRef.current = rows;
      return rows;
    } catch {
      // Reference table unavailable (dev / not yet seeded) — degrade to the global 0.85 threshold
      // (resolveConfidenceThreshold) + key-derived display names (displayNameFor), never a thrown gate.
      registryRef.current = [];
      return [];
    }
  }, [dataService]);

  const runClassify = React.useCallback(
    async (fileId: string) => {
      if (!classifyBindingId) return null;
      try {
        const dispatched = await classifyDispatcher(classifyBindingId, {
          slots: { fileIds: [fileId] },
          requiresAttachments: true,
          attachmentCount: 1,
        });
        return isAgreementClassifyResult(dispatched.result) ? dispatched.result : null;
      } catch {
        return null;
      }
    },
    [classifyBindingId, classifyDispatcher]
  );

  const dispatchReview = React.useCallback(
    async (fileId: string, fileName: string | undefined, subDomainKey: string, displayName: string): Promise<void> => {
      mountFileInCompose(fileId, fileName, AGREEMENT_ANALYSIS_WORK_TYPE);
      if (!reviewBindingId) {
        inject(
          makeLocalAssistantMessage("Sorry — the Agreement Review capability isn't available right now. Please try again.")
        );
        return;
      }
      // task 031 (DEF-09 routing): await the mounted file's REAL document session (see
      // documentSessionWaiter.ts) so the review's compose-disposition SessionOutput lands where
      // ComposeWorkspace reads compose-outputs. Degrades to the bound chat session (undefined
      // override) if it never establishes — never blocks, never drops the review.
      const documentSessionId = await awaitDocumentSessionId(fileId);
      await dispatchReviewBinding(reviewBindingId, {
        slots: { fileIds: [fileId], subDomain: subDomainKey },
        resultLabel: displayName,
        sessionIdOverride: documentSessionId ?? undefined,
      });
    },
    [mountFileInCompose, reviewBindingId, dispatchReviewBinding, inject, awaitDocumentSessionId]
  );

  /** "Both" — sequential dispatch, one pack at a time (ADR-016; never concurrent). */
  const dispatchBothSequentially = React.useCallback(
    async (
      fileId: string,
      fileName: string | undefined,
      candidates: readonly AgreementClassifyCandidate[],
      registry: readonly AgreementTypeRegistryEntry[]
    ): Promise<void> => {
      mountFileInCompose(fileId, fileName, AGREEMENT_ANALYSIS_WORK_TYPE);
      // task 031 (DEF-09 routing): resolved ONCE for the file (not per-candidate) — every sequential
      // pack in "both" targets the SAME document session.
      const documentSessionId = await awaitDocumentSessionId(fileId);
      for (const candidate of candidates) {
        const displayName = displayNameFor(candidate.subDomainKey, registry);
        if (!reviewBindingId) {
          inject(
            makeLocalAssistantMessage(
              `Sorry — the Agreement Review capability isn't available right now (skipped the **${displayName}** lens). Please try again.`
            )
          );
          continue;
        }
        // eslint-disable-next-line no-await-in-loop -- ADR-016: sequential, never concurrent.
        await dispatchReviewBinding(reviewBindingId, {
          slots: { fileIds: [fileId], subDomain: candidate.subDomainKey },
          resultLabel: displayName,
          sessionIdOverride: documentSessionId ?? undefined,
        });
      }
      resolvedRef.current.set(fileId, { subDomainKey: "both", displayName: "both" });
    },
    [mountFileInCompose, reviewBindingId, dispatchReviewBinding, inject, awaitDocumentSessionId]
  );

  const runGate = React.useCallback(
    async (fileId: string, fileName: string | undefined): Promise<void> => {
      if (!fileId) return;

      // ADR-041 "no double-ask": a file whose gate already resolved re-dispatches directly.
      const resolved = resolvedRef.current.get(fileId);
      if (resolved) {
        if (resolved.subDomainKey === "both") {
          // Already ran "both" for this file — re-running silently would double-spend the model;
          // tell the user their prior review stands rather than re-dispatching two more runs.
          inject(
            makeLocalAssistantMessage(
              "This document was already reviewed under both lenses — see the Review Summary in the Compose tab."
            )
          );
          return;
        }
        await dispatchReview(fileId, fileName, resolved.subDomainKey, resolved.displayName);
        return;
      }

      // Code-review self-check (Step 9.5): `resolvedRef` alone races if the SAME fileId's gate is
      // invoked twice before the first classify round-trip returns (e.g. a doubled-up "review this
      // document" send) — both calls would see no resolved entry yet and classify concurrently. This
      // in-flight guard makes a duplicate call for a file already being classified a silent no-op
      // (the FIRST call owns the turn; ADR-041 E-1 "a single complete proposal").
      if (inFlightRef.current.has(fileId)) return;
      inFlightRef.current.add(fileId);

      setClassifying(true);
      try {
        const [registry, classifyResult] = await Promise.all([loadRegistry(), runClassify(fileId)]);
        if (!classifyResult) {
          inject(
            makeLocalAssistantMessage(
              "Sorry — I couldn't classify this document right now. Please try again."
            )
          );
          return;
        }

        const decision = resolveAgreementReviewGateDecision(classifyResult, registry);
        pendingRef.current = { fileId, fileName, decision, registry };

        switch (decision.kind) {
          case "non-agreement":
            // NEVER silent-decline: explicit message + the general-review escape hatch.
            enqueueAssistantMessage(makeLocalAssistantMessage(AGREEMENT_REVIEW_NON_AGREEMENT_MESSAGE));
            acceptChips(toConsumerChipWire(buildAgreementReviewNonAgreementChips()));
            return;

          case "auto-proceed": {
            // NEVER silent-wrong-grounding: still an explicit orientation + dispatch, just no chat question.
            const displayName = displayNameFor(decision.subDomainKey, registry);
            resolvedRef.current.set(fileId, { subDomainKey: decision.subDomainKey, displayName });
            pendingRef.current = null;
            await dispatchReview(fileId, fileName, decision.subDomainKey, displayName);
            return;
          }

          case "confirm": {
            const displayName = displayNameFor(decision.subDomainKey, registry);
            enqueueAssistantMessage(
              makeLocalAssistantMessage(buildAgreementReviewConfirmMessage(displayName, decision.confidence))
            );
            acceptChips(toConsumerChipWire(buildAgreementReviewConfirmChips(displayName)));
            return;
          }

          case "composite":
            enqueueAssistantMessage(makeLocalAssistantMessage(AGREEMENT_REVIEW_COMPOSITE_MESSAGE));
            acceptChips(toConsumerChipWire(buildAgreementReviewCompositeChips(decision.candidates, registry)));
            return;
        }
      } finally {
        setClassifying(false);
        inFlightRef.current.delete(fileId);
      }
    },
    [loadRegistry, runClassify, dispatchReview, enqueueAssistantMessage, acceptChips, inject]
  );

  const handleGateChipAction = React.useCallback(
    (actionId: string): void => {
      const pending = pendingRef.current;
      if (!pending) return;
      const { fileId, fileName, decision, registry } = pending;

      if (actionId === LOCAL_CHIP.agreementReviewConfirm && decision.kind === "confirm") {
        pendingRef.current = null;
        const displayName = displayNameFor(decision.subDomainKey, registry);
        resolvedRef.current.set(fileId, { subDomainKey: decision.subDomainKey, displayName });
        void dispatchReview(fileId, fileName, decision.subDomainKey, displayName);
        return;
      }

      if (actionId === LOCAL_CHIP.agreementReviewGeneral) {
        pendingRef.current = null;
        const fallbackKey = resolveFallbackKey(registry);
        const displayName = displayNameFor(fallbackKey, registry);
        resolvedRef.current.set(fileId, { subDomainKey: fallbackKey, displayName });
        void dispatchReview(fileId, fileName, fallbackKey, displayName);
        return;
      }

      if (actionId === LOCAL_CHIP.agreementReviewBoth && decision.kind === "composite") {
        pendingRef.current = null;
        void dispatchBothSequentially(fileId, fileName, decision.candidates, registry);
        return;
      }

      const lensKey = decodeAgreementReviewLensChipId(actionId);
      if (lensKey && decision.kind === "composite") {
        pendingRef.current = null;
        const displayName = displayNameFor(lensKey, registry);
        resolvedRef.current.set(fileId, { subDomainKey: lensKey, displayName });
        void dispatchReview(fileId, fileName, lensKey, displayName);
      }
    },
    [dispatchReview, dispatchBothSequentially]
  );

  const resetForSession = React.useCallback((): void => {
    resolvedRef.current = new Map();
    pendingRef.current = null;
    inFlightRef.current = new Set();
    // registryRef is intentionally NOT cleared — the sprk_agreementtype registry is tenant-wide
    // reference data, not session-scoped; re-reading it per session would be wasted round-trips.
  }, []);

  return React.useMemo(
    () => ({ runGate, handleGateChipAction, classifying, resetForSession }),
    [runGate, handleGateChipAction, classifying, resetForSession]
  );
}
