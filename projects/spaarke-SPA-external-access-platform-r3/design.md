# Design — Spaarke External Access Platform R3 (DRAFT for review)

> **Status**: 🟡 DRAFT — scope collection for owner review. Not yet a spec; do not start execution.
> **Created**: 2026-08-12 · **Author**: (external-access-r2 follow-on)
> **Predecessors**: `spaarke-SPA-external-access-platform-r1` (shipped) · `-r2` (shipped — module-host SPA
> platform, dual-plane auth, access-write/read model, Tier-1 entitlement, task-073 UAT)

---

## 1. Context

R2 delivered the **module-host external SPA** (card launcher + dual identity-plane auth + Teams-embed),
the **external access model** (polymorphic grant write/read across Project/Matter/Work Assignment,
organization grants, standing grants, Tier-1 module entitlement), and the **read path** (granted records
+ document/invoice rollup) — all verified in task-073 live UAT.

R2 UAT confirmed the access model works end-to-end, but surfaced **interactive gaps** that were, by
design, deferred read-only "graceful degrades" or P3 (Legal Front Door intake) scope. R3 closes those so
the portal is a working destination, not just a read surface. **This is critical functionality** (owner,
2026-08-12).

## 2. Problem statement (from R2 live UAT, 2026-08-12)

| # | Observed | Root cause |
|---|---|---|
| 3E | Clicking a record in the SPA grids does nothing | SPA is intentionally **Xrm-free/read-only**; grid row-open defaults to `Xrm.Navigation.navigateTo`, which no-ops without `Xrm`. No SPA record-detail surface exists. |
| 4B | Quick-start cards (NDA Assessment, Invention Submission, Submit Policy Question, Trademark Search) don't open wizards | The **Legal Front Door typed-intake capability is not built** (P3). The cards are previews; the launch handler only opens a widget when one is wired. |
| 6A | An already-onboarded external contact added to a **subsequent** record gets **no** notification | `invite-and-grant` emails only on **first** CIAM onboarding; a subsequent `/grant` sends nothing. |
| Grids | Column sets in the CIAM data grids need refinement | Grid `sprk_gridconfiguration` column config is placeholder-level for several tabs. |
| Teams | Same quick-start + grid behavior in the Teams-embedded app | Teams embeds the same external-spa; fixes apply to both surfaces. |

**Working as designed (NOT R3 scope):** "Ask Legal" assistant (FR-26 preview — future release); e-signature
for NDA (deferred). 5A first-assignment CIAM invite email works (R2). 5B Partner-primary login shipped (R2).

## 3. Goals / Non-goals

**Goals**
- G1. Partners can **open a record** from any SPA/Teams grid into a read-only detail surface (fields +
  related documents/invoices **+ messages + events/tasks**), respecting the same Tier-2 per-record
  authorization as the list. *(Owner decision 2026-08-12: detail includes messages and events/tasks.)*
- G2. The **Legal Front Door** typed-intake wizards create `sprk_servicerequest` records and are launchable
  from the quick-start cards + "More Services". **Confirmed R3 set (owner, 2026-08-12): all four — NDA
  Assessment, Invention Submission, Policy Question, Trademark Search.**
- G3. An external contact added to a **new** record receives a **notification** — **email (portal
  deep-link) AND in-portal notification** (owner decision 2026-08-12) — parity with first-assignment
  onboarding.
- G4. Refined, owner-approved **grid columns** across the CIAM data tabs.
- G5. Full **Teams-embedded parity** for G1–G4.

**Non-goals (this release)**
- Ask Legal assistant (FR-26 preview). E-signature. In-portal record **editing** (partners stay read-only;
  intake creates service requests, it does not edit core records). New identity planes.

## 4. In-scope capabilities + reuse map (§11 — extend donors, don't fork)

### C1 — Record-open detail surface (G1 / 3E)
- **Reuse**: `SprkModal` + `RecordNavigationModalShell` (the "1 of N" browse shell) from
  `@spaarke/ui-components`; `RecordHeader`/field components for read-only field display; the **existing R2
  read path (task 028)** that already powers the tab lists + doc/invoice rollup (no new server read for
  fields/docs/invoices); the host's existing **`EventsCalendar` + `SmartTodo`** external-plane read paths
  supply the **events/tasks** section (already Xrm-free — verified in `src/client/external-spa/src/`).
- **New**: a read-only "record detail" view wired to the grid framework's `rowOpen` for the Xrm-free SPA
  host (grid config already supports a `rowOpen` type — see the Service Request config's
  `"rowOpen":{"type":"formDialog"}`). **Messages section is NET-NEW to the external host** — no
  Communication component exists in `external-spa` today; `CommunicationsWorkspaceWidget` (shared lib) is
  internal-plane, so this requires a **new external-plane message read path with its own Tier-2 check**.
  This is the largest single new surface in R3 — the spec must estimate it explicitly, not fold it into
  "reuse."

### C2 — Legal Front Door typed-intake wizards (G2 / 4B)
- **Reuse**: the `Wizard` / `WizardModal` preset / `WizardRegistry` framework; `CreateRecordWizard` as the
  structural pattern; `FieldMappingService`; the `sprk_servicerequest` entity (stub exists); the
  QuickStartPane `launch → onOpenWidget` wiring + the surface-launch registry pattern.
- **New**: one typed-intake wizard per request type (schema-driven where possible) that writes a
  `sprk_servicerequest`; registry entries; wire each quick-start action's target. **NOT code pages** —
  these are shared React `WizardModal` components mounted in the SPA (same as the Create* wizards).

### C3 — Subsequent-assignment notification (G3 / 6A) — R3 (owner confirmed 2026-08-12, not an R2 hotfix)
- **Reuse**: the R2 external-access grant write path + the existing CIAM invite/notify plumbing
  (`invite-and-grant`), the notification/email sender the BFF already uses (email half).
- **New (email)**: on a `/grant` to an already-onboarded contact, send a "you've been given access to
  {record}" email with a portal deep-link. §10 BFF hygiene: Placement Justification + `<hot-path-declaration>`
  + publish-size verification + tests (C3 is the only BFF-touching capability — these §10 blocks are
  mandatory, not optional).
- **New (in-portal)**: owner chose email **+ in-portal**. The external host has **no notification surface
  today** (no inbox/banner) and `Spaarke.Notifications` is server/internal-plane. In-portal therefore =
  a new external-SPA UI surface **plus** a new external-plane read endpoint. Net-new; estimate separately.
- **⚠️ Build prereq**: C3 builds/deploys `Sprk.Bff.Api`. This worktree MUST be net10-ready first — see
  **§4.5 Build-environment prerequisite** below. A net8 BFF deploy to the net10 dev runtime = 503.

### C4 — Grid column refinement (G4)
- **Reuse**: DataGrid framework `sprk_gridconfiguration` config records (data-only; no code).
- **New**: owner-approved column sets per CIAM tab (needs the column spec from the owner).

### C5 — Teams parity (G5) — verify C1–C4 render + launch correctly in the Teams-embedded host.

## 4.5 Build-environment prerequisite — .NET 10 (BINDING for any BFF build/deploy)

> **As of 2026-08-14, `master` and the dev App Service runtime are .NET 10.** A net8 BFF deploy to the
> net10 runtime **503s on startup**. This worktree is currently **~164 commits behind master** and has
> **not** merged the net8→net10 retarget.

**Scope of impact:**
- **C3 only** touches the BFF (`Sprk.Bff.Api`) → C3 is gated on net10-readiness.
- **C1 / C2 / C4 / C5** are client-only (`external-spa` Vite; `npm run build`, never `dotnet`) →
  **unaffected**; safe to build on the current tree.

**Gate (run before the first C3 BFF build/deploy, NOT before):**
1. `dotnet --list-sdks` → confirm a `10.0.1xx` entry (machine has `10.0.101`; restart stale shells if not visible).
2. Commit/stash local work, then `git fetch origin && git merge origin/master`; resolve `.csproj` TFMs,
   `global.json`, `Directory.Packages.props`/`Directory.Build.props` **toward master's net10 versions**.
3. `dotnet build -c Release src/server/api/Sprk.Bff.Api/` must be clean (Graph/Kiota call sites:
   see `projects/dotnet-10-upgrade-r1/notes/graph6-kiota2-break-assessment.md`).
4. Only then deploy. Verify: `curl https://spaarke-bff-dev.azurewebsites.net/healthz` → 200.

**Do NOT** deploy the BFF from a net8 tree. The spec's C3 task MUST carry this as a hard prerequisite so
execution does not inadvertently regress dev. (Client work may proceed on the current tree meanwhile.)

## 5. ADR touchpoints (anticipated)
- ADR-028 (auth planes — unchanged; reuse), ADR-024 (polymorphic regarding — service requests),
  ADR-050 / MODAL-DECISION-CRITERIA (record-detail modal), ADR-009 (cache/invalidate for notify),
  ADR-007 (SPE facade for any doc access in detail view), §10 BFF hygiene (C3 notification).

## 6. Open questions for the owner (review these)
1. ~~**Front Door request types**~~ — ✅ **RESOLVED (2026-08-12): all four** (NDA Assessment, Invention
   Submission, Policy Question, Trademark Search) are R3.
2. ~~**Record-detail depth**~~ — ✅ **RESOLVED (2026-08-12): read-only fields + related docs/invoices +
   messages + events/tasks.** No editing actions. (Messages = net-new external-plane read path, see C1.)
3. ~~**Notification channel (6A)**~~ — ✅ **RESOLVED (2026-08-12): email (portal deep-link) + in-portal.**
   In-portal surface is net-new to the external host (see C3). *Still needed: exact deep-link target +
   email/in-portal copy.*
4. **Grid columns (C4)** — ⬜ **STILL OPEN.** Provide the desired column set per tab (Projects / Matters /
   Work Assignments / Documents / Invoices / Service Requests). Blocks C4 estimation.
5. **Scope boundary** — 6A: ✅ **R3** (owner, 2026-08-12). C4 timing: ⬜ **still open** — R3 or an
   immediate config-only change ahead of R3?

## 7. Draft success criteria
- A partner opens a granted Matter/Project/WA/Document/Invoice into a read-only detail surface (Tier-2
  enforced) in both SPA and Teams.
- Each quick-start card opens its intake wizard; submitting creates a `sprk_servicerequest` visible in the
  Service Requests tab.
- Adding an onboarded contact to a new record sends them a portal-linked notification.
- Grid columns match the owner-approved spec.

---

*Next step after review: convert this to `spec.md` via `/design-to-spec`, then `/project-pipeline`
(INITIALIZE-ONLY) to generate plan + tasks. Reuse donors above are binding per §11.*
