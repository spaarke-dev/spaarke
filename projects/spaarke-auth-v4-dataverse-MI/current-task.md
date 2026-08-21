# Current Task State — spaarke-auth-v4-dataverse-MI

> **Last Updated**: 2026-08-20 (task 011 in flight)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | `spaarke-auth-v4-dataverse-MI` — eliminate `BFF-API-ClientSecret`; OBO → MI-FIC |
| **Branch** | `work/spaarke-auth-v4-dataverse-MI` · worktree `c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI` |
| **Task** | **011 — DI lifetimes + ADR-009 cache decision** — implementation DONE, verification in flight |
| **Step** | Step 6 of 6 (verify) → then Step 9.5 quality gates |
| **Status** | in-progress |
| **Next Action** | Confirm full suite green → run `/code-review` + `/adr-check` → mark 011 ✅ → **task 020** |
| **Progress** | 4 of 26 complete · 3 deferred · 22 remaining (011 closing) |

### Repo state

| Check | Value |
|---|---|
| Merged `origin/master` | ✅ `8dcb21c96` — **god-class ratchet RETIRED on master**, CI red resolved |
| Behind master | 0 |
| ArchTests | ✅ 36/36 |
| Publish | 43.67 MB compressed incl. PDBs — **zero delta**; ceiling 60 |
| CVE | ✅ clean |

### Files modified (task 011, uncommitted)

- `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` — static (tenant|client) CCA cache
- `src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs` — same cache
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs` — Scoped → **Singleton**
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs` — comment only (registration deliberately unchanged)
- `tests/integration/seam/Auth/ConfidentialClientSharingSeamTests.cs` — NEW, 4 tests
- `tests/integration/seam/Auth/CredentialSelectionSeamTests.cs` — joined the serialising collection
- `tests/integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs` — **task-010 regression fix**
- `projects/.../notes/decisions/011-adr009-token-cache-decision.md` — NEW

### ⚠️ Regression found and FIXED this session (task 010's, not 011's)

Task 010's fail-fast validation in `DataverseWebApiClient` broke **13 ExternalAccess contract tests**.
`StubDataverseWebApiClient` passed `Dataverse:ClientId/:ClientSecret/:TenantId` — keys the class never
reads (it reads `API_APP_ID` / `API_CLIENT_SECRET` / `TENANT_ID`) — and worked only via the silent
`DefaultAzureCredential` fallback that task 010 deliberately replaced. Fixed by setting
`Graph:ManagedIdentity:Enabled=true` on the stub. **240/240 ExternalAccess now green.**

**Process lesson**: task 010 verified with the seam tests only, not the full suite. Any task that
changes a constructor from silent-fallback to fail-fast MUST run the full suite.

### Key decisions (task 011)

1. **`SpaarkeCore.cs:39` registration deliberately NOT changed.** It is `AddHttpClient<T>` — promoting a
   typed HttpClient to singleton pins one `HttpMessageHandler` for process lifetime, defeating handler
   rotation/DNS refresh. Sharing is achieved by the static client cache instead (the `DataverseUserClient`
   pattern, which is also a transient typed HttpClient).
2. **App-only credential needed no change** — `Program.cs:46` already registers `TokenCredential` singleton.
   The hazard was `_cca` only.
3. **ADR-009: application-level caches stay authoritative.** NOT wiring `AddDistributedTokenCache` — a
   serialized MSAL cache carries refresh-token material, a security-posture change that must not ride along
   with a DI-lifetime fix. `GraphClientFactory` + `AgentTokenService` already Redis-cache their OBO results.
   Consequence recorded: `DataverseAccessDataSource` / `DataverseUserClient` cache in-process only.
   Revisit trigger + preferred remedy (app-level cache, NOT distributing MSAL's) in the decision record.
4. **Sharing asserted by count-delta, not reflection** (ADR-038 B8) — N instances → 1 client.
   Structural "no bypassing call site" guard **booked onto task 060** (2nd such obligation).

### Owner directives (2026-08-20)

- Autonomous + **parallel task agents where safe** — first real opportunity is Group C (021/023/024) after 020
- CI issues known, handled separately — **the god-class one is already fixed on master**
- PR #801 — ✅ fixed as recommended and closed
- `dataverse-access-unification-r1` — **inactive / not scheduled**; interlock cleared everywhere

### Next tasks

**020** (`IClientAssertionProvider` seam, opus/xhigh) → then **030 pulled forward** → then Group C in parallel.
Task 020 must raise `ADR010_DITests.cs:164` ceiling **153 → 154** in the same PR.
