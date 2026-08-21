# Current Task State — spaarke-auth-v4-dataverse-MI

> **Last Updated**: 2026-08-21 (task 020, quality gates in flight)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | `spaarke-auth-v4-dataverse-MI` — eliminate `BFF-API-ClientSecret`; OBO → MI-FIC |
| **Branch** | `work/spaarke-auth-v4-dataverse-MI` · worktree `c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI` |
| **Task** | **020 — `IClientAssertionProvider` seam** — implementation + verification DONE; Step 9.5 gates running |
| **Status** | in-progress |
| **Next Action** | Apply gate findings → mark 020 ✅ → **task 030 (PULLED FORWARD, run before 021/022)** |
| **Progress** | 5 of 26 complete (001, 002, 003, 010, 011) · 020 closing · 3 deferred |

### Verification (all green)

| Check | Value |
|---|---|
| Full BFF suite | **10,553 / 0** (97 skipped) — NFR-04: all 46 fixtures unchanged |
| Seam tests | **17 / 17** (4 new for the provider) |
| ArchTests | **36 / 36** |
| Publish | **43.68 MB** compressed incl. PDBs — **zero delta**; ceiling 60 |
| CVE | clean |
| `Spaarke.Dataverse.csproj` | **zero diff** — FR-14 layer rule intact |

### Files modified (task 020, uncommitted)

- `src/server/shared/Spaarke.Dataverse/IClientAssertionProvider.cs` — NEW, the contract
- `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/ManagedIdentityAssertionProvider.cs` — NEW, MI-FIC impl
- `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/ManagedIdentityCredentialFactory.cs` — extracted `ResolveUamiClientId`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs` — singleton registration + inbound/outbound doc split
- `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` — nullable `assertion` param (accepted, unused until 022)
- `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` — `Microsoft.Identity.Web.Certificateless` 4.14.2
- `tests/Spaarke.ArchTests/ADR010_DITests.cs` — **comment only**, ceiling NOT raised (see below)
- `tests/integration/seam/Auth/ClientAssertionProviderSeamTests.cs` — NEW, 4 tests
- `projects/.../tasks/061-credential-census-test.poml` — blind-spot criteria booked

### ⚠️ ESCALATION — prescriptive-step deviation, awaiting owner acknowledgement

**Task 020 step 6 said: raise `ADR010_DITests` ceiling 153 → 154, "without it the build fails."
The premise is FALSE and the ceiling was NOT raised.**

Verified empirically, not assumed:
- ArchTests **pass at 153** (unraised)
- Actual 1:1 interface count is **151** — the ceiling already had 2 slack
- `IClientAssertionProvider` appears **0 times** in the counted list

Cause: the test scans `typeof(Program).Assembly` — the BFF only — and the interface is declared in
`Spaarke.Dataverse`. **A cross-assembly 1:1 seam is structurally invisible to this ratchet.** Raising
it would have granted headroom for a future *in-assembly* interface to land unreviewed.

**Two follow-ups:**
1. **Blind spot** — booked onto task **061** as an acceptance criterion + negative control (a scratch
   confidential-client site in `Spaarke.Dataverse`; an assembly-scoped detector passes only by accident).
2. **OWNER DECISION OPEN** — ceiling is 153 against a real count of 151. Tightening to 151 re-arms the
   ratchet properly but could redden CI for other in-flight projects. Not done; out of scope here.

### Key decisions (task 020)

1. **Contract in `Spaarke.Dataverse`, impl in BFF** — dependency inversion is the ONLY legal seam
   (base layer; `Spaarke.Core` placement is circular, CI-blocked by FR-14).
2. **Contract exposes no MSAL types** — `Task<string> GetAssertionAsync(CancellationToken)`. Keeps a
   future Key Vault certificate implementation possible without a contract change.
3. **Registered in `AuthorizationModule`**, not `SpaarkeCore` and NOT inline in `Program.cs` (whose
   `TokenCredential` registration the task explicitly names as the anti-pattern). Rationale: the
   provider serves Graph + Dataverse + Agent alike, so it is not a Dataverse concern.
4. **Construction is network-free by design** — failure surfaces at first call as a catchable
   `MsalServiceException`. This is what makes task 021's ordered fallback possible; a constructor that
   probed IMDS would fail BFF startup on every workstation.
5. **`ResolveUamiClientId` extracted** from `ManagedIdentityCredentialFactory` so the provider and every
   app-only consumer read the same setting through one code path (ADR-028 A4 line 208).
6. **Acceptance criterion "resolving twice returns the same singleton" NOT asserted** — ADR-038 ban B3.
   Second time this project's authored criteria specified a banned shape (task 011 was the first).

### Carried-forward obligations

| Onto | Obligation |
|---|---|
| **030** | ⏩ **PULLED FORWARD — run NEXT**, before 021/022 (provisioning Wave G-3 soft-blocked) |
| **021** | Ordered fallback; catch `MsalServiceException` + branch on `ErrorCode` |
| **022** | Collapse the THREE per-class CCA caches onto the provider — task 011's A4 exception EXPIRES here; also migrate `ConfidentialClientSharingSeamTests` when the diagnostics move |
| **060** | `_cca`-decoupling source guard (010) + no-call-site-bypasses-the-cache guard (011) |
| **061** | Census must scan ALL server assemblies, not just the BFF (020's blind-spot finding) |
| **024** | Workstation user-secret `API_CLIENT_SECRET` is STALE → `AADSTS7000215` |
| **031/041** | `az account get-access-token --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"` yields a real delegated user token |
| **090** | Power BI criterion 10 waived with reason |

### Owner directives (standing)

Autonomous + parallel agents where safe · CI issues handled separately (god-class one already fixed on
master) · PR #801 fixed + closed · `dataverse-access-unification-r1` INACTIVE, interlock cleared.
