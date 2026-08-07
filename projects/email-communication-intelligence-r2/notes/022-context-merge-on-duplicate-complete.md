# Task 022 — FR-C2 context-merge on duplicate — CODE COMPLETE (2026-08-06)

Rigor FULL · sonnet·high (ran on Opus) · parallel-safe:false. When task-021 dedup collapses N copies of an email
to one canonical `sprk_communication`, the discarded copies' delivery context is now **merged onto the canonical
row** instead of dropped — no delivery fact lost.

## The correct model (owner: "what is the correct functional + technical solution?")

The per-copy UNIQUE fact is the **delivering mailbox** (recipients/subject/body are identical across copies of one
email) — plus, on the upload side, the **saving user**. Both are SETS. The correct model is **two additive
set-union memo columns on the canonical row** (ADR-045 — extend, the row stays the reconciliation unit; NOT a
child entity, NOT overloading recipients):

- `sprk_deliveredmailboxes` — `; `-delimited **set-union** of monitored-mailbox addresses that delivered a copy.
- `sprk_savedbyusers` — set-union of user identities that saved a copy via upload.

Set-union (not concat) gives **idempotency** (Service-Bus redelivery of the same duplicate adds nothing).

## What shipped (code — non-gated)

- **`Services/Communication/DeliveryContextMerge.cs`** (new, static): `Union(existing, value)` — pure,
  case-insensitive, order-preserving, idempotent set-union (the testable core); `MergeAsync(dataverse, canonicalId,
  attribute, value, …)` — read-modify-write that writes **only when the set changed** and is **non-fatal (NFR-04)**
  (any failure logs + degrades, never throws out of capture/upload).
- **`IncomingCommunicationProcessor.cs`** (inbound) — merges the delivering mailbox at the THREE points context was
  dropped: (1) cross-mailbox dup (was `Exists…` bool → now `TryGetCanonicalByInternetMessageIdAsync`, **fail-open
  preserved**); (2) create-race dup (`wasDuplicate`); (3) **seeds the canonical's OWN mailbox after a successful
  create** so the set is COMPLETE (first + all dups). All three via the non-fatal `MergeAsync` — never the atomic
  entity build — so capture never fails on the new column being absent before its gated deploy (**contract-first**).
- **`OfficeDocumentPersistence.cs`** (office/upload half) — on an email save, if the same email was captured inbound
  (`GetCommunicationByInternetMessageIdAsync` finds a canonical), merges the saver onto `sprk_savedbyusers`. Two
  optional/null-tolerant ctor deps (`ICommunicationDataverseService` + `IGenericEntityService`) → zero test ripple;
  DI resolves both in every host; null → guarded no-op.

## Tests (14; ADR-038 module boundaries)
- `DeliveryContextMergeTests` (10): Union — first / append / idempotent-present / case-insensitive / blank-noop /
  order-preserving; MergeAsync — writes union / no-write-when-unchanged / non-fatal-on-throw / empty-id-or-value noop.
- `CommunicationIntegrationTests.InboundPipeline_CrossMailboxDuplicate_MergesDeliveringMailboxOntoCanonical` — the
  acceptance criterion: same email → second mailbox → canonical lists BOTH; no second communication created.
  (`CreateInboundGraphMock` extended with an optional `internetMessageId`.)
- `OfficeDocumentPersistenceDedupTests.…EmailSave_MergesUploaderOntoCanonicalCommunication` — the "M uploaders" half.
- **1127 Communication+Office green** (0 failed); build 0-err. CVE clean; no package delta (publish ~48.3 MB).

## Acceptance criteria status
| Criterion | Status |
|---|---|
| N mailboxes → canonical reflects all N (queryable) | ✅ inbound merge + integration test (code; live end-to-end pending column) |
| Redelivery → no double-append (set-union) | ✅ idempotent Union + MergeAsync write-only-on-change; tested |
| Non-fatal: merge throws → capture/upload not failed | ✅ MergeAsync + both callers non-fatal; tested |
| M uploaders recorded | ✅ office uploader merge + test |
| Build green; size ≤60 MB + delta; no new HIGH CVE | ✅ 0-err; ~48.3 MB (code-only); CVE clean |

## Step 9.5 (inline)
- **ADR-045** ✅ extends the persist path in place (no fork; canonical stays the unit). **ADR-010** ✅ no new
  interface; `DeliveryContextMerge` is a static helper; optional-trailing-dep pattern on OfficeDocumentPersistence.
  **ADR-027** ✅ the two columns are the gated schema task 028. **ADR-038** ✅ module-boundary mocks; pure-Union
  units; no banned shapes. **§10** ✅ Placement Justification (extends in place, no new service/package), size/CVE.
  **§11** ✅ two columns justified (no existing set-field; recipients ≠ delivering mailboxes; child entity
  discouraged). **NFR-04** ✅ non-fatal throughout.
- **code-review**: caught + fixed the fail-open regression (inline lookup dropped the old helper's try/catch →
  restored as `TryGetCanonicalByInternetMessageIdAsync`); removed the now-orphaned bool helper.

## GATED tail (operator go-ahead) = task 028
The two memo columns `sprk_deliveredmailboxes` + `sprk_savedbyusers` via `dataverse-create-schema` → managed
solution → `spaarkedev1`. Code ships safely behind it (every write is non-fatal → degrades until the columns
exist). Tracked in TASK-INDEX.
