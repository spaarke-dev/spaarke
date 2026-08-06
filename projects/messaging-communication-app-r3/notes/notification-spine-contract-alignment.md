# Notification-Spine Contract Alignment — Input for the Task-045 P1 Gate

> **Date**: 2026-07-20 · **From**: `spaarke-notification-spine-r1` (authoritative copy: main repo `projects/spaarke-notification-spine-r1/design.md`; the email-r4-worktree copy referenced by task 045 is synced to it)
> **Spine status**: combined-scope design (path iii — absorbs assistant R1.5), pre-`/design-to-spec`. Spine design **§5A.7** records the full R3 consumer verification.

## Answers to R3's unresolved spine questions (spec "Unresolved Questions" #1)

1. **Producer trigger — on-capture vs on-send**: **Both. The trigger is persistence** — `communication-arrived` fires when a `sprk_communication` row is written, inbound capture and outbound send identically, all channels, no assessment prerequisite (spine §5A.2/§5A.6 #2).
2. **Producer ownership**: **The spine emits; R3 consumes only.** Task 045's step 2 ("emit from the BFF") should be reconciled at P1 to "verify the spine's emit fires for message + email persistence" — the spine's R1 owns the persist-path emit for all channels (single integration point, §5A.1). Do not wire a producer in R3.
3. **Consumer API**: defined at spine spec time, committed shape per §5A.7 #2 — a **host-agnostic shared client subscriber library** (usable from the workspace widget, the record-form PCF, and the standalone code page — this requirement was added to the spine because of R3's three hosts), a BFF negotiate endpoint, the §5A.3 envelope (`kind`, `communicationId`, `threadId`, `channel`, `direction`, `senderDisplay`, optional privacy-gated `snippet`, `badgeDelta`), and a `kind`-generic pending/poll fallback endpoint (ADR-032 degrade → R3's polling-only path).

## Coordination

- The spine's arrived-producer touches the same `Services/Communication/` persist path R3's Phase-1 wave (002–005) is editing serially — `/conflict-check` + merge-order between the two projects before either lands that touch.
- The spine plans the arrived-producer in its **core wave** (needs only outbox + delivery layers), specifically so task 045 unblocks as early as possible.
- Minor POML note: task 045's dep annotation says "003 (participant junction)" — R3's 003 is the list-threads endpoint; the junction was R2's 003. Cosmetic; fix when convenient.

Task 045 stays `blocked` until the spine's spec formalizes the above; this note is the design-level confirmation its escalation trigger asks for.
