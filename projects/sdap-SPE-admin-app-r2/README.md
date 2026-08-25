# SDAP SPE Admin App — R2 (make it work, on current SPE platform)

> **Status**: DESIGN (re-scoped 2026-08-20) · execution not started, operator-gated
> **Lineage**: follow-on to [`sdap-SPE-admin-app-r1`](../sdap-SPE-admin-app-r1/) (the original build — 75 tasks, closed in one day, Mar 2026)
> **Origin**: code-quality-and-assurance-r3 follow-on (RED-1), **re-scoped** after live diagnosis + platform research
> **Epic**: Code Quality (#427) · **Surface**: BFF (`Sprk.Bff.Api`) + `src/solutions/SpeAdminApp/`

## One-liner

The SPE Admin app gives Spaarke admins a UI for SharePoint Embedded, which Microsoft otherwise exposes only
through Postman, PowerShell, and CLI. **It has never been fully functional.** R2 makes it work — on the
current SPE platform — and then pays down the structural debt the code review flagged.

## What changed on 2026-08-20

R2 was originally scoped as a *behavior-preserving decomposition* of the 4,911-LOC
`Infrastructure/Graph/SpeAdminGraphService.cs`. **That framing is withdrawn.** Three findings:

1. **A live walkthrough** (Spaarke Dev tenant) confirmed four of nine screens fail outright and one fails
   silently. A refactor that preserves behavior would have preserved the bugs.
2. **The refactor's stated safety net doesn't exist.** 359 SpeAdmin tests, none of which make an HTTP call
   or stand up a host. The Graph interaction — the substance of the app — has zero automated coverage. One
   test exists *"only to make the manual test plan visible in the test runner."*
3. **The platform moved.** `containerTypes` **does not support application permissions at all** — the app's
   app-only auth makes that screen architecturally impossible. Meanwhile Microsoft shipped an SPE admin
   experience in the SharePoint admin center (GA July 2026) that overlaps the same screens.

**None of the observed defects are caused by file size, and none are fixed by splitting the file.** The
decomposition is real debt — it moves to the end, where a harness exists to make it safe.

## Quick links

- **[spec.md](spec.md) — AI implementation spec (2026-08-21). 31 FRs, completed §6.5 ADR Tensions block, owner clarifications. This is what `/project-pipeline` consumes.**
- [design.md](design.md) — verified current state, root causes, six workstreams, open decisions, acceptance
- [notes/spe-platform-research-2026-08-20.md](notes/spe-platform-research-2026-08-20.md) — SPE platform state as of today
- [notes/RED-1-investigation-research.md](notes/RED-1-investigation-research.md) — original RED-1 seed (superseded framing; retained for lineage)

## Workstreams

| | Workstream | Why |
|---|---|---|
| **A** | Make failures visible | The app reports success when it isn't succeeding. Gates everything else. |
| **B** | Resolve the auth model | App-only → hybrid delegated. The architectural decision of the project. 🔔 **Gated on an explicit ADR-028 / ADR-008 §6.5 conflict check** (design.md §5.1) — path A/B/C named by a human before any implementation task starts. Not advisory. |
| **C** | Correct the API surface | `/beta` → v1.0; three wrong property names; quota-vs-consumption split. |
| **D** | Build the harness | WireMock mapping tests + `[Category("LiveIntegration")]`. R1 recommended this and never did it. |
| **E** | New capabilities | Container archival (**up to 75% storage cost reduction**); real per-container quota; per-container item recycle bin. *(Information barriers removed from scope 2026-08-21 — owner decision.)* |
| **F** | Decomposition | Last, not first — protected by D, along seams revealed by B–C. |

## Decisions

| # | Decision | Status |
|---|---|---|
| D1 | Container Types screens | ✅ **Rebuild** on delegated auth. D2 pays for the delegated path anyway; and listing is ownership-filtered (not admin-gated) while Graph create needs **no admin role** — much cheaper than first assumed. Billing-profile attach deep-links out. |
| D2 | Auth model | ✅ **Hybrid** — delegated where required, app-only where supported. ADR-028 / ADR-008 check required. |
| D3 | Recycle bin | ✅ **Both** — deleted containers (one-line fix) + per-container item recycle bin (the likelier intent). |
| D4 | New capabilities | ✅ **Archival + quota + item recycle bin + owner management.** ~~Information barriers~~ **removed from scope 2026-08-21 (owner decision — ethical walls / conflict-of-interest not needed)**. **Legal hold / retention / eDiscovery excluded — Purview's surface, not ours.** |
| D5 | **Does Workstream F (splitting the 4,911-line file) ship inside R2, or as its own follow-on project?** | ✅ **Split out** → follow-on `speadmingraphservice-decomposition-r1`, entry-gated on A–E merged + the D harness green. A–E is already a full project; F is a large rewrite in a contended hot path; and F is *better work after* A–E, when the seams are known rather than inferred. Two cheap hygiene moves (dead stub, misfiled endpoints file) still ship in R2. |

**Out of scope, with a home:** billing-profile attach (`Add-SPOContainerTypeBilling`) is PowerShell, needs
Azure subscription owner/contributor, and is a once-per-customer provisioning act — it belongs to
[`customer-provisioning-orchestration-r1`](../customer-provisioning-orchestration-r1/), not here. SPE Admin
**reads** `billingStatus` and warns when billing is invalid. Legal hold / retention / eDiscovery belong to
Microsoft Purview; SPE Admin exposes the container URL and deep-links out.

## Prerequisite — satisfied

✅ **`spaarkedev1`** ("Spaarke Dev" / config "Spaarke PAYGO 1") and its container type + containers are
available for Workstreams B and D. Live-tier testing is unblocked.

⚠️ **Destructive tests must use a dedicated throwaway container.** The existing containers hold real working
documents (signed NDAs, Compose drafts, matter files). Delete / permanent-delete / recycle-bin-purge paths
provision and tear down their own container; read-only and additive tests may use the existing ones.

## Graduation criteria

- [ ] All nine screens work against the Spaarke Dev tenant, or are deliberately removed with rationale recorded
- [ ] No screen reports success while returning no data; every failure surfaces the real underlying error
- [ ] GA'd surfaces call v1.0; property names verified against current schema
- [ ] WireMock + LiveIntegration harness exists; a wrong property name fails CI
- [ ] `knowledge/sharepoint-embedded/` refreshed (2026-05-14 → current)
- [ ] Build 0/0 under the analyzer gate; publish size neutral; no new NuGet; no new HIGH CVE
- [ ] `/conflict-check` clean before each PR
