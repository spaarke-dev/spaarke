# Task 016 — Briefing-accuracy eval family: schema proposal (escalation-gated)

> **Date**: 2026-07-09 · **FR-A7 / NFR-02** · **STATUS: PROPOSAL — awaiting confirmation before mass-authoring** (per the task's `<escalation>` trigger)

## Why this is a proposal, not a finished suite

The task escalation trigger fires: *"If the existing golden-utterances.json schema cannot express item-level accuracy assertions, STOP and propose a NEW fixture format + suite … present the proposed schema for confirmation before mass-authoring cases."*

**Confirmed**: `tests/integration/contract/Eval/GoldenUtteranceEvalSuiteTests.cs` (1,335 lines) + `golden-utterances.json` are **dispatch/routing-oriented** — every case is `{caseId, family, channel, utterance, expected:{outcomeClass, consumerType, catalogStatus}, assertions:{schemaConformance, citationIntegrity}}`, all about *which capability/binding/action an utterance routes to*. There is **no vocabulary for item-level provenance** (per-item title vs regarding vs citation origin), aggregate-preference, grounding round-trip, or anchor-drop. Force-fitting it would corrupt the dispatch schema. So a **new fixture + suite** is required — presented here for sign-off.

## What the accuracy family must assert (the 4 property families, FR-A7)

1. **Zero cross-item pairing** — for every rendered item, its title, regarding record name, and citation/link all originate from the SAME source item. ≥1 case constructed to FAIL under the pre-R5 cross-pairing bug (removed by task 010's per-channel LLM leg deletion) and PASS now.
2. **Aggregation-preference** — the briefing surfaces an aggregate ("you have N …") rather than enumerating, where the deterministic TL;DR facts drive it.
3. **Grounding round-trip** — every TL;DR fact traces back to the deterministic view model (extends task 013's `DailyBriefingTldrFactsTests`).
4. **TL;DR-abstraction** — a non-resolving anchor is **dropped, not warned** (task 014's `itemRefs[]` logic).

## Proposed fixture schema — `tests/integration/contract/Eval/briefing-accuracy-corpus.json`

Item-level, deterministic-pipeline-oriented (NOT dispatch):

```jsonc
{
  "schemaVersion": "briefing-accuracy-1.0",
  "cases": [
    {
      "caseId": "BA-001",
      "family": "zero-cross-pairing",            // one of the 4 property families
      "description": "two matters with confusable titles never cross-pair title↔link",
      "sourceRecords": [                          // fixture rows fed to the deterministic collector
        { "entity": "sprk_matter", "id": "…GUID-A…", "name": "Acme merger review",
          "highPriority": true, "link": "/main.aspx?etc=…&id=…A…" },
        { "entity": "sprk_matter", "id": "…GUID-B…", "name": "Acme lease review",
          "highPriority": true, "link": "/main.aspx?etc=…&id=…B…" }
      ],
      "expect": {
        "perItemProvenance": true,                // each rendered bullet: title.id == regarding.id == link.id
        "noCrossPairing": true                    // assert no bullet mixes A's title with B's link
      }
    },
    {
      "caseId": "BA-010", "family": "aggregation-preference",
      "sourceRecords": [ /* N todos */ ],
      "expect": { "tldrPrefersAggregateOverEnumeration": true, "aggregateCount": 7 }
    },
    {
      "caseId": "BA-020", "family": "grounding-round-trip",
      "sourceRecords": [ /* mixed */ ],
      "expect": { "everyTldrFactTracesToViewModel": true }
    },
    {
      "caseId": "BA-030", "family": "tldr-abstraction",
      "sourceRecords": [ /* … */ ],
      "tldrAnchors": [ "resolves-to-item-X", "does-not-resolve" ],
      "expect": { "nonResolvingAnchorDropped": true, "noWarningEmitted": true }
    }
  ]
}
```

## Proposed suite — `tests/integration/contract/Eval/BriefingAccuracyEvalSuiteTests.cs`

- `[Trait("Category", "GoldenUtteranceEval")]` → **auto-included in the existing `eval-gate` CI job** (`dotnet test --filter "Category=GoldenUtteranceEval"`, no `continue-on-error`). This satisfies NFR-02 "required merge gate" **without editing `sdap-ci.yml`'s job graph** — the gate already exists; the new suite joins it by trait. (If you prefer an explicitly-named gate, I can add a `Category=BriefingAccuracyEval` OR-clause to the eval-gate filter — one-line CI edit. Recommend the trait-reuse to avoid touching the hot-path CI file.)
- **Mechanism** (ADR-038-compliant, mirrors task 036's approach): load the corpus JSON → for each case, build the deterministic view model by driving the **real `DailyBriefingCollector`** with a `Mock<IGenericEntityService>` returning the case's `sourceRecords` (module-boundary mock — NO `Mock<HttpMessageHandler>`, no DI-registration/ctor-null tests) → assert the case's `expect` block against the produced `DailyBriefingNarrateRequest` view model + `TldrFacts`.
- **Zero-cross-pairing** is asserted structurally: each channel bullet's `Id` (entityId), regarding name, and link all resolve to the same source record — the deterministic renderer (011 `BuildDeterministicBullet`) builds one bullet per source item, so a mixed bullet is impossible; the corpus proves it with confusable-title fixtures.

## Deterministic-renderer unit tests — `src/client/shared/Spaarke.DailyBriefing.Components/src/components/__tests__/deterministicRenderer.test.tsx`

Client-side (Jest): render `NarrativeBullet`/the deterministic row from a single item; assert the link target === the item's link, title === item's narrative. Small, unambiguous — can be authored immediately.

## Overlap note (avoid duplicate coverage)

Property families 3 (grounding round-trip) and 4 (anchor-drop) already have unit coverage from tasks 013 (`DailyBriefingTldrFactsTests`) and 014 (anchor-resolution tests). The accuracy corpus **consolidates** them into the gated family + adds the missing family-1 (cross-pairing) and family-2 (aggregation) corpus — it does not duplicate; it references the existing unit tests as the fine-grained proof and adds the corpus-level integration proof + the merge gate.

## Estimate & open decisions for sign-off

1. **Schema OK?** (item-level `sourceRecords` + `expect` block, above) — or adjust fields.
2. **CI gate**: reuse `Category=GoldenUtteranceEval` trait (recommended, no CI edit) **vs** explicit `BriefingAccuracyEval` filter add (touches hot-path `sdap-ci.yml`)?
3. **Corpus size**: propose ~8–12 cases (2–4 per family). Confirm depth.

On confirmation I'll author the corpus + suite + renderer tests, run green, and wire/verify the gate. This is the last non-deploy task; everything after (017/024/038 deploy + browser UAT, 022 operator sign-off, 090 wrap) needs the live environment + you.
