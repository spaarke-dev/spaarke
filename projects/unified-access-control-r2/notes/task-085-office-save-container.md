# Task 085 — `POST /api/office/save` now derives its container from the authorized record

> **Completed** 2026-08-30 · FULL rigor · opus @ high
> **Files**: `Models/Office/SaveRequest.cs`, `Services/Office/OfficeService.cs`,
> `tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs`,
> **NEW** `tests/integration/auth/UnifiedAccessControl/OfficeSaveContainerProvenanceTests.cs`

---

## 1. The defect

The route carries `.AddEntityAccessFilter()`, which authorizes the caller against
`SaveRequest.TargetEntity` — and then wrote the bytes into `SaveRequest.ContainerId`, **a different
client-supplied field on the same body**. The authorization key and the write destination were two
independently caller-chosen values for one decision, on an **app-only MI write** where no SPE ACL would
catch a mismatch. Task 083 named that shape **option (B)** and rejected it; this was it, live.

Two facts compounded it:

- `TargetEntity` is **optional**, and `EntityAccessFilter.cs:148-159` calls `next(context)` when the
  target is absent — so a save could run on baseline Office authentication alone and still name its own
  container.
- The value did not stop at the request. `OfficeService.cs:169` serialized it into the **ProcessingJob
  payload**, so a client-chosen container outlived the call inside a Dataverse row, and the async
  attachment upload in `UploadFinalizationWorker` read it back.

No shipped client ever sent it — the add-in body is `contentType/email/targetEntity/aiOptions/
documentMetadata`. **The hole was the contract, not the traffic.**

## 2. The fix

`SaveRequest.ContainerId` **deleted** (tombstoned in place, following the `FolderPath` precedent on the
same type). New `OfficeService.ResolveContainerAsync`:

- **`TargetEntity` present** → task 076's `RecordContainerResolver.ResolveForRecordAsync` derives the
  container from *that* record — the same one the filter authorized. Authorization key and write
  destination are now **one value**, so no code path can let them disagree. The resolver is secure-aware:
  a secure record's own container wins.
- **`ContainerDecisionOutcome.FailClosed`** (secure record, no container of its own) → **refuse**.
  Falling through to the shared default would place secure content where SPE cannot un-share it.
- **No `TargetEntity`** → `EmailProcessing:DefaultContainerId`, server-side and fail-closed when unset.

Derivation happens **before the payload is serialized**, so the job row and the synchronous upload carry
the same server-derived value. That is what closes the worker half — the fix had to reach the payload,
not just the upload.

## 3. Deviation from the POML brief, stated plainly

The POML (which I wrote) specified: *no record → derive from the acting user's business unit*. **I did
not implement that**, and the reason is in `RecordContainerResolver`'s own contract, §*"Why the RECORD's
business unit and not the ACTING USER's"*:

> *users sit in the Operations subtree while secure records are owned in `Secure Projects`, so
> acting-user resolution writes a secure record's content into the general Operations container*

That is the exact isolation failure this project exists to close. Two further facts made the acting-user
branch unnecessary here:

1. The owner's Q1 answer sanctioned acting-user BU for **task 076's three upload-before-a-record-exists
   client paths** (EmailComposer local attachment, Analysis wizard standalone doc, DocumentUploadWizard
   skip-associate). Office save is a different surface.
2. Every shipped add-in path sends a `TargetEntity`, so Office save's no-record branch is
   **contract-only**. Building a new acting-user derivation for it would add a component with no traffic
   (CLAUDE.md §11) that the resolver's own docs argue against.

The binding requirement from Q5 — *"accepting one in the request is the vulnerability"* — is fully met:
the container is server-derived in both branches. Q5's other half is also preserved: the response still
returns the chosen `driveId`, which the client needs for `sprk_graphdriveid` and `indexFile()`.

## 4. Escalation trigger did NOT fire

The POML's trigger: *stop if a shipped Office add-in build sends `ContainerId`.*

One scare: `useSaveFlow.ts:62` declares `containerId?: string`. It is on **`JobResultArtifact`** — the
**response** shape — and appears exactly once in the file. That is Q5 working as designed. Confirmed
across all five files referencing `office/save`: no client sends it in a request, and the quick-save
body type enumerates its fields explicitly without it.

## 5. Verification

| Check | Result |
|---|---|
| Build | 0 warnings / 0 errors |
| New contract tests | **3 / 3** |
| Full BFF suite | **10,908 passed / 0 failed / 72 skipped** (10,896 + 9 anonymous controls from 091 + 3 here) |
| ArchTests | 144 passed / **6 failed = the clean-tree baseline** |
| Publish | **45.12 MB** compressed incl. PDBs — **0.00 MB delta**; ceiling 60 |
| CVE | `no vulnerable packages`; none added |

**Perturbation**: reinstating `ContainerId` on `SaveRequest` reddens **2 of 3** contract tests.

Provenance guard: both Office sinks moved `ClientSupplied` → **`ServerDerivedRecord`**, owner re-pointed
to `085 (CLOSED)`. The original finding text is retained inside each entry rather than deleted — it is
the clearest surviving statement of why this guard is keyed on **sinks rather than routes**: the client
value crossed three frames (`OfficeEndpoints` → `OfficeService` → `OfficeStorageUploader`) before
reaching the sink, so no route-level census could see it.

## 6. Why the tests assert the contract, not the behaviour

No client sent `ContainerId`, so a behavioural test over today's callers would have **passed throughout
the defect's life**. What must stay true is that the field cannot come back — a property that still
deserializes is one a future change starts honouring again. `FolderPath` sat dormant and always-null on
this same type for the life of the feature and was removed for exactly that reason.

So `SaveRequest_ExposesNoPropertyThatNamesAStorageContainer` checks for **any** container- or
drive-naming property rather than the one historical name: the defect is the *capability*, not the
spelling, and reintroducing it as `DriveId` would be the same hole with a different label.

## 7. Placement Justification (CLAUDE.md §10)

No new endpoint, service, interface, DI registration, package, or map. One existing service gains one
existing scoped dependency (`RecordContainerResolver`, already registered at `Program.cs:63`, already
consumed by tasks 076/078 and the Communication path). A property is **removed** from an existing
contract. Publish size unchanged.
