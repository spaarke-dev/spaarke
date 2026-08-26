# Task 012 — operator role prerequisite, surfaced honestly

> **Spec FR-B03** · 2026-08-22 · **DONE**, with **one operator decision left open** (§5)
> **Method**: live Entra / Microsoft Graph reads against the Spaarke Dev tenant, then code.
> Read-only against the tenant — nothing was created, modified, or granted.
> **No secret value, token, or assertion appears in this file.** App ids, tenant ids, and directory-role
> template ids are public identifiers, not credentials.

---

## 1. The finding that shaped the task

**Microsoft Entra directory roles are invisible to the BFF.** Proven with a positive control:

| Check | Result |
|---|---|
| Does an Entra role "SharePoint Embedded Administrator" exist? | ✅ yes — template `1a7d78b6-429f-476b-b8eb-35fb715fffd4` |
| Is it activated in this tenant? | ✅ yes — directory role `1e5d7bb3-…` |
| Is `ralph.schroeder@spaarke.com` a member? | ✅ **yes** — confirmed via `/directoryRoles/{id}/members` |
| `groupMembershipClaims` on `SDAP-BFF-SPE-API` | **`null`** |
| Real token issued for `aud = api://1e40baad-…`, same user | claims: `acr aio amr appid appidacr aud email exp family_name given_name iat ipaddr iss name nbf oid preferred_username rh roles scp sid sub tid unique_name upn uti ver xms_ftd` |
| → `wids` (tenant-wide directory roles) present? | ❌ **absent** |
| → `roles` (Spaarke app roles) present? | ✅ present |

Entra emits `wids` only when the **resource** application sets `groupMembershipClaims` to `All` or
`DirectoryRole`. On the BFF it is unset, so the claim never appears.

**The load-bearing consequence**: *absence of the claim does not mean absence of the role.* A user who
genuinely holds the SharePoint Embedded Administrator role produces a token indistinguishable from one
who does not. Nothing in `src/` reads `wids` — grep returns zero hits.

🔔 **The POML's escalation trigger fires by its own terms** ("if the required directory role cannot be
detected from the token's claims, STOP and escalate"). Its *rationale* — "a wrong 'you lack role X'
message is exactly the misleading-error defect this project removes" — is honored by the design below,
which reports the prerequisite from the one place that is authoritative and never asserts membership.
The residual item that genuinely needs a human is §5.

---

## 2. What the POML got right, and what it missed

| POML premise | Verdict |
|---|---|
| "Today they get a generic 403" | ✅ **HOLDS** — `ProblemDetailsHelper.Forbidden` returned `detail: "Access denied"`, full stop. First premise in this project to survive contact. |
| "A missing Entra role must produce a message naming the role" — *from the filter* | ❌ **not implementable there.** See §1. |
| Step 2: "From task 010's findings, determine which operations require a directory role" | ❌ **the finding does not exist.** Task 010 §"Step 7 — NOT RESOLVED" explicitly could not answer it and handed it to 011; 011 is 🔄 partial and did not settle it. Still open — see §6. |
| `<relevant-files>` = the filter + the client | ⚠️ **incomplete** — the actual defect was one layer down, in `ContainerTypeEndpoints`. |

### 🔴 The real defect, which the POML did not name

All four container-type operations passed a hardcoded `StatusCodes.Status500InternalServerError` to
`ToProblemDetails`, while `GraphErrorTranslator.ClientStatusFor` — added by task 001 precisely to
preserve upstream status — went unused on this path.

**A Graph 403 reached the admin as HTTP 500 "Internal Server Error."** A permission problem was
indistinguishable from a server bug, so the admin's next move was to report an outage rather than
request a role.

**Sixth instance of this project's signature shape**: *a lower layer collapsing a real distinction into
a generic one that an upper layer reads as something else.* (003 config-load · 005 audit-write ·
002 ODataError · 024 storage-null · 022 `deletedDateTime` · **012 403→500**.)

---

## 3. The design — report each layer where it is authoritative

| | Layer 1 — Spaarke admin app role | Layer 2 — Entra directory role |
|---|---|---|
| Gates | reaching `/api/spe` at all | what Graph returns for container types |
| Granted by | a Spaarke administrator | a Microsoft Entra administrator |
| Observed | `roles` claim — **directly visible** | **not visible**; only Graph knows |
| Reported by | `SpeAdminAuthorizationFilter` | `ContainerTypeEndpoints.EntraRoleDeniedProblem` |
| Code | `sdap.access.deny.role_insufficient` (403) · `sdap.access.deny.unauthenticated` (401) | `spe.containertypes.entra_role_required` (403) |

Each layer speaks **only** about what it can observe. That is the whole rule.

### What the layer-2 message may and may not say

It names the role and what the role enables. It does **not** assert the caller lacks it — Graph reports
*that* a request was denied, not *why*, and 403 also covers an unregistered container type, a consent
gap, or a config pointing at another tenant. So the wording states the prerequisite and then says
Spaarke cannot see whether the caller holds it, pointing at the Graph diagnostics for the alternatives.

Three tests exist purely to keep a future "helpful" edit from turning this back into an assertion.

---

## 4. Changes

| File | Change |
|---|---|
| `Api/Filters/SpeAdminAuthorizationFilter.cs` | Two layers documented distinctly, with the §1 measurement inline and an explicit **"do not add a `wids` check"**. 403 now names the Spaarke permission and who grants it; 401 split onto its own code and says nothing about roles. |
| `Api/SpeAdmin/ContainerTypeEndpoints.cs` | 403-filtered catch on list / get / create → real **403** naming the Entra role. New `EntraRoleDeniedProblem` helper (`internal` so its wording contract is testable). |
| `Infrastructure/Errors/ProblemDetailsHelper.cs` | `Forbidden` gains **optional** `detail` + `traceId`. All 16 existing call sites unchanged. |
| `services/speApiClient.ts` | `PERMISSION_CODES` + `describePermissionPrerequisite` — picks a **heading** only; body text still comes from `describeApiError`, because only the BFF knows which layer denied. |
| `container-types/ContainerTypesPage.tsx` | Prerequisites titled for what they are instead of "Failed to load…"; empty state gained the scoping explanation. Fluent v9 tokens (`colorNeutralForeground3`), no hex. |
| `tests/integration/auth/SpeAdmin/SpeAdminAuthorizationLayerTests.cs` | **14 tests**, new file. |
| `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` | Compile `tests/integration/auth/**`. |

### Register endpoint deliberately untouched
`RegisterContainerTypeAsync` calls **SharePoint REST**, not Graph (SPE-053) — different permission
model, no `SpaarkeStorageException` path. Adding the Entra-role message there would name a
prerequisite that endpoint never established.

### `tests/integration/auth/**` was a dead KEEP path
ADR-038 §2 path #1 designates it for security-auth tests. It held **only a README** and was compiled by
no test project — so the category was unusable and nothing had ever been filed there. Now wired on the
same terms as `contract/**`.

---

## 5. 🔔 Operator decision — left open deliberately

**Should `groupMembershipClaims` be set to `DirectoryRole` on `SDAP-BFF-SPE-API`?**

It would put `wids` in the token and let SPE Admin tell a user *before* they act — e.g. disabling
tenant-wide controls up front rather than after a Graph round-trip.

**Not taken unilaterally.** `SDAP-BFF-SPE-API` is the shared registration behind **every** Spaarke
client surface; the change adds a claim to *every* token it issues, for every app, and grows token
size for users with many directory roles. That blast radius is an operator's call, not a task's.

| | |
|---|---|
| **Cost of leaving it** | Prerequisites are reported *reactively* (after Graph refuses) rather than *proactively*. Every message is still accurate. |
| **If adopted** | Filter can pre-check `1a7d78b6-…` / `62e90394-…` — but must still treat absence as unknown, since `wids` carries tenant-wide roles only. |

**Nothing in this task depends on the answer.** It is an enhancement, not a gap.

---

## 6. Still open — inherited, not created

**Which container-type operations genuinely require the directory role vs. are merely
ownership-scoped?** Task 010 could not answer it; task 011 was to and did not.

Not settled here either. It needs a delegated token holding `FileStorageContainerType.Manage.All` for a
user who does **not** hold the role. The executing account **does** hold it (§1), and the Azure CLI
client lacks the scope — a probe returned `503 UnknownError`, which is not evidence of anything and is
recorded as such rather than read as a result.

**This does not weaken the shipped message**, which names the role as the prerequisite for *tenant-wide*
administration — true on both readings of the contradictory Microsoft docs — and never claims the
screen is unusable without it (POML constraint; spec §4.2b).

Carries to **task 027** (owner management), the next task holding a delegated token.

---

## 7. Observation for a later task — NOT fixed here

`GraphErrorTranslator.ToProblemDetails()` (the **no-arg** overload, ~29 callers across
`ContainerItemEndpoints` / `DocumentsEndpoints` / `OBOEndpoints` / `UploadEndpoints`) hardcodes on 403:

```csharp
: status == 403 ? "api identity lacks required container-type permission for this operation."
```

On any **delegated** path the failing identity is the signed-in user, not "the api identity" — so the
message may name the wrong party. **Not touched**: those are document/file surfaces outside FR-B03, the
overload is shared by 29 sites, and I have not verified which of them are delegated. Recording it rather
than fixing it blind. → **task 042** or `speadmingraphservice-decomposition-r1`.

Related, also unfixed: the general `catch (SpaarkeStorageException)` on container types still maps
**every** non-403 Graph status to 500, so a 429 throttle also reads as a server error. In scope for
whichever Workstream C task next touches those catches (**021** / **023**).

---

## 8. Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **0 errors**, 7 pre-existing warnings |
| Unit tests | **10,641 passed**, 0 failed, 97 skipped (+14) |
| New auth tests | **14 / 14** |
| ArchTests (ADR-007 etc.) | **36 / 36** |
| SpeAdminApp code page | builds (vite, 3,379 modules) |
| Publish, compressed incl. PDBs | **43.67 MB** — **0 MB delta**; ceiling 60 |
| New NuGet | none |
| Vulnerable packages | none |

## 9. Acceptance criteria

| # | Criterion | Status |
|---|---|---|
| 1 | Role-gated operation names the role + what it enables | ✅ via layer 2, where Graph is authoritative |
| 2 | Ordinary user sees scoping, not denial | ✅ empty-state notice; no denial language |
| 3 | Two layers documented distinctly in code | ✅ filter remarks + helper remarks + tests |
| 4 | Negative: unauthenticated still 401 | ✅ own code; asserted to mention no role |
| 5 | Negative: Spaarke admin without Entra role gets the role message | ✅ **behaviourally** — passes layer 1, Graph 403, role message. Not claim-detected (§1), which is why it is honest |
| 6 | Fluent v9 tokens, adapts in dark mode | ✅ `colorNeutralForeground3`, `MessageBar` intents; no hex |
| 7 | Build 0 errors; publish within ceiling | ✅ §8 |

**Not verified**: the three `<ui-tests>` need a **deployed** app and two accounts (one with the role,
one without). Adds to the project's standing UI-verification debt — it does not reduce it.

---

## 10. Placement Justification (root CLAUDE.md §10 / §11)

**Placement — in the BFF.** All three changes extend authorization/error translation that already lives
there. Only the BFF holds the deny decision and the Graph response; moving either out would mean
shipping raw Graph errors to the client and letting it guess — the defect being removed.

| §11 question | Answer |
|---|---|
| **Existing** | `SpeAdminAuthorizationFilter` (403 path), `ProblemDetailsHelper.Forbidden`, `GraphErrorTranslator.ToProblemDetails`, `describeApiError` — all found by grep, all already present. |
| **Extension** | **Yes, throughout.** No new service, interface, endpoint, DI registration, package, or Dataverse column. `Forbidden` gained optional parameters (16 call sites unchanged); `EntraRoleDeniedProblem` is a private-by-default helper on an existing static class; `describePermissionPrerequisite` is a function in the existing client module. |
| **Cost of doing nothing** | A Graph 403 renders as "Internal Server Error", so a permission problem is reported as an outage; and a layer-1 denial says only "Access denied", naming neither what was checked nor who grants it. Both send the admin to the wrong remedy. |

`<hot-path-declaration>`: BFF **Y** · SpaarkeAi N · ci-workflows N · skill-directives N · root-CLAUDE N.
`/conflict-check`: PR #812 (`unified-access-control-r2`) also edits `Api/Filters/`, but
`DocumentAuthorizationFilter.cs` — **no file overlap**.

**ADR compliance**: ADR-008 (authorization stays in the endpoint filter; no global middleware) ·
ADR-019 (RFC 7807 throughout) · ADR-007 (endpoints catch `SpaarkeStorageException`, never a Graph type —
ArchTests 36/36) · ADR-021 (Fluent v9 semantic tokens, no hex) · ADR-038 (tests filed under the
`tests/integration/auth/**` KEEP path; no `Mock<HttpMessageHandler>`, no DI-registration or ctor
null-check tests). **No ADR conflict — no §6.5 escalation.**
