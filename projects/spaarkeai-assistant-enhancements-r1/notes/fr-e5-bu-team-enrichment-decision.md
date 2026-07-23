# FR-E5 BU/team enrichment (un-defer D-032-01) — implementation decision record

> **Un-defers**: D-032-01 (the BU/team half of FR-E5 that task 032 §4 recommended deferring; owner
> un-deferred). The role half already ships via task 030's stated-role render.
> **Scope**: BFF hot chat-path, `Services/Ai/Context`. Additive C# only. **Date**: 2026-07-16.

---

## (a) How BU/team NAMES are resolved + caching/latency decision (NFR-03)

**New reader**: `Services/Ai/Context/UserOrgContextReader.cs` (`IUserOrgContextReader` + `UserOrgContext`
record + `UserOrgContextRenderer` + `NullUserOrgContextReader` ADR-032 P2 default).

- **Identity reuse (no second mechanism)**: the reader keys off the SAME server-resolved `systemuserid`
  `ContextBinder.ResolveCallerSystemUserIdAsync` already produces, then calls
  `IIdentityNormalizationService.ResolveAsync(guid)` (Singleton, MembershipModule) for the caller's
  `BusinessUnitId` + `TeamIds[]`. Those IDs are already Redis-cached (10-min TTL) by that service — no new
  identity path.
- **Name resolution**: `businessunit.name` via one keyed `RetrieveAsync("businessunit", id, ["name"])`;
  team names via one keyed `RetrieveMultiple` on `team` with `teamid In (…)` projecting `name`. Team names
  are sorted **Ordinal in the reader** (renderer preserves order) → byte-stable.
- **Caching (NFR-03)**: the resolved `{BusinessUnitName, TeamNames[]}` bundle is Redis-cached via
  `ITenantCache.GetOrCreateAsync`, resource `user-org-context`, **per-`systemuserid` key, 10-minute TTL** —
  the SAME pattern `IdentityNormalizationService` uses (ADR-009). On a warm cache the hot bind path pays
  **zero** Dataverse reads for names; on a cold cache it pays the two cheap keyed reads on top of the
  (cached) identity IDs. Empty results are cached too, so an org-less user does not re-read every turn.
- **Soft-fail-to-null**: any identity/name/cache failure degrades the org block to **absent** (mirrors the
  stated-profile soft-fail); a bind is never taken down by this read. `NullUserOrgContextReader` keeps the
  block absent when the reader is unregistered.

**Why a dedicated reader, not extend `PersonIdentity`**: folding names into `PersonIdentity` would force
name reads on **every** membership-resolution consumer + a cache-schema bump — widening a shared hot path
(§10/§11). A dedicated reader isolates the org-name read + its own cache to the ContextBinder user-fragment
path (lower blast radius). It is NOT jammed into `StatedProfile` (different source — systemuser org
membership vs `sprk_userprofile`), and it is NOT the deferred `IOrganizationalContextProvider` (that is
inbound org-scope context for grounding; this is preference-only prompt bias).

## (b) Composition into the User fragment (byte-stability)

`ContextBinder.ResolveUserFragmentAsync` now composes THREE sibling producers off the one resolved
systemuserid, in a fixed deterministic order joined by a blank line:

1. stated-profile block (`### Your Profile (stated)`) — task 030
2. **org block (`### Your Organization`)** — NEW (this change)
3. user-memory recall block

The refactor is behavior-preserving when the org block is absent (stated-only → stated; memory-only →
memory; stated+memory → `stated\n\nmemory` — the existing goldens are unchanged). The org block is its own
`UserOrgContextRenderer.Render` output: fixed field order (Business Unit → Teams), team names in Ordinal
order, no clock/GUID → byte-stable (NFR-02/04). Existing byte-stability goldens
(`StatedProfileRendererTests`, `ContextBinderStatedProfileTests`, `ContextEnvelopeRendererTests`) did **not**
need re-baselining — the addition is purely additive and null when no org reader is present.

## (c) Measured fragment size vs the 700 budget

`EnvelopeBudget.User` = **700** (task 032). The org block adds ~25–50 tokens (heading + BU line + teams
line). Realistic worst composition = ~532 (task 032's two-producer worst) + ~28 (org) ≈ **~560**, inside the
700 ceiling (~140 tokens headroom). `ContextBinderOrgContextTests.BindAsync_ComposedUserFragmentWithOrgBlock_StaysWithinUserBudget`
pins a heavy-but-realistic composition (heavy stated free text + full 250-token memory recall + full org
block) and asserts the User slice is **not breached**. **Escalation did NOT fire** — no budget bump needed.

## (d) ADR-039 preference-only pin

BU/team is CONTEXT that biases the one turn's prompt ONLY. It is composed exclusively into the User
`Fragment` text; it never reaches `AgentToolFilterContext` / grounding / dispatch (task 031 invariant; task
052 F3/F4). Pinned by `ContextBinderOrgContextTests.BindAsync_OrgContext_ConfinedToUserFragment_AndAgentToolFilterContextHasNoOrgMember`
(org text lands only in `User.Fragment`; `AgentToolFilterContext` remains the 4 structural-facts-only
members). BU/team names are system-owned (not user-authored free text), so — unlike the stated-profile
free-text fields — no `«...»` untrusted-content guard is needed.

## (e) Files changed

- **NEW** `src/server/api/Sprk.Bff.Api/Services/Ai/Context/UserOrgContextReader.cs`
- **NEW** `src/server/api/Sprk.Bff.Api/Services/Ai/Context/UserOrgContextRenderer.cs`
- **EDIT** `src/server/api/Sprk.Bff.Api/Services/Ai/Context/ContextBinder.cs` (ctor param + 3-block composition + `ResolveOrgContextFragmentAsync`)
- **EDIT** `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` (register `IUserOrgContextReader`)
- **NEW tests** `UserOrgContextRendererTests.cs`, `UserOrgContextReaderTests.cs`, `ContextBinderOrgContextTests.cs`

## (f) §10 BFF hygiene

- **Placement**: `Services/Ai/Context/` (ADR-013 in-zone AI code), latency-coupled to the per-turn bind.
- **No new package** → no new CVE surface. Additive C# only → publish-size delta ≈ 0 (see task summary).
- **Component justification** (§11): concrete failure mode if absent — a profiled turn cannot carry the
  caller's BU/team context; the BU/team half of FR-E5 stays unshipped.
