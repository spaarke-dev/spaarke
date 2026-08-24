# Task 022 — the recycle-bin deletion timestamp

> **Task 022** (spec FR-C03) · 2026-08-24 · **complete**
> Escalation trigger evaluated and did **NOT** fire — the deleted-containers surface exists and works.

---

## 1. The POML describes a different bug than the one that exists

> *"The Recycle Bin screen fails with `Could not find a property named 'deletedDateTime' on type
> microsoft.graph.fileStorageContainer`. The `$select` at :4351 requests a property the entity type
> does not declare — and a comment 11 lines below at :4362 already says so."*

**Three claims, all wrong.**

| Claim | Reality |
|---|---|
| The screen **fails** with an OData parsing error | It does **not** fail. The request succeeds and rows are returned. |
| The `$select` requests an **undeclared** property | Graph returns `deletedDateTime` and Kiota parses it — task 040 probed the real SDK and found `TryGetValue("deletedDateTime") = True`, `runtime type: System.DateTime`, `value: 8/1/2026 5:45:00 PM`. |
| The comment below **contradicts** the code | It does not. The comment says the property arrives via `AdditionalData` rather than as a typed member, and the code reads `AdditionalData`. **Comment and code agreed.** |

The "verification" behind this POML was that both lines *exist* — not that the described failure
occurs. The error message appears to be narrative rather than an observation.

### The actual defect, proven by task 040

```csharp
rawDeletedAt is string deletedAtStr && DateTimeOffset.TryParse(deletedAtStr, out var parsed)
//            ^^^^^^ never true — Kiota stores a System.DateTime
```

Graph sent the value. Kiota parsed it. It was sitting in the dictionary. **Production dropped it on a
type check.** `DeletedDateTime` was `null` for every row, so the recycle bin could not sort by deletion
date or age anything out — and "deleted at an unknown time" became indistinguishable from "deleted
just now".

**This project's signature defect shape, ninth instance**: a lower layer collapsing a real value into
an absent one that an upper layer reads as benign.

Note the comment's other half — *"in Graph SDK 5.x"* — is the same expired-version-claim pattern that
task 030 found behind `billingClassification`. Here the claim happened to remain true; the guard next
to it did not. **Task 023 still owns the audit of the remaining `AdditionalData` sites.**

---

## 2. The fix, and why it is at one layer only

`ReadDeletionTimestamp` accepts every shape Kiota can produce — `DateTimeOffset`, `DateTime`
(the observed one), and `string` — rather than guessing one, because the guess is what failed. A
`DateTime` of `Unspecified` kind is read as **UTC**: Graph emits UTC, and assuming local time would
shift every timestamp by the server's offset.

An unreadable or absent value still yields `null`. **Unknown must stay unknown** — substituting "now"
would make an aged-out container look freshly deleted, which is how a retention sweep skips the very
rows it exists to catch. There is a test for that specifically.

### The `$select` was removed, not corrected

Same decision as the container-type list (task 030) and for the same reason: a hand-maintained list of
property names is a standing liability in both directions — a wrong or version-absent name is a hard
400 that breaks the whole view (`storageUsedInBytes` on v1.0), and a name the list merely *forgot*
silently withholds the property from every caller (`owningAppId` on the container-type surface). The
default projection cannot drift out of sync with itself. The `$filter` is unchanged — it is what scopes
the view to one container type.

### 🔑 The DTO and the UI needed no change — and that matters

`DeletedContainerDto.DeletedDateTime` was already `DateTimeOffset?`. `RecycleBinPage.tsx` already
sorts nulls last and already renders a muted **"Unknown"** rather than a blank or a fabricated date.
The empty state is already distinguishable from a failure.

So acceptance criteria 3 and 4 were **already satisfied** — the presentation layer was honest the whole
time and was simply starved by the layer beneath it. Every recycle-bin row has been correctly reporting
"Unknown", for a value the service had in hand and discarded. That is what confirms the fix belongs at
exactly one layer, and that the UI half of the POML's step 3 was unnecessary.

---

## 3. Tests

Task 040 left two characterization tests with an explicit instruction: *"WHEN 022 FIXES THIS, THIS TEST
MUST FAIL AND BE UPDATED."* It did.

| Test | Change |
|---|---|
| `ListDeletedContainers_RequestsDeletedDateTimeInTheSelect` | **Inverted** → `…SendsNoSelect_SoTheProjectionCannotDriftFromTheCode`. Also asserts the filter survives and stays unquoted (ADR-044). |
| `ListDeletedContainers_MapsIdAndDisplayNameButNeverTheDeletionTimestamp` | **Inverted** → `…MapsTheDeletionTimestamp_NotJustIdAndDisplayName`. Asserts the **exact value**, not merely non-null: a fix returning `DateTime.UtcNow` would also satisfy "not null" while being just as wrong. |
| `DeletedContainerPayload_StoresTheTimestampAsDateTimeNotString` | **Kept unchanged.** It pins the root-cause runtime type, so a future Kiota upgrade that changes the representation surfaces here rather than in production. |
| `ListDeletedContainers_WhenGraphOmitsTheTimestamp_LeavesItNull` | **New** — guards the "unknown stays unknown" half. |

Updated in place under `tests/integration/contract/**` (a KEEP path) rather than duplicated into
`tests/integration/regression/**`: the contract tests already cover this exact behaviour, and a second
copy would be the scaffolding `/test-diet` deletes at project close.

---

## 4. Gates

- **BFF build ✅** — 0 errors, 7 pre-existing warnings.
- **Tests ✅** — **10,653 passing** (+1 net: 1 added, 2 inverted), 0 failed, 97 skipped.
- **ArchTests ✅** — 36/36.
- **Publish ✅** — **43.67 MB compressed incl. PDBs, 0 MB delta**. Ceiling 60 MB. No package change.

**Placement justification (root CLAUDE.md §10):** no new endpoint, service, DI registration, or
package — one corrected read inside an existing `Infrastructure/Graph` method, plus one private helper.

⚠️ **Not verified against Spaarke Dev** (step 7 / AC-1). The `az` session has expired and this session
cannot run an interactive login. The behaviour is pinned by WireMock against the real Kiota
deserializer, which is where the defect lived — but a live confirmation that deleted containers list
without error is still outstanding, along with the standing UI-verification gap from tasks 001 / 003 /
012 / 030 / 021.
