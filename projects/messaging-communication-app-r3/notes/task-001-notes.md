# Task 001 — Characterize existing Communication read + send paths (baseline tests)

**Status**: Complete. Characterization-only — no `src/**` file was modified. All new tests are GREEN against the
unedited tree.

## What this task did

Authored/extended baseline tests pinning the CURRENT observable behavior of `CommunicationThreadReadService`
(read paths) and `CommunicationService` (outbound send thread-resolution) so the Phase-1 edits (tasks 002-005,
FR-16/17/18/19) surface as intentional, reviewable diffs rather than silent regressions.

## Files touched

- `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationThreadReadServiceTests.cs` — **extended**
  with 2 new tests (DTO-shape baseline).
- `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationServiceEmailSendThreadTests.cs` — **new
  file** (2 tests; email-send thread-resolution baseline).

No other file under `tests/**` or `src/**` was created or modified by this task.

## Baseline snapshot (what's pinned, and why)

### 1. Read DTO shape (current, pre-Phase-1)

- `ThreadReadResult.Name == null` for the R1 per-thread read (`ReadThreadAsync`) — new test
  `ReadThreadAsync_SingleThreadRead_NameIsAlwaysNull`. (By-regarding read populates `Name`; already covered by
  existing `CommunicationByRegardingReadTests`.)
- `ThreadMessageDto` carries `From` (string) and has **no** `Direction` / `IsInbound` / `IsOutbound` /
  `SenderId` / `SenderIdentity` property — new test
  `ThreadMessageDto_CurrentShape_ExposesFromWithNoDirectionOrSenderIdentityField` (reflection-based property-set
  assertion on the record type). This is the explicit "current shape" pin the POML asked for; when FR-18/19 add
  a direction/sender-identity field, this test's expected-property list is the reviewable diff.

### 2. Impersonation + shared access-filter invariant — CONFIRMED via EXISTING coverage (no new test needed)

- Internal-only rows hidden from a non-internal caller: `CommunicationThreadReadService` hardcodes
  `IsInternalUser: true` for every read (R1/R2 callers are internal by construction — see the service's own doc
  comment), so there is no route to exercise "hidden from a non-internal caller" *through the service* today.
  The invariant is already proven directly against the REAL, shared `CommunicationAccessFilter` by
  `CommunicationByRegardingReadTests.CommunicationAccessFilter_InternalOnlyMessage_HiddenFromNonInternalCaller`
  and exhaustively by `CommunicationAccessFilterTests`. No duplicate test added (root CLAUDE.md §11 reuse-first).
- Unresolved caller fails closed (403), no app-only fallback: already covered by
  `CommunicationThreadReadServiceTests.ReadThreadAsync_UnresolvedCaller_ThrowsForbiddenAndNeverQueries`,
  `GetUnreadCountAsync_UnresolvedCaller_ThrowsForbidden`, and
  `CommunicationByRegardingReadTests.ReadByRegardingAsync_UnresolvedCaller_ThrowsForbiddenAndNeverQueries`.

### 3. No-membership-union structural guard — CONFIRMED via EXISTING coverage (no new test needed)

- `CommunicationWorkspaceReadSeamTests.NoMembershipUnionRegression_ReadServiceAndAccessFilter_NeverDependOnRetiredGrantOrMembershipSeams`
  already asserts, via constructor-shape reflection, that neither `CommunicationThreadReadService` nor
  `CommunicationAccessFilter` depends on `IThreadPrivateGrantProvider` (or any membership-resolution seam), and
  that the read service's constructor has exactly 4 parameters (impersonated query, shared filter, caller
  resolver, logger). Ran green in this task's `dotnet test` pass — see test output below.

### 4. Outbound send thread-resolution — NEW baseline (the load-bearing contrast)

- **Email** (`CommunicationServiceEmailSendThreadTests`, new file):
  - `SendAsync_ForEmailType_WithNoThreadId_UsesFindOrCreateThreadResolver` — no `ThreadId` ⇒ the find-or-create
    `IThreadResolver` ladder runs (`ResolveOutboundThreadAsync`).
  - `SendAsync_ForEmailType_WithThreadId_StillUsesFindOrCreateResolver_ThreadIdCurrentlyIgnored` — **the
    key pre-FR-19 pin**: supplying `request.ThreadId` on an EMAIL send does **not** change behavior at all.
    `ResolveOutboundThreadAsync` never reads `request.ThreadId` (confirmed by direct code inspection — see
    `CommunicationService.cs` `ResolveOutboundThreadAsync`, ~line 442, and the field's own XML doc comment on
    `SendCommunicationRequest.ThreadId`: *"Ignored for Email sends"*). The find-or-create resolver runs
    identically whether or not `ThreadId` is supplied, and no direct `sprk_communicationthread` stamp
    (`UpdateAsync`) ever occurs on the email path. FR-19 is expected to add explicit-ThreadId honoring for
    email; when it lands, this test's assertions become the documented, reviewable diff.
- **Message** (already fully covered by existing `CommunicationServiceMessageSendTests`, no new test needed):
  - `SendAsync_ForMessageType_WithThreadId_StampsThreadLookupDirectlyAndGrantsAccess` — WITH `ThreadId`, the
    message path bypasses find-or-create entirely and stamps `sprk_communicationthread` directly via
    `AssignExplicitThreadAsync`, then chains the task-043 `IDirectThreadAccessService` grant.
  - `SendAsync_ForMessageType_WithoutThreadId_StillUsesFindOrCreateResolver` — WITHOUT `ThreadId`, the message
    path uses the find-or-create `IThreadResolver` ladder (parity with the email path's unconditional behavior).

This is the asymmetry the baseline exists to pin: **today, Email always ignores `ThreadId`; Message honors it
when present.** Any Phase-1 task that changes Email's behavior to honor `ThreadId` (FR-19) will need to update
`SendAsync_ForEmailType_WithThreadId_StillUsesFindOrCreateResolver_ThreadIdCurrentlyIgnored` — that update IS
the reviewable diff proving the change was intentional.

## Escalations / gaps

None. Every acceptance-criterion behavior in the POML was either directly observable through the existing
`IImpersonatedCommunicationQuery` / `ICallerSystemUserResolver` / `IThreadResolver` / `IGenericEntityService`
test seams, or already covered by pre-existing tests (see §2/§3 above). No source change was required to
characterize any behavior — the escalation trigger in the POML did not fire.

## Verification results (this task)

- `dotnet build src/server/api/Sprk.Bff.Api/` — **0 errors** (19 pre-existing warnings, none introduced by this
  task's test files).
- `dotnet test` filtered to the touched/related Communication read + send test classes — **51 / 51 passed**
  (`CommunicationThreadReadServiceTests`, `CommunicationByRegardingReadTests`, `CommunicationServiceMessageSendTests`,
  `CommunicationServiceEmailSendThreadTests`, `CommunicationWorkspaceReadSeamTests`).
- `dotnet list package --vulnerable --include-transitive` (Sprk.Bff.Api) — **1 pre-existing HIGH** advisory on
  `System.Security.Cryptography.Xml 8.0.3` (4 advisory URLs, same package). This task added **zero** NuGet
  package references (test-only C# files against existing test-project dependencies), so this is **not a new
  CVE introduced by task 001** — it is pre-existing repo state. Flagging for awareness; not this task's scope to
  remediate.
- BFF publish-size impact: **~0** — this task is tests-only (no `src/**` change, no new package reference), so
  no `dotnet publish` delta is expected. Not re-measured (no production code touched).
- `git diff --name-only` / `git status --porcelain` at task completion showed 3 files this task did **not**
  touch already dirty in the worktree at task start: `docs/data-model/sprk_communication.md`,
  `projects/messaging-communication-app-r3/current-task.md` (both modified), and
  `projects/messaging-communication-app-r3/notes/task-006-notes.md` (untracked). These pre-date this task's
  execution — confirmed by diffing only the two files this task actually wrote content into. This task's own
  file-level footprint is exactly:
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationThreadReadServiceTests.cs` (modified —
    extended)
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationServiceEmailSendThreadTests.cs` (new)
  - `projects/messaging-communication-app-r3/notes/task-001-notes.md` (this file, new)
