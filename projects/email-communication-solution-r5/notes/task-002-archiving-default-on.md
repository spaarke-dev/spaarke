# Task 002 — FR-17 Archiving default-on for monitored email accounts

**Status**: complete · **Rigor**: FULL · **Model**: opus @ high · **Date**: 2026-07-28

## Empirical current-state finding (Step 1)

FR-17 default-on was **already the effective behavior** before this task — confirming the POML's grounded 2026-07-27 reading. Trace:

- **Flag mapping** — `CommunicationAccountService.MapToCommunicationAccount` (lines 155-157):
  `sprk_archiveincomingoptin` absent on the Dataverse entity → `ArchiveIncomingOptIn = null`; present → its bool value.
- **Monitored scope** — the processor only ever sees accounts returned by `QueryReceiveEnabledAccountsAsync`, whose filter is `sprk_receiveenabled eq true and statecode eq 0`. So every account reaching the archive gate is monitored **by construction**; there is no non-monitored code path in this processor (a non-monitored account resolves to `null`).
- **Archive gate** — `IncomingCommunicationProcessor.ProcessAsync` Step 6 was `if (account?.ArchiveIncomingOptIn != false)`.

Truth table (pre-change and post-change are identical):

| account | flag | archives? |
|---|---|---|
| null (unresolved) | — | YES (defensive default) |
| monitored | unset (null) | **YES — default-on** |
| monitored | true | YES |
| monitored | false (explicit opt-out) | NO (skipped + informational log) |

**Conclusion**: no behavioral flip needed. This task is **contract-pinning + test coverage**, not a change to what archives.

## Change made (Steps 2-4) — minimal, behavior-preserving

The `ProcessAsync` archive path is untestable end-to-end (every `ProcessAsync`-level test in `InboundPipelineTests.cs` is `Skip`'d — "Graph SDK sealed classes cannot be mocked with Moq"). To make the default-on contract **pinnable by a unit test** without a Graph-wrapper refactor (which would broaden scope well beyond archiving), the gate decision was extracted into a pure derivation method on the existing domain model:

- **`CommunicationAccount.ShouldArchiveIncoming()`** (new) = `ArchiveIncomingOptIn != false`. Consistent in shape with the existing `DeriveAuthMethod()` / `DeriveSubscriptionStatus()` derivations on the same model (extend-existing, not a new component — no §11 justification required). XML doc states the FR-17 default-on, forward-only contract.
- **Gate refactor** — `IncomingCommunicationProcessor.cs` Step 6: `if (account?.ShouldArchiveIncoming() ?? true)`. Exactly reproduces the prior truth table (null account → `?? true` → archive; non-null → predicate). Clarifying FR-17 comment added.

Semantic equivalence verified: `account?.ShouldArchiveIncoming() ?? true` ≡ `account?.ArchiveIncomingOptIn != false` for all four cases above.

## Scope containment (Step 3 + criteria)

- No change to attachment processing (Step 5), thread continuity/association (4.5/4.6), participant index (4.7), send, or mark-as-read (Step 7). Diff is confined to the archive gate + its derivation + the test.
- Explicit opt-out (`false`) still honored — the existing "EML archival skipped" informational log path is unchanged.
- **Forward-only**: nothing in this change triggers or schedules archival of historical mail. No backfill.

## Tests (Step 4)

Added to existing `CommunicationAccountModelTests.cs` (sibling of the existing model-derivation tests):
- `ShouldArchiveIncoming_WhenFlagUnset_ReturnsTrue` — monitored + unset → default-on.
- `ShouldArchiveIncoming_WhenFlagExplicitlyTrue_ReturnsTrue`.
- `ShouldArchiveIncoming_WhenExplicitlyOptedOut_ReturnsFalse` — explicit opt-out honored.

Maintain-class: they break loudly if someone flips default-on to opt-IN. Result: **17/17 passed** (14 pre-existing + 3 new).

## Build / size / CVE (Step 5)

- `dotnet build -c Release` → **0 errors** (pre-existing warnings only).
- Compressed publish (incl. PDBs): **51.29 MB** vs ~49.63 MB baseline. Under the 60 MB hard ceiling and the 55 MB architecture-review line; the ~1.66 MB apparent delta is compression-tooling variance — this change adds **no package** and is boolean-logic-only, so it cannot contribute MB.
- CVE scan: `System.Security.Cryptography.Xml 8.0.3` HIGH is **pre-existing transitive** debt. No `.csproj` change in this task → **0 new HIGH CVE introduced**.

## Placement Justification (BFF §10, cite `.claude/constraints/bff-extensions.md`)

Change lives entirely in the existing `Services/Communication/` ingestion path behind the ADR-045 boundary. No new endpoint, service, DI registration, package, or background work. No CRUD→AI dependency. Test obligation (§F) satisfied by the 3 added model tests. Belongs in BFF (the inbound ingestion pipeline is BFF-resident by construction; not event-driven out-of-band work).

## ADR notes

- ADR-045 (Communication) — Path C (comply): archive gate/default only; no change to Association Engine, enrichment, send, or `.eml` writer. Consistent with the ADR's best-effort/non-fatal archival posture.
- ADR-018 (config/feature flags) — the opt-out is a per-account data flag on `sprk_communicationaccount`, not a kill-switch options class; no partial-behavior-when-disabled concern (opt-out cleanly skips + logs).
