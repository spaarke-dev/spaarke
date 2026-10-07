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
- G5. **Teams parity for the WORKFORCE plane** (G1–G4). Scope narrowed 2026-10-07 (see §4.6): the Teams
  tab serves only Dataverse-licensed users (internal / B2B-guest, workforce plane). **CIAM external
  contacts are browser-only** — a CIAM account cannot sign into Teams, so there is no Teams parity to
  deliver for the external-contact plane.

**Non-goals (this release)**
- Ask Legal assistant (FR-26 preview). E-signature. In-portal record **editing** (partners stay read-only;
  intake creates service requests, it does not edit core records). New identity planes.
- **External contacts (CIAM) inside the Teams tab** — not possible / not planned (§4.6); contacts use the
  browser SPA only.
- **Model 2 (dedicated per-customer tenant) workforce auth** — out of scope for now; R3 targets **Model 1**
  (customer staff are B2B guests in Spaarke's tenant), see §4.6.

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

### C5 — Teams parity (G5) — WORKFORCE plane only (narrowed 2026-10-07, §4.6)
- **Reuse / verify**: C1–C4 render + launch correctly in the Teams-embedded host **for workforce users**.
- **New (auth-shape change, §4.6)**: the Teams tab adopts the add-ins' sign-in shape — **NAA only** (the
  Teams-SSO `getAuthToken` fallback is **dropped** for the multi-customer build, because a single manifest
  can name only one audience). A **dedicated Spaarke Teams client app** (in Spaarke's tenant) becomes the
  manifest `webApplicationInfo.id` + the NAA client, decoupled from any backend (today `1e40baad` the
  backend doubles as the client). Workforce authority = **Spaarke's tenant** (Model 1), not `/organizations`.
- **Not in scope**: external-contact Teams parity (CIAM can't sign into Teams — §4.6).

## 4.5 Build-environment prerequisite — .NET 10 (BINDING for any BFF build/deploy)

> **As of 2026-08-14, `master` and the dev App Service runtime are .NET 10.** A net8 BFF deploy to the
> net10 runtime **503s on startup**. ✅ **This worktree was merged onto net10 master on 2026-10-07**
> (global.json pins SDK 10.0.100; machine has 10.0.101) — the drift below is resolved; keep the gate as
> the standing rule for any future divergence.

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

## 4.6 Per-customer backend & auth-platform alignment (customer-provisioning-orchestration-r1)

> **CONFIRMED by owner 2026-10-07** via `customer-provisioning-orchestration-r1` (task t240). R3's single-BFF
> assumption is superseded: under owner D-13 each customer gets its own backend (`sprk-{customerId}-prod-api`,
> own Entra app + audience + CORS). The external SPA is **shared Spaarke-tenant infra** (one build, one
> origin) that selects the backend/scope **at runtime** — the auth module already supports this (pluggable
> authority + `setActiveBffTokenAcquirer` / `setActiveLoginScope`; NAA requests scope dynamically). What
> changes is **configuration + packaging + the external-contact platform**, not the auth engine.
> Source: `projects/customer-provisioning-orchestration-r1/notes/t240-plan.md`; R3 reply in
> [`notes/t240-auth-coordination-response.md`](notes/t240-auth-coordination-response.md).

### Confirmed decisions
1. **Production origin — `https://external.spaarke.com`** (one shared site `swa-spaarke-external-spa-prod`,
   `rg-spaarke-shared-prod`). Covers Teams **and** browser; every customer backend lists this **one** origin
   in CORS (provisioning task 240a). Provisioning creates the SWA + custom domain; owner adds DNS.
2. **Teams sign-in — NAA only; drop the Teams-SSO fallback.** A **dedicated Spaarke Teams client app** (one,
   Spaarke tenant) is the manifest `webApplicationInfo.id` + NAA client; provisioning H3 pre-authorizes it on
   every customer backend. NAA requests the chosen backend's scope dynamically → reaches any customer.
3. **External contacts are served by the customer's OWN backend** (not a shared one) — forced by D-13: a
   contact's shared records live in that customer's Dataverse, so only that stamp can serve them.
4. **External contacts are BROWSER-ONLY.** A CIAM (`spaarkeextid`) local account cannot sign into Teams
   (the Teams broker only yields a workforce/guest identity); forcing it would need their org admin to
   install the Spaarke app + a second sign-in, for zero added capability. → **C5 narrowed to workforce.**
5. **Workforce authority = Spaarke's tenant (Model 1).** R3's current `/organizations` multitenant shape is
   the **Model 2** path (deferred). Under Model 1, customer staff are **B2B guests in Spaarke's tenant** and
   stamps validate Spaarke-tenant tokens, so workforce sign-in (Teams **and** browser) authenticates against
   **Spaarke's tenant** — same as the Office add-in. Implemented via the existing optional `authority`
   override (`workforceAuthorityConfig({authority})` / `TeamsWorkforceAuthConfig.authority`) → **config, not
   code**.

### Two populations — do not conflate
| Population | Identity | Teams? | Backend routing |
|---|---|---|---|
| Workforce / internal (incl. **B2B-guest** licensed) | Entra, **Spaarke tenant** (Model 1) | ✅ yes | directory endpoint (group membership) |
| **CIAM external contact** (outside counsel) | `spaarkeextid` local account | ❌ browser-only | invitation / deep-link scoped (see below) |

### R3-side actions (each a live action — confirm with owner first)
- **A1.** Produce a **prod Teams appPackage** (own manifest + prod Teams app registration); dev stays `green-dune`.
- **A2.** Update the manifest origin references + SPA redirect URIs to `external.spaarke.com`; set
  `webApplicationInfo.id` to the **dedicated Spaarke Teams client app** (owner creates it; send its client id
  to provisioning). Keep the dev origin in CORS during transition.
- **A3.** Code: drop the `acquireBffTokenViaTeamsSso` fallback branch (NAA-only); set workforce `authority`
  to Spaarke's tenant (Model 1). Confirm the **supported Teams-host matrix all speak NAA** before removing
  the fallback (flag to provisioning if any host lacks it).
- **A4.** Deploy the production build to `swa-spaarke-external-spa-prod` once the origin is live.

### 240d co-design (external contacts on stamps) — R3 positions
Provisioning task 240d closes the gap that **no stamp can serve CIAM today** (no `Ciam:*` settings; the CIAM
Graph provisioner uses a Key Vault cert a keyless stamp lacks). R3 co-designs with provisioning + `unified-access-control-r2`. **This is the hard dependency for R3's external-contact capabilities (C1 messages/detail,
C2 external submission, C3 in-portal notify) on provisioned customers.** R3's positions:
- **CIAM audience → one per customer** (per-stamp CIAM app/audience in the shared `spaarkeextid` tenant), not
  one shared audience — else a contact's token for customer A could be replayed against customer B's BFF.
- **Routing → invitation / deep-link scoped, NOT the workforce directory endpoint.** CIAM contacts are not
  members of the `sprk-{customerId}-users` workforce groups the directory resolves, so that mechanism can't
  enumerate a contact's customers. A contact's customer relationship is established at **invite/grant** time —
  which is exactly R3's **C3 deep-link** ("you've been granted access to {record}" → one record on one stamp).
  So C3's deep-link should encode `{customerId, apiBaseUrl}`, and the C3 notification mechanism **converges
  with** contact→backend routing. Cold-landing (no deep-link) needs a contact→customers map in the shared
  registry (written at invite time) or an "enter invite / pick organization" prompt.
- **Provisioner → keyless federated credential** from the stamp's managed identity (not a Key Vault cert).
  Note this is **cross-tenant workload-identity federation** (stamp MI → CIAM app in `spaarkeextid`) — confirm
  support before committing.

### Sequencing implication for the spec
- **C4 (grid columns)** and the **non-auth UI of C1/C2/C5** are independent of the platform work — can proceed.
- **C1/C2/C3 for workforce users** are largely stamp-agnostic (Model-1 Spaarke-tenant authority aside).
- **C1/C2/C3 for external contacts** depend on **240d** — they cannot ship to a provisioned customer until
  stamps serve CIAM. This split is a natural wave boundary for the plan.

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
