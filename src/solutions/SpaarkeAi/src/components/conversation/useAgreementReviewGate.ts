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
  DEFAULT_REVIEW_DEPTH,
  buildAgreementReviewCompositeChips,
  buildAgreementReviewConfirmChips,
  buildAgreementReviewConfirmMessage,
  buildAgreementReviewDepthChoiceChips,
  buildAgreementReviewDepthChoiceMessage,
  buildAgreementReviewDirectDepthChoiceMessage,
  buildAgreementReviewNonAgreementChips,
  buildAgreementReviewSanityMismatchMessage,
  displayNameFor,
  isAgreementClassifyResult,
  resolveAgreementReviewGateDecision,
  resolveAgreementReviewSanityMismatch,
  resolveFallbackKey,
  toConsumerChipWire,
  type AgreementClassifyCandidate,
  type AgreementReviewGateDecision,
  type AgreementTypeRegistryEntry,
  type ReviewDepth,
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
  /**
   * task 023 (FR-09 explicit door, design Lens 3d "hint authoritative"): binds `subDomainKey`
   * DETERMINISTICALLY — no classification gate, no confirm chips, no re-route. The classifier
   * still runs, but only as a NON-BLOCKING, warn-only sanity check (see module doc + the
   * `resolveAgreementReviewSanityMismatch` pure decision). Callable directly (bypassing the
   * text-detection path entirely) — the seam task 033's wizard auto-run bridge consumes.
   *
   * task 070 (UAT2 review-depth selector): `reviewDepth` is an OPTIONAL override.
   *  - PROVIDED (the wizard auto-run hand-off, which already resolved a depth at finish time) →
   *    dispatches IMMEDIATELY at that depth — no extra chip turn. Inserting a post-open ask here
   *    would defeat FR-17's whole point ("no manual re-upload, review auto-runs").
   *  - OMITTED (the TEXT-path explicit door — the user typed "review this document" on an
   *    already-oriented session) → inserts ONE depth-choice turn (Quick/Thorough chips) before
   *    dispatching (CRITICAL UX RULE: exactly one extra turn, never a double-ask).
   */
  runExplicit: (
    fileId: string,
    fileName: string | undefined,
    subDomainKey: string,
    reviewDepth?: ReviewDepth
  ) => Promise<void>;
  /** Routes a `local:agreement-review-*` chip click back to the pending gate decision. */
  handleGateChipAction: (actionId: string) => void;
  /**
   * UAT round-3 (item #7): the DIRECT consumer-chip/card click path (ConversationPane.handleReviewNda,
   * the "Review an NDA" chip) — no classification, no `subDomain` slot (unchanged pre-070 wire shape).
   * Inserts ONE depth-choice turn (reusing the SAME `pendingDepthRef`/chip machinery every other branch
   * uses — never a second mechanism) before dispatching; the chip click IS the type commitment, so
   * there is no type question to fold in. No-op if `reviewBindingId` is unavailable — the caller (which
   * already resolved/checked the bindingId) is responsible for its own "capability unavailable" message
   * in that case, mirroring `handleReviewNda`'s existing structure.
   */
  runDirectReview: (fileId: string, fileName: string | undefined) => void;
  /** True while classification is in flight (optional "Working…" affordance). */
  classifying: boolean;
  /**
   * task 023 (classifier-path lookup write seam): the LAST subDomain key the CLASSIFIER (not the
   * explicit door) resolved for this session, if any — `null` when the session never went through
   * a classifier resolution, or only ever resolved via `runExplicit` (an explicit bind is already
   * persisted by its own door; it needs no NEW lookup write). Consumed by the promote/fork ->
   * Analysis binding flow to write the resolved `sprk_agreementtype` lookup so persistence matches
   * routing (see `agreementTypeLookupWrite.ts`).
   */
  getLastResolvedSubDomainKey: () => string | null;
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

/**
 * task 070 (UAT2 review-depth selector) — the pending (unanswered) DEPTH decision, awaiting a
 * Quick/Thorough chip click. Populated by the three branches whose type is already settled but
 * still need a depth choice inserted (auto-proceed, the explicit door's text-path ask, composite
 * once a lens/"Both" is picked) — see `buildAgreementReviewDepthChoiceChips`.
 */
type PendingDepthChoiceTarget =
  | {
      readonly kind: "single";
      readonly subDomainKey: string;
      readonly displayName: string;
      /**
       * Whether resolving this depth choice should also update `lastResolvedSubDomainKeyRef`
       * (task 023's classifier-path lookup-write seam — tracks CLASSIFIER resolutions only, never
       * an explicit-door bind, which already has a persisted lookup from its own door).
       */
      readonly trackAsClassifierResolution: boolean;
    }
  | {
      readonly kind: "both";
      readonly candidates: readonly AgreementClassifyCandidate[];
      readonly registry: readonly AgreementTypeRegistryEntry[];
    }
  | {
      /**
       * UAT round-3 (item #7) — the DIRECT consumer-chip/card click path (`runDirectReview`, below).
       * No `subDomainKey` (the pre-070 dispatch never carried one) and no classifier-resolution
       * tracking (there was never a classification to track).
       */
      readonly kind: "direct";
    };

interface PendingDepthChoice {
  readonly fileId: string;
  readonly fileName: string | undefined;
  readonly target: PendingDepthChoiceTarget;
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
  // task 023 (classifier-path lookup write seam): the LAST subDomain the CLASSIFIER resolved this
  // session (auto-proceed / confirm-accept / a single composite-lens pick — NOT `runExplicit`,
  // which already has a persisted lookup from its own door, and NOT "both", which is ambiguous for
  // a single-valued lookup). `null` until a classifier resolution happens; reset per session.
  const lastResolvedSubDomainKeyRef = React.useRef<string | null>(null);
  // The single unanswered gate awaiting a chip click (single dispatch decision per turn, mirrors
  // useConsumerChips' chip-consumption invariant).
  const pendingRef = React.useRef<PendingGate | null>(null);
  // task 070 — the single unanswered DEPTH choice awaiting a Quick/Thorough chip click. Separate
  // from `pendingRef` (a different decision shape — no classify decision, just a settled type or
  // composite candidate set) but the SAME single-pending-decision-per-turn invariant.
  const pendingDepthRef = React.useRef<PendingDepthChoice | null>(null);
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
    async (
      fileId: string,
      fileName: string | undefined,
      subDomainKey: string,
      displayName: string,
      reviewDepth: ReviewDepth
    ): Promise<void> => {
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
      // task 070 — `reviewDepth` rides the SAME dispatch-args slots the server already reads
      // `subDomain` off of (SessionDispatchOrchestrator.TryReadReviewDepth); the client never
      // names a model/deployment (ADR-039), only this closed two-value product intent.
      await dispatchReviewBinding(reviewBindingId, {
        slots: { fileIds: [fileId], subDomain: subDomainKey, reviewDepth },
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
      registry: readonly AgreementTypeRegistryEntry[],
      reviewDepth: ReviewDepth
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
          slots: { fileIds: [fileId], subDomain: candidate.subDomainKey, reviewDepth },
          resultLabel: displayName,
          sessionIdOverride: documentSessionId ?? undefined,
        });
      }
      resolvedRef.current.set(fileId, { subDomainKey: "both", displayName: "both" });
    },
    [mountFileInCompose, reviewBindingId, dispatchReviewBinding, inject, awaitDocumentSessionId]
  );

  /**
   * UAT round-3 (item #7) — the DIRECT consumer-chip/card click dispatch (no classification, no
   * `subDomain` slot). Mirrors `handleReviewNda`'s PRE-existing dispatch body EXACTLY (`slots:
   * {fileIds}`, no `resultLabel`), only adding `reviewDepth` — the chip click already committed the
   * type, so nothing else about the wire shape changes. `mountFileInCompose` is NOT called here — the
   * caller (`runDirectReview` below / `handleReviewNda`) already mounted the file at click time,
   * before the depth ask, so the document opens immediately regardless of how long the user takes to
   * answer Quick/Thorough.
   */
  const dispatchDirectReview = React.useCallback(
    async (fileId: string, reviewDepth: ReviewDepth): Promise<void> => {
      if (!reviewBindingId) return;
      const documentSessionId = await awaitDocumentSessionId(fileId);
      await dispatchReviewBinding(reviewBindingId, {
        slots: { fileIds: [fileId], reviewDepth },
        sessionIdOverride: documentSessionId ?? undefined,
      });
    },
    [reviewBindingId, dispatchReviewBinding, awaitDocumentSessionId]
  );

  /**
   * UAT round-3 (item #7): the direct chip/card click entry point — see the controller interface doc
   * comment for the full rationale. Presents the SAME standalone depth-choice turn `runGate`'s
   * auto-proceed branch uses (below), scoped to `kind: "direct"` so `handleGateChipAction` dispatches
   * via `dispatchDirectReview` (no `subDomain`) instead of `dispatchReview` (which always sets one).
   */
  const runDirectReview = React.useCallback(
    (fileId: string, fileName: string | undefined): void => {
      if (!fileId || !reviewBindingId) return;
      pendingDepthRef.current = { fileId, fileName, target: { kind: "direct" } };
      enqueueAssistantMessage(makeLocalAssistantMessage(buildAgreementReviewDirectDepthChoiceMessage()));
      acceptChips(toConsumerChipWire(buildAgreementReviewDepthChoiceChips()));
    },
    [reviewBindingId, enqueueAssistantMessage, acceptChips]
  );

  const runGate = React.useCallback(
    async (fileId: string, fileName: string | undefined): Promise<void> => {
      if (!fileId) return;

      // ADR-041 "no double-ask": a file whose gate already resolved re-dispatches directly.
      // task 070: a REPEAT invocation skips the depth ask entirely (mirrors the pre-070 "no
      // double-ask" re-dispatch — no NEW question is introduced for a file already resolved) and
      // defaults to Thorough (the safe, legal-quality default) rather than re-surfacing a chip
      // turn for an already-settled file.
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
        await dispatchReview(fileId, fileName, resolved.subDomainKey, resolved.displayName, DEFAULT_REVIEW_DEPTH);
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
            // NEVER silent-wrong-grounding: still an explicit orientation, just no TYPE question.
            // task 070: insert ONE depth-choice turn before dispatching (CRITICAL UX RULE — the
            // type is settled with high confidence, but the user still picks Quick vs Thorough).
            // Do NOT set resolvedRef/lastResolvedSubDomainKeyRef yet — mirrors the "confirm"
            // branch's timing exactly (a question is pending; the resolution is recorded only once
            // the user actually answers it, in handleGateChipAction below).
            const displayName = displayNameFor(decision.subDomainKey, registry);
            pendingRef.current = null;
            pendingDepthRef.current = {
              fileId,
              fileName,
              target: {
                kind: "single",
                subDomainKey: decision.subDomainKey,
                displayName,
                trackAsClassifierResolution: true,
              },
            };
            enqueueAssistantMessage(makeLocalAssistantMessage(buildAgreementReviewDepthChoiceMessage(displayName)));
            acceptChips(toConsumerChipWire(buildAgreementReviewDepthChoiceChips()));
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
      // task 070 — the standalone depth-choice chips (auto-proceed / explicit-door ask / composite
      // post-pick) are checked FIRST: these fire when `pendingRef` is already null (the type
      // question, if any, was already answered/skipped) and `pendingDepthRef` holds the settled
      // target(s) awaiting only a Quick/Thorough pick.
      if (actionId === LOCAL_CHIP.agreementReviewDepthQuick || actionId === LOCAL_CHIP.agreementReviewDepthThorough) {
        const pendingDepth = pendingDepthRef.current;
        if (!pendingDepth) return;
        pendingDepthRef.current = null;
        const reviewDepth: ReviewDepth =
          actionId === LOCAL_CHIP.agreementReviewDepthQuick ? "quick" : "thorough";
        const { fileId, fileName, target } = pendingDepth;

        if (target.kind === "both") {
          void dispatchBothSequentially(fileId, fileName, target.candidates, target.registry, reviewDepth);
          return;
        }

        // UAT round-3 (item #7) — the direct chip/card path: no subDomain, no classifier-resolution
        // tracking (there was never a classification to track).
        if (target.kind === "direct") {
          void dispatchDirectReview(fileId, reviewDepth);
          return;
        }

        resolvedRef.current.set(fileId, { subDomainKey: target.subDomainKey, displayName: target.displayName });
        if (target.trackAsClassifierResolution) {
          lastResolvedSubDomainKeyRef.current = target.subDomainKey; // task 023: classifier resolution
        }
        void dispatchReview(fileId, fileName, target.subDomainKey, target.displayName, reviewDepth);
        return;
      }

      const pending = pendingRef.current;
      if (!pending) return;
      const { fileId, fileName, decision, registry } = pending;

      if (
        (actionId === LOCAL_CHIP.agreementReviewConfirmQuick || actionId === LOCAL_CHIP.agreementReviewConfirmThorough) &&
        decision.kind === "confirm"
      ) {
        pendingRef.current = null;
        const displayName = displayNameFor(decision.subDomainKey, registry);
        const reviewDepth: ReviewDepth =
          actionId === LOCAL_CHIP.agreementReviewConfirmQuick ? "quick" : "thorough";
        resolvedRef.current.set(fileId, { subDomainKey: decision.subDomainKey, displayName });
        lastResolvedSubDomainKeyRef.current = decision.subDomainKey; // task 023: classifier resolution
        void dispatchReview(fileId, fileName, decision.subDomainKey, displayName, reviewDepth);
        return;
      }

      if (actionId === LOCAL_CHIP.agreementReviewGeneral) {
        pendingRef.current = null;
        const fallbackKey = resolveFallbackKey(registry);
        const displayName = displayNameFor(fallbackKey, registry);
        resolvedRef.current.set(fileId, { subDomainKey: fallbackKey, displayName });
        lastResolvedSubDomainKeyRef.current = fallbackKey; // task 023: classifier-flow resolution
        // task 070 — the general-review escape hatch is deliberately NOT depth-split (see
        // buildAgreementReviewConfirmChips's doc comment); defaults to Thorough.
        void dispatchReview(fileId, fileName, fallbackKey, displayName, DEFAULT_REVIEW_DEPTH);
        return;
      }

      if (actionId === LOCAL_CHIP.agreementReviewBoth && decision.kind === "composite") {
        pendingRef.current = null;
        // task 070 — insert ONE depth-choice turn before the sequential dispatch (composite is not
        // one of the task's two named "insert one turn" branches, but the same rationale applies
        // more strongly here: combining depth into the lens-choice turn would multiply chips
        // combinatorially — N lenses x 2 depths + "Both" x 2 — for an already-dense turn).
        pendingDepthRef.current = {
          fileId,
          fileName,
          target: { kind: "both", candidates: decision.candidates, registry },
        };
        enqueueAssistantMessage(
          makeLocalAssistantMessage(buildAgreementReviewDepthChoiceMessage("both agreement types"))
        );
        acceptChips(toConsumerChipWire(buildAgreementReviewDepthChoiceChips()));
        return;
      }

      const lensKey = decodeAgreementReviewLensChipId(actionId);
      if (lensKey && decision.kind === "composite") {
        pendingRef.current = null;
        const displayName = displayNameFor(lensKey, registry);
        // task 070 — same follow-up depth turn as "Both" above, for a single chosen lens.
        pendingDepthRef.current = {
          fileId,
          fileName,
          target: { kind: "single", subDomainKey: lensKey, displayName, trackAsClassifierResolution: true },
        };
        enqueueAssistantMessage(makeLocalAssistantMessage(buildAgreementReviewDepthChoiceMessage(displayName)));
        acceptChips(toConsumerChipWire(buildAgreementReviewDepthChoiceChips()));
      }
    },
    [dispatchReview, dispatchBothSequentially, dispatchDirectReview, enqueueAssistantMessage, acceptChips]
  );

  // task 023 (FR-09 explicit door): binds `subDomainKey` DETERMINISTICALLY — no classification
  // gate, no confirm chips, no re-route. Callable directly (the seam task 033's wizard auto-run
  // bridge consumes) or via ConversationPane's text-detection path when the session was launched
  // already-oriented (`explicitComposeLaunch?.subDomain`).
  //
  // task 070: `reviewDepth` is OPTIONAL — see the interface doc comment above for the two-mode
  // contract (provided = wizard auto-run, dispatch immediately; omitted = TEXT door, ask once).
  const runExplicit = React.useCallback(
    async (
      fileId: string,
      fileName: string | undefined,
      subDomainKey: string,
      reviewDepth?: ReviewDepth
    ): Promise<void> => {
      if (!fileId) return;

      // ADR-041 "no double-ask": a file whose bind already resolved (via either door — a fileId's
      // gate takes exactly one path per session) re-dispatches directly, no re-classify/re-notice.
      // task 070: an already-resolved repeat call never re-asks depth either — defaults to
      // Thorough unless this SAME call explicitly supplies one (matches runGate's identical
      // resolved-cache timing decision above).
      const resolved = resolvedRef.current.get(fileId);
      if (resolved && resolved.subDomainKey !== "both") {
        await dispatchReview(fileId, fileName, resolved.subDomainKey, resolved.displayName, reviewDepth ?? DEFAULT_REVIEW_DEPTH);
        return;
      }

      if (inFlightRef.current.has(fileId)) return;
      inFlightRef.current.add(fileId);

      try {
        const registry = await loadRegistry();
        const displayName = displayNameFor(subDomainKey, registry);

        // Warn-only sanity check (ADR-041): run the classifier NON-BLOCKING, fired immediately
        // regardless of which branch below runs — so if a depth-choice turn is about to be shown,
        // the wait time for the user's pick is put to use (task 070). `runClassify` never throws
        // (returns null on any failure/unavailability) and this `.then()`/`.catch()` pair NEVER
        // surfaces an error or blocks/re-routes the explicit run — resolveAgreementReviewSanityMismatch's
        // own null-safe contract means a classifier error silently produces no notice, per the
        // negative acceptance criterion.
        void runClassify(fileId)
          .then((classifyResult) => {
            const mismatch = resolveAgreementReviewSanityMismatch(subDomainKey, classifyResult, registry);
            if (mismatch) {
              enqueueAssistantMessage(makeLocalAssistantMessage(buildAgreementReviewSanityMismatchMessage(mismatch)));
            }
          })
          .catch(() => {
            // Never surfaced — see doc comment above.
          });

        if (reviewDepth) {
          // Depth already decided (the wizard auto-run hand-off) — dispatch immediately, no ask.
          // Inserting a chip turn here would defeat FR-17's "no manual re-upload, review
          // auto-runs" — the whole point of the auto-run bridge is zero further chat interaction.
          resolvedRef.current.set(fileId, { subDomainKey, displayName });
          await dispatchReview(fileId, fileName, subDomainKey, displayName, reviewDepth);
          return;
        }

        // TEXT-door explicit bind (no depth supplied) — insert ONE depth-choice turn (task 070,
        // CRITICAL UX RULE). Do NOT set resolvedRef yet — mirrors the ask-then-record timing the
        // auto-proceed branch (runGate) and the composite post-pick branch (handleGateChipAction)
        // both use; `trackAsClassifierResolution: false` because an explicit bind already has a
        // persisted lookup from its OWN door (task 023) — this is not a classifier resolution.
        pendingDepthRef.current = {
          fileId,
          fileName,
          target: { kind: "single", subDomainKey, displayName, trackAsClassifierResolution: false },
        };
        enqueueAssistantMessage(makeLocalAssistantMessage(buildAgreementReviewDepthChoiceMessage(displayName)));
        acceptChips(toConsumerChipWire(buildAgreementReviewDepthChoiceChips()));
      } finally {
        inFlightRef.current.delete(fileId);
      }
    },
    [loadRegistry, dispatchReview, runClassify, enqueueAssistantMessage, acceptChips]
  );

  const resetForSession = React.useCallback((): void => {
    resolvedRef.current = new Map();
    pendingRef.current = null;
    pendingDepthRef.current = null;
    inFlightRef.current = new Set();
    lastResolvedSubDomainKeyRef.current = null;
    // registryRef is intentionally NOT cleared — the sprk_agreementtype registry is tenant-wide
    // reference data, not session-scoped; re-reading it per session would be wasted round-trips.
  }, []);

  const getLastResolvedSubDomainKey = React.useCallback(
    (): string | null => lastResolvedSubDomainKeyRef.current,
    []
  );

  return React.useMemo(
    () => ({
      runGate,
      runExplicit,
      handleGateChipAction,
      runDirectReview,
      classifying,
      getLastResolvedSubDomainKey,
      resetForSession,
    }),
    [
      runGate,
      runExplicit,
      handleGateChipAction,
      runDirectReview,
      classifying,
      getLastResolvedSubDomainKey,
      resetForSession,
    ]
  );
}
