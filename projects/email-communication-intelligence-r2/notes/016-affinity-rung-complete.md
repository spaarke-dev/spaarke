# Task 016 — AffinityRung + sprk_affinity learning store (FR-A4) — COMPLETE (2026-08-06)

Rigor FULL · opus·high. Deterministic learning loop shipped; ADR-040 Path A honored; one escalation fired + documented.

## What shipped
- **`RungKind.Affinity = 10`** — new suggest-only deterministic rung kind.
- **`Configuration/AffinityOptions.cs`** — `Communication:Affinity` section, `IOptionsMonitor`, per-tenant `Enabled`
  override (`IsEnabledFor(tenantKey)`), `MinConfirmations` (3), `SuggestConfidence` (0.60, Suggested band),
  keyword bounds. Kill-switch flips without redeploy (ADR-018).
- **`Services/Communication/Engine/AffinityStore.cs`** — read (`GetTopAffinityAsync`: one query, OR-of-(type,value)
  pairs, tenant-scoped, top by confirmation count) + increment writer (`RecordConfirmationAsync`: query-then
  create/update on the `sprk_name` upsert key). Self-contained over `IGenericEntityService` — **no new Dataverse
  seam**. Both best-effort (NFR-04): read returns null on failure, writer never throws.
- **`Services/Communication/Engine/Rungs/AffinityRung.cs`** — computes signals, reads the store, emits ONE
  suggest-band `RungMatch` on `RegardingFieldMap.FieldFor(targetEntity)` citing the confirmation count. Public
  static `ExtractSignals` so the (future) confirmation writer reuses identical canonicalization.
- **`AssociationStatusMapper.cs`** — doc-only: `IsAutoFileEligible` + `IsDeterministicWriteEligible` now explicitly
  note Affinity is excluded (by omission) — the structural never-auto-file guarantee.
- **`IncomingAssociationResolver.cs`** — `RungKind.Affinity` added to `IsDeterministic` so the rung RUNS in the
  deterministic pass (like RecordNameMatch/ContactNameMatch) yet stays out of both mapper eligibility sets.
- **`CommunicationModule.cs`** — `Configure<AffinityOptions>` + `AffinityStore` + `AffinityRung` registered
  unconditionally (ADR-010), self-gated by config.
- **`tests/.../AffinityRungTests.cs`** — 12 tests: surfacing after N confirmations (cites count); no-row / unmapped
  target → empty; global + per-tenant kill-switch → empty AND no Dataverse cost; **never-auto-files at 0.60 AND
  0.99 through the REAL mapper**; `ExtractSignals` pure logic; writer increment vs create vs best-effort-no-throw.

## Schema (operator-created, verified via MCP)
`sprk_affinity`: `sprk_name`(850, upsert key), `sprk_signaltype` CHOICE {Sender 100000000, Sender Domain …001,
Subject Keyword …002, Participant Set …003}, `sprk_signalvalue`(1000), `sprk_targetentity`(100),
`sprk_targetid`(100), `sprk_confirmationcount`(int), `sprk_lastconfirmed`(datetime), `sprk_tenantkey`(128). The
code matches this shape exactly. (POML step 1 — create schema — was done by the operator ahead of coding.)

## ADR-040 Path A boundary (why affinity ≠ ledger ≠ participant index)
- **Session ledger (ADR-040)**: per-SESSION disposition (what the user did with a message this session). Ephemeral,
  session-scoped.
- **Participant index (ADR-048)**: per-MESSAGE junction of (message × party × role) — powers the participant facet.
- **`sprk_affinity` (this task)**: cross-message, per-tenant FREQUENCY accumulation of confirmed (signal → record).
  None of the three subsumes the others; affinity is the only one accumulating confirmation frequency by signal
  type. Kept a genuinely distinct, inspectable store — the accepted Path A exception.

## 🔔 Escalation fired — confirmation-write hook is r5-owned (documented, NOT worked around)
POML escalation trigger #2: *"If recording confirmations requires a write at a confirmation site outside this
task's scope (r5 review surface), note the seam and escalate rather than editing r5-owned code."* This fired:
- The human "confirm/change which record" UI is **r5-owned** (r5 coordination note §1; r5 CLAUDE.md: use the
  additive `applyRegardingSelection` path). That write is **client-side host-context `Xrm.WebApi`, bypassing the
  BFF** — there is NO server-side confirmation site in this repo to hook.
- **Action taken (directional mode):** built + unit-tested the write capability (`AffinityStore.RecordConfirmation
  Async`, ready seam) but did **NOT** edit r5 code and did **NOT** dangle a caller-less BFF endpoint (§11). The
  wiring is a two-sided contract whose other side is r5.
- **FR-E6 coordination ask (for r5):** on a confirmed association, call `AffinityStore.RecordConfirmationAsync`
  for each signal `AffinityRung.ExtractSignals(envelope)` produces (same canonicalization). Since the r5 write is
  client-side, this needs EITHER a thin BFF endpoint r5 calls on confirm, OR a Dataverse plugin/webhook observing
  `sprk_communication` regarding+status→Resolved transitions. **Track via `/defer` + the r5 coordination note.**
  Until wired, affinity rows accumulate ONLY if some path calls the writer — so FR-A4's learning loop is
  READ-COMPLETE, WRITE-PENDING-r5-wiring.

## Design decision — affinity CAN write a Suggested core regarding (not surface-only)
Mirroring RecordNameMatch (per POML "mirror RecordNameMatch/ContactNameMatch"): excluded from both eligibility
sets → never Resolved. A core-entity affinity target at ≥0.50 is written as a **Suggested** regarding (status
never Resolved), consistent with the owner's "core records may be auto-associated; status stays Suggested" rule.
A stronger content rung on a different target wins the write (higher FullConfidence); affinity never overrides it.
Acceptance criterion 2 ("always at most Suggested") holds — verified by test at 0.60 and 0.99.

## Verify
Build 0 err · Affinity 12/12 + Communication suite 774/774 green (5 pre-existing skips) · CVE clean · publish
**48.29 MB** compressed (≤60 MB; flat vs 021's 48.28 — no packages) · /conflict-check: no other worktree PR touches
RungKind/AssociationStatusMapper/CommunicationModule/IncomingAssociationResolver. Step 9.5: 0 violations, 0 warnings.

## Placement Justification (§10 — for the PR)
New store + rung + options live in `Services/Communication/Engine` behind the ADR-045 boundary, reusing
`IGenericEntityService` (no new access path, no new Dataverse-seam interface). No endpoint, no package, no
conditional DI. §11: `<existing>` none (grep affinity → none); `<extension>` ledger/participant-index serve
different purposes; `<cost-of-doing-nothing>` cold inbound never gets easier + no deterministic corpus for the
deferred ML ranker.
