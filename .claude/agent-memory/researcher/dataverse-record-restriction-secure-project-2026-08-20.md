---
name: dataverse-record-restriction-secure-project-2026-08-20
description: Per-record RESTRICT/DENY landscape for Secure Projects — no deny exists (GA or preview); matrix data access (modernized BUs, GA) makes BU-parking cheap (owningbusinessunit settable, users never move); filtered-view predicate RLS is a blog-only watch item; single-secure-BU + owner-team-per-project alternative.
metadata:
  type: project
---

# Dataverse per-record restriction / Secure Project — 2026-08-20

**Question**: For unified-access-control-r2 Secure Projects (record visible ONLY to named users/contacts, overriding standing role grants): does any per-record DENY exist, what's the canonical confidential-records pattern, is BU-per-project right, do impersonated reads honour it?

**Findings**:
- **NO record-level deny exists, GA or preview, as of 2026-08-20.** Checked 2025w2 + 2026w1 release plans — nothing. Model is strictly additive (wp-security-cds, updated 2026-08-14: "you can't go back and hide a single record"; "Security is always additive"). Column-level security is the only restrictive primitive and is column-granular only. See [[dataverse-record-access-security-2026-07-16]] for the union model + POA APIs.
- **Watch item**: Power Platform blog 2025-08-07 describes a **filtered-view predicate RLS model** (role + column-value predicates → CRUD only on matching rows), live in D365 F&O + Power Pages, "expanding to other workloads" — but NO Learn doc, NO PPAC switch, NO release-plan entry for custom Dataverse tables yet. Not designable-against.
- **Matrix data access / modernized BUs is GA** (PPAC switch "Record ownership across business units" = `EnableOwnershipAcrossBusinessUnits`; companions `AlwaysMoveRecordToOwnerBusinessUnit=false`, `RecomputeOwnershipAcrossBusinessUnits`). `owningbusinessunit` becomes a settable column decoupled from owner's BU; users get roles FROM any BU without moving; owner can sit anywhere with just some Read-granting role; setting the column needs Append-To(Local) on businessunit. This removes the classic costs of BU-parking (no user moves, no ownership churn).
- **BU-per-secure-project verdict**: sound but likely over-engineered. Microsoft's only guidance is directional anti-proliferation ("Minimize the number of business units" — POA doc; BUs are boundaries not entities; role instances replicate per BU, cost unquantified, no documented BU cap). Flatter alternative with the same exclusion guarantee: **ONE Secure-Projects BU + owner team per project** (team owns records; team role at Basic depth with team-privilege inheritance; membership = grant seam; POA-light per the mitigation list). Prerequisite either way: **audit that NO standing role grants Org-depth Read** on secured tables — one Org-read hole defeats everything.
- **Impersonation honours all mechanisms** (roles/BU depth/teams/POA/hierarchy) — but effective = **INTERSECTION of app user × impersonated user**, so keep the app user Org-scoped or brokered reads come back narrower than the user's real access. Contacts are not systemusers → cannot be impersonated; external story must go through the external-access systemuser.

**Sources**: learn wp-security-cds (2025-06-03/upd 2026-08-14) · modernized-business-units-security (2025-03-13/upd 2026-08-14) · manage-principalobjectaccess-storage (2023-09-20) · release-plan/2026wave1/data-platform + 2025wave2 · microsoft.com/power-platform/blog 2025-08-07 data-protection-in-dataverse. Full write-up: `projects/unified-access-control-r2/notes/investigation/09-dataverse-record-security.md`.

**Open questions**: When does filtered-view RLS reach custom tables (re-check release waves ~2026w2)? Does Spaarke pick BU-per-project (matrix ergonomics) or single-BU+owner-team? Quantified perf cost of role-replication across many BUs remains undocumented.
