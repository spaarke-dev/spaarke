# Version Compatibility Matrix (BFF × Solution)

> **Status**: v1 (initial publication — 2026-08-17)
> **Owner**: Spaarke Platform Operations (Release Manager)
> **Update cadence**: per release-tag milestone (living document; append rows/columns each release; do not rewrite history)
> **Consumed by**: **H0 preflight (upgrade mode)** per [design.md §14A.3](../../projects/customer-provisioning-orchestration-r1/design.md) + [spec.md FR-34](../../projects/customer-provisioning-orchestration-r1/spec.md)
> **Related**:
> - [design.md §14A.2 — Handler reuse: upgrade mode vs first-install mode](../../projects/customer-provisioning-orchestration-r1/design.md) (semantics H0 applies to each handler when this matrix returns 🟡/🔴)
> - [customer-comms/ — U-CB-1..U-CB-6 templates](./customer-comms/) (operator communication scripts referenced from remediation guidance below)
> - `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` — **TODO**: cross-link once [task 001](../../projects/customer-provisioning-orchestration-r1/tasks/001-consolidate-deploy-guides.poml) lands the authoritative deploy guide (Wave 0 parallel)

---

## 1. Purpose

This matrix maps each supported **BFF version** × **Solution-set version** pair to a compatibility verdict. It is the authoritative query surface that **H0 preflight (upgrade mode)** consults before an upgrade run proceeds against a live customer environment.

Without this matrix, an upgrade against `sprk_dataverseenvironment` with `sprk_provisionedon != null` has no way to verify that the pending BFF binary + Dataverse solution set is a supported pair — and design.md §4B silent-fail traps T1..T6 cascade (customer environment becomes unreachable, DE seed fails, MI-Dataverse-App-User missing, container-type replication stalls, etc.). Publishing the matrix is the mechanism that lets H0 fail **loudly + early** on an incompatible pair.

## 2. How H0 uses this matrix (query semantics)

**Registry inputs** (from `sprk_dataverseenvironment` row for the target customer — v3.3 extension per design.md §14A.3):

| Column | Purpose |
|---|---|
| `sprk_bffversion` | Semver-ish string identifying the BFF binary the customer's environment is currently bound to (e.g. `1.0.0-net10` for the current baseline). Populated at H9 slot-swap. |
| `sprk_solutionversion` | Aggregated Solution-set version tag (e.g. `S2026.08`) representing the shipped combination of the 8 solutions per [design.md §11.1a](../../projects/customer-provisioning-orchestration-r1/design.md). Populated at H6 completion. |
| `sprk_clientcachebusttoken` | Env-var value picked up by SPA clients on next refresh; force-bump on solution upgrades that require immediate cache invalidation. |

**Algorithm**:

```
1. LOAD sprk_bffversion + sprk_solutionversion from customer registry row
2. LOAD target release's BFF version + Solution-set version from release manifest
3. LOOK UP cell (target_bff, target_solutions) in matrix below
4. IF cell == 🔴 Red:
     → BLOCK upgrade; emit runNote → operator; require intermediate release
5. IF cell == 🟡 Yellow:
     → APPLY the manual-step guidance from §5 for the cited U-CB-N class
     → REQUIRE operator ACK before H2a/H6/H9 proceed
     → EMIT the matching customer-comms template (see §5)
6. IF cell == ✅ Green:
     → PROCEED with normal upgrade sequencing
```

**Non-goals of this matrix**:
- Not an SLA (customer-facing SLAs are separate — see design.md §14A.7)
- Not a decommission model (see D17 / `Decommission-Customer.ps1`)
- Not a zero-downtime guarantee (H9 blue-green gives minutes-of-drain per §14A.7)

## 3. Version scheme

### 3.1 BFF version (`sprk_bffversion`)

Derived from the deployed `Sprk.Bff.Api` binary. Format:

```
<major>.<minor>.<patch>[-<qualifier>]
```

- `major` bumps on breaking BFF contract changes (new required `[Required]` IOptions section, removed public API surface, breaking auth flow).
- `minor` bumps on additive endpoints or new optional config sections.
- `patch` bumps on bugfixes without contract change.
- `qualifier` (optional) marks framework or platform migrations (e.g. `-net10` for the .NET 10 baseline post `dotnet-10-upgrade-r1`).

**Determining the current version**: read the assembly info of the deployed binary OR the release-tag on `master` at H9 deploy time. Bind into `sprk_bffversion` on the customer's registry row at H9 slot-swap.

### 3.2 Solution-set version (`sprk_solutionversion`)

An **aggregate tag** representing the coordinated shipped combination of the 8 managed solutions per [design.md §11.1a](../../projects/customer-provisioning-orchestration-r1/design.md):

- SpaarkeCore (Tier 1)
- SpaarkeWebResources (Tier 2)
- CalendarSidePane, DocumentUploadWizard, EventRibbons, EventDetailSidePane, EventsPage, LegalWorkspace (all Tier 3)

Format: `S<YYYY>.<MM>[.<seq>]` — e.g. `S2026.08` is the August-2026 shipped set. Sequenced (`.1`, `.2`) if multiple sets ship in the same month. Individual solutions carry their own `<Version>` inside each `Other/Solution.xml` (Dataverse-visible); the aggregate is what H6 pins to the customer.

**Rationale for aggregate vs 8-way individual axes**: a matrix cross-producting BFF versions × 8 independent solution versions is combinatorially unmanageable and violates §14A.3's two-dimensional intent. Shipping the 8 solutions as a coordinated set is already the H6 contract; the aggregate tag is the natural representation.

### 3.3 Release manifest — where the pair for a release is recorded

Each release-tag milestone records its BFF version + Solution-set version in the release notes for that tag. The matrix below is appended to at each release. Historical rows/columns are NEVER deleted — they document what was supported at prior release points so H0 upgrade-mode can service customers still on older versions.

## 4. Matrix

**Legend** (verbatim from design.md §14A.3):

| Status | Meaning | Upgrade order |
|---|---|---|
| ✅ Green | Compatible; upgrade either first | BFF or solution can lead; doesn't matter |
| 🟡 Yellow | Compatible but MUST upgrade in specific order | E.g., "solution must upgrade first; BFF requires new column X" |
| 🔴 Red | Incompatible; do NOT deploy this pair | Blocked; requires intermediate release |

**v1 matrix** (baseline row/column only — additional rows/columns append at each release-tag milestone per §6 update cadence):

|                            | **S2026.08** *(current shipped set — 8 solutions per §11.1a)* |
|----------------------------|:-------------------------------------------------------------:|
| **BFF 1.0.0-net10** *(current baseline — post `dotnet-10-upgrade-r1`; publish ~44.96 MB per NFR-01)* | ✅ Green |

> **v1 caveat — single-cell honesty**: prior to release-tag adoption, the repo has exactly ONE shipped baseline BFF version and ONE shipped Solution-set version, so v1 is a legitimate 1×1 matrix. This is NOT a partial matrix — it is the complete matrix for the current state. The framework (row/column semantics, legend, remediation guidance below) is what H0 upgrade-mode needs in place before the FIRST upgrade wave ships to a customer; the matrix EXPANDS from here.
>
> **When you ship the next release**: add ONE new row (new BFF version) OR ONE new column (new Solution-set version) OR BOTH (if the release bumps both). Fill every cell in the new row/column against every existing column/row.

## 5. Remediation guidance (per 🟡 / 🔴 cell, mapped to U-CB-N class)

When a cell is 🟡 Yellow or 🔴 Red, the operator MUST invoke the applicable U-CB-N protocol from [design.md §14A.4](../../projects/customer-provisioning-orchestration-r1/design.md) BEFORE the upgrade proceeds. Each U-CB class has an associated customer-communication template in [`customer-comms/`](./customer-comms/) (authored by [task 007](../../projects/customer-provisioning-orchestration-r1/tasks/007-author-ucb-customer-comms-templates.poml) — Wave 0 parallel; template file may not yet exist at v1-of-this-matrix publication).

| U-CB class | Trigger (what upgrade change causes this) | Verdict typically | Remediation (operator flow) | Customer-comms template |
|---|---|---|---|---|
| **U-CB-1** — Column removal or type change | Solution upgrade drops or breaking-type-changes a `sprk_*` column | 🟡 Yellow (with `--allow-destructive`) OR 🔴 Red (without) | (1) Take pre-migration data export; (2) obtain customer signoff; (3) `Deploy-DataverseSolutions.ps1 --allow-destructive`; (4) publish reversal note if incident | [`customer-comms/U-CB-1-column-removal.md`](./customer-comms/U-CB-1-column-removal.md) *(exists)* |
| **U-CB-2** — AI Search index vector-dimension change | H2b upgrade migrates embedding model (e.g. 3072 → 768) | 🟡 Yellow | (1) Estimate re-index window from doc volume; (2) schedule maintenance window; (3) run H2b upgrade with `--reindex`; (4) monitor re-index completion | `customer-comms/U-CB-2-vector-dim-change.md` *(pending task 007)* |
| **U-CB-3** — BFF app-reg permission additions requiring re-consent | BFF release adds a Graph / Dataverse / SPE permission scope needing admin consent | 🟡 Yellow | (1) Compute new admin-consent URL; (2) send to customer admin; (3) H0.5 re-consent flow captures `tid`; (4) proceed with H9 | `customer-comms/U-CB-3-reconsent.md` *(pending task 007)* |
| **U-CB-4** — SPE container-type schema change | Major Microsoft Graph SDK / SPE container-type migration | 🟡 Yellow | (1) Announce up-to-24h SPE replication window per T6; (2) confidential-client re-create ceremony if needed; (3) verify container-type post-replication | `customer-comms/U-CB-4-spe-schema.md` *(pending task 007)* |
| **U-CB-5** — KV secret rotation cascading to BFF app restart | H4-rotate variant executes (client secret rotation) | 🟡 Yellow | (1) H4-rotate ceremony writes new secret; (2) slot-swap or App Service restart to invalidate MSAL cache; (3) verify auth flows post-restart | `customer-comms/U-CB-5-kv-rotation.md` *(pending task 007)* |
| **U-CB-6** — Client-secret expiry (BFF-API-ClientSecret 24-month) | Approaching or hit 24-month expiry on any long-lived client secret | 🟡 Yellow (30-day-out alarm) OR 🔴 Red (expired) | (1) 30-day-out: schedule H4-rotate; (2) if expired: emergency rotation + immediate slot-swap + customer notification | `customer-comms/U-CB-6-secret-expiry.md` *(pending task 007)* |

**Cross-reference to §14A.2 handler upgrade-mode semantics**: after clearing the U-CB block, each handler in the upgrade path (H2a / H2b / H4 / H6 / H7 / H9 / H12a/b/c / H14) executes per its upgrade-mode row in the [§14A.2 table](../../projects/customer-provisioning-orchestration-r1/design.md) — NOT its first-install semantics. The most common mistake at this point is a handler falling through to first-install branch on an already-provisioned customer (e.g. H4 overwriting a live client secret because rotation-safe mode was not honored); the §14A.2 table is the authoritative spec that catches this.

## 6. Update cadence — how this document evolves

Per [design.md §14A.3](../../projects/customer-provisioning-orchestration-r1/design.md) + project assumptions (`spec.md` "Version-compatibility matrix maintenance: authored by task-execute at each release-tag milestone; not auto-generated"):

1. **Trigger**: any release-tag on `master` that bumps EITHER `sprk_bffversion` OR `sprk_solutionversion` (or both).
2. **Author**: release manager for that release (or task-execute invocation authoring the release-tag milestone).
3. **Action**: append the new BFF version as a new row AND/OR the new Solution-set version as a new column. Fill EVERY cell in the new row/column against every existing column/row — a partial fill is a defect (per POML acceptance criterion 2).
4. **Verdict rules** (verbatim from §14A.3):
   - ✅ Green: no breaking change class applies (no U-CB-N triggers between the pair).
   - 🟡 Yellow: at least one U-CB-N applies AND the change is applicable with an operator manual step + customer-comms notification.
   - 🔴 Red: pair is fundamentally incompatible (e.g. BFF version cannot function against solution set missing a required entity/column it depends on) — requires an intermediate release the customer must land first.
5. **Do NOT delete historical rows/columns**: customers still on old versions rely on H0 preflight consulting historical cells before upgrading them.
6. **Commit**: the matrix edit ships in the SAME commit / PR as the release-tag.

## 7. What this matrix is NOT

- **Not** a decommission model — see D17 in design.md.
- **Not** a data-migration model — see §11.6 (`spaarke-data` CLI is separate).
- **Not** an SLA — customer-facing SLA lives with the customer-engagement work, not r1's pipeline concern.
- **Not** auto-generated — per project assumption, this doc is human-maintained at each release-tag; there is no CI job producing it.
- **Not** a substitute for the [§14A.2 handler upgrade-mode table](../../projects/customer-provisioning-orchestration-r1/design.md) — that table IS the per-handler semantics; this matrix is the pair-level gate that runs BEFORE that table is consulted.

---

**Change log**

| Version | Date | Change | Author |
|---|---|---|---|
| v1 | 2026-08-17 | Initial publication per FR-34 + design.md §14A.3. Baseline row (BFF 1.0.0-net10) × baseline column (S2026.08) = ✅ Green. Framework + remediation guidance in place; matrix expands per §6 at each release-tag milestone. | task-execute (task 006, project customer-provisioning-orchestration-r1) |
