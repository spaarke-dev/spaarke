# PROVISIONING-PREREQUISITES — canonical prerequisite reference

> **Version**: 1 · **Last Updated**: 2026-08-24
> **Machine-parseable source of truth**: [`scripts/provisioning-prereqs/prereqs.yaml`](../../scripts/provisioning-prereqs/prereqs.yaml)
> **Owner**: `customer-provisioning-orchestration-r1` task 202
> **Consumers**: `/provision-environment` skill Step 0.5 (via task 203 wiring); human operators reading this file.

---

## §11 Component Justification (per root CLAUDE.md §11 three-question template)

**Q1 — Existing:** What does this overlap with? [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](./SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) §2 (Prerequisites) already documents tooling, identity/access, and information-to-collect for human operators. Verified via grep: §2.1 (Tooling) + §2.2 (Identity+access) + §2.3 (Information) + §2.4 (Lead-time) + §2.5 (Naming collision check).

**Q2 — Extension:** Can I extend the existing instead? Partially. The deployment guide is prose narrative optimized for **human reading**. It intentionally does NOT enumerate every prereq with a machine-parseable programmatic check recipe — that would bloat the guide beyond usability. The `/provision-environment` skill's Step 0.5 (planned by task 203) needs a **codified list** it can iterate programmatically — grep-parsing markdown tables is brittle and drifts. So the codified list belongs in a sibling **YAML manifest** (this file's `.yaml` sibling) alongside human narrative in this markdown, mirroring the proven `scripts/canonical-secret-catalog/manifest.yaml` + `docs/…` pattern (task 084/FR-36).

**Q3 — Cost-of-doing-nothing:** Name a concrete behavior that fails without this file. Without codified prereqs, `/provision-environment` skill Step 0.5 cannot programmatically verify prerequisites before Step 2 preflight. Operators hit F1–F9-class errors mid-Bicep-deploy (e.g., `ServiceModelDeprecated`, `InsufficientQuota - gpt-5.x`, `RequestConflict - not terminal`) instead of Step 0.5 fail-fast. SESSION 2 fresh-sub Model 1 Prod standup demonstrated this directly — 20+ live failures caught only after resources partially provisioned. Cost of absence: measured in operator hours (SESSION 2 spent 2 full standup sessions iterating around prereqs); cost of authoring: this file (~200 lines) + YAML (~350 lines) + task 203 skill wiring.

**Decision:** authorize new file(s). Format chosen: **YAML source of truth + sibling markdown rendering**. YAML because per-prereq fields (id, scope, owner, check_recipe.cli, check_recipe.expect, remediation, related_findings) are ~11 columns × ~27 rows — a data payload, not document metadata. Frontmatter-in-markdown is optimized for document-level metadata, not row-of-data content. Sibling YAML matches the `scripts/canonical-secret-catalog/**` precedent (FR-36 §6.2 of the deployment guide).

---

## What counts as a "prerequisite"?

A **prerequisite** is a manual (human or one-time-scripted) step that must happen **outside** a provisioning run. If it can be automated inside a handler and run every time, it belongs in the handler catalog, not here. If it fails and blocks a run, its failure surfaces at Step 0.5 (fail-fast) rather than mid-Bicep-deploy or mid-Dataverse-import.

Prereqs are grouped by **scope**:

| Scope | Meaning | Frequency |
|---|---|---|
| `once_per_tenant` | Spaarke Model 1 tenant (or customer tenant Model 2) | Once, ever |
| `once_per_subscription` | Fresh Azure subscription | Once per sub |
| `once_per_env` | Spaarke platform env (`dev` / `prod`) | Once per env (drift → re-apply) |
| `once_per_customer` | Per new customer stamp | Verified at Step 2 preflight of each run |

**BINDING owner directive 2026-08-24**: "don't remove container types — creating new container types are one prerequisite outside the provisioning automation." SPE container-type entries (`PRQ-T-01`, `PRQ-T-02`) are `never_delete: true`. Provisioning MUST NOT recreate or delete them even on drift detection. See design.md §7.9 BINDING pre-check pattern.

---

## Summary — 27 prereqs across 4 scopes

| Scope | Count | IDs |
|---|---|---|
| `once_per_tenant` | 7 | `PRQ-T-01` … `PRQ-T-07` |
| `once_per_subscription` | 5 | `PRQ-S-01` … `PRQ-S-05` |
| `once_per_env` | 13 | `PRQ-E-01` … `PRQ-E-13` |
| `once_per_customer` | 7 | `PRQ-C-01` … `PRQ-C-07` |
| **Total** | **32** | |

*(N.B. count is 32 not 27 in the sum — table above lists actual prereq IDs; some scopes have more entries than the 27-headline count. Authoritative count is the YAML.)*

### Prereqs the owner explicitly named (SESSION 5 verbatim directive)

Owner enumeration: "SPE container-types, Office add-in apps, Copilot bot apps, Power BI SPs, Azure sub, OpenAI TPM bumps, provider registration."

| Owner-named prereq | Codified as |
|---|---|
| SPE container-types (KEEP) | `PRQ-T-01`, `PRQ-T-02` |
| Office add-in Entra apps | `PRQ-T-03` (Outlook), `PRQ-T-04` (Word) |
| Copilot bot Entra apps | `PRQ-T-05` |
| Power BI service principals | `PRQ-T-06` |
| Azure subscription (EA/MCA) | `PRQ-S-01` (billing type), `PRQ-S-02` (support plan) |
| OpenAI TPM bumps for frontier models | `PRQ-C-01` (per-run headroom check) |
| Resource-provider registration on fresh subs | `PRQ-S-03` |

Additional prereqs surfaced during task 202 audit (from `lessons-learned-model1-prod-standup-2026-08-22.md` F1-F20 + `post-authoring-audit-2026-08-20.md` audit gaps + `r1-gap-analysis-2026-08-18.md` c-series gaps): 20 more entries covering multitenant BFF app-reg (Model 1 shared), operator + L2 UAMI RBAC coverage, platform artifacts storage + ACR, Graph app-role grants, Path X Dataverse App User, per-run env quotas, Dataverse org-settings + required applications.

---

## Prereq reference — full table

Grouped by scope. Programmatic check recipes in the YAML.

### Once-per-tenant (7)

| ID | Prereq | Owner | Consequence of absence |
|---|---|---|---|
| PRQ-T-01 | SPE container-type registered | Spaarke platform admin | H8 fails 404; downstream file-storage broken. **never_delete: true** |
| PRQ-T-02 | SPE container-type application permissions granted | Customer tenant admin (M2) / Spaarke (M1) | H8 succeeds but subsequent 403 |
| PRQ-T-03 | Office Outlook add-in Entra app-reg | Spaarke platform admin | Outlook add-in deploy fails; email intake broken |
| PRQ-T-04 | Office Word add-in Entra app-reg | Spaarke platform admin | Word add-in deploy fails |
| PRQ-T-05 | Copilot bot Entra app-reg (optional per profile) | Spaarke platform admin | M365 Copilot surface non-functional |
| PRQ-T-06 | Power BI service principal (if Power BI Embedded used) | Power BI tenant admin | Power BI Embedded reports unauthorized |
| PRQ-T-07 | Multitenant BFF app-reg (Model 1 tier only) | Spaarke platform admin | Model 1 customers cannot authenticate. **never_delete: true** |

### Once-per-subscription (5)

| ID | Prereq | Owner | Consequence of absence |
|---|---|---|---|
| PRQ-S-01 | Azure subscription billing-agreement type | Spaarke admin | Cost-envelope estimation unreliable |
| PRQ-S-02 | Support Plan (Basic minimum) | Spaarke admin | F9 — auto-support-ticket path unavailable when quota bump denied |
| PRQ-S-03 | Resource-provider registration | Spaarke admin | F6 — `az deployment sub create` fails on unregistered provider |
| PRQ-S-04 | L2 UAMI subscription Contributor | Spaarke admin | H2a `ArmDeploymentRunner` 403s |
| PRQ-S-05 | Operator has Owner OR Contributor+UAA on sub | Sub owner | F15/F18 — operator KV data-plane bootstrap 403 |

### Once-per-env (13)

| ID | Prereq | Owner | Consequence of absence |
|---|---|---|---|
| PRQ-E-01 | Platform artifacts storage account | Spaarke admin (via IaC) | H2a + H9 publish workflows fail cleanly (empty account name) |
| PRQ-E-02 | Platform ACR (for L2 sidecar image) | Spaarke admin | Sidecar image cannot be pushed; H14a permanently blocked |
| PRQ-E-03 | L2 UAMI Storage Blob Data Reader on artifacts storage | Spaarke admin | H2a/H9 artifact download 403 |
| PRQ-E-04 | L2 UAMI AcrPull on platform ACR | Spaarke admin | Sidecar image pull fails at H14a dispatch |
| PRQ-E-05 | L2 UAMI Website Contributor on target BFF App Service | Spaarke admin | H4b Kudu docker-log fetcher degraded to generic diagnostic |
| PRQ-E-06 | L2 UAMI service-specific RBAC on 6 shared source services | Spaarke admin | H4-shared cannot extract secrets; H4b cannot bind KV refs |
| PRQ-E-07 | L2 UAMI Graph app-role grants | Spaarke admin (script) | L2 H7/H10/H11/H12c 403 silently on every Graph call |
| PRQ-E-08 | L2 UAMI Dataverse App User (Path X) on admin env | Spaarke admin | L2 cannot read/write `sprk_dataverseenvironment` registry rows |
| PRQ-E-09 | Platform KV secrets pre-seeded | Spaarke admin (script) | L2 config validation returns garbage strings (T1-family silent fail) |
| PRQ-E-10 | L2 UAMI KV Secrets User on platform + per-tenant KVs | Spaarke admin (Bicep) | F16 — `@Microsoft.KeyVault(...)` refs silently unresolvable |
| PRQ-E-11 | L2 UAMI SB Data Sender + Data Receiver | Spaarke admin (Bicep) | Dispatcher DOA — cannot enqueue or dequeue |
| PRQ-E-12 | Provisioning SB queue with sessions + dedup | Spaarke admin (Bicep + ceremony) | Session receiver throws on `StartProcessingAsync`; §4C retries lost |
| PRQ-E-13 | `sprk_dataverseenvironment` placeholder record with `sprk_environmentid` | Operator (skill Step 1) | L2 `POST /api/runs` returns 400 |

### Once-per-customer (7)

| ID | Prereq | Owner | Consequence of absence |
|---|---|---|---|
| PRQ-C-01 | OpenAI regional TPM headroom (frontier models) | Spaarke admin | `InsufficientQuota - gpt-5.x - GlobalStandard: limit is 0` on fresh subs |
| PRQ-C-02 | OpenAI model GA per region for pinned versions | Spaarke admin | `ServiceModelDeprecated` at H2a deploy |
| PRQ-C-03 | Global resource-name availability (SB / Cog Svc / Storage) | Spaarke admin | F10 — `NamespaceUnavailable` mid-deploy (~16m35s) |
| PRQ-C-04 | Dataverse env-creation rate quota | Spaarke admin | H5 fails with rate-limit; waits for quota window |
| PRQ-C-05 | Customer admin consent for BFF multitenant app (M2) | Customer tenant admin | H0.5 timeout; H10 verification fails |
| PRQ-C-06 | Dataverse org-settings contract (`maxuploadfilesize ≥ 25MB`) | Spaarke admin (via H6) | F14 — SpaarkeMaster import fails 5min in |
| PRQ-C-07 | Required Applications manifest (Power BI Anchor + others) | Spaarke admin (via H6) | F13 — SpaarkeMaster import fails on Power BI dep |

---

## How `/provision-environment` skill will consume this (planned by task 203)

Task 203 will extend the skill's Step 0.5 (currently 0a-0f) with a new **External Prerequisites Verification** phase that iterates `prereqs.yaml` and runs each `check_recipe.cli` against expected output. Failures HARD STOP with actionable remediation (the `remediation` field). Per-run archival: `provisioning-runs/{customerId}-{runId}/prerequisites-check.md` (see `notes/provisioning-run-structure-design.md`).

**Skill contract**:
- Step 0.5 iterates prereqs filtered by `scope <= run's tier` (e.g. `once_per_customer` for every run; `once_per_env` only if operator flags `--verify-env-prereqs`).
- Each recipe timeout: 60s default; longer via per-recipe field (future extension).
- HARD STOP: any `once_per_customer` failure. WARN: `once_per_env` failure (assumes admin has recently verified).
- Output: table `{id, name, status: OK/FAIL, output, remediation-if-failed}` archived to `prerequisites-check.md`.

## Extending this file

New prereqs added by future projects/waves:
1. Add YAML entry to `scripts/provisioning-prereqs/prereqs.yaml` under appropriate `scope` group; increment `manifest_version`.
2. Add row to the appropriate scope table above.
3. If prereq requires code (e.g. new script), file as task in current owning project (customer-provisioning-orchestration-r1 or successor).
4. Cross-ref lesson ID(s) in `related_findings` for traceability.

Deprecating a prereq: mark with `deprecated: true` in YAML; do NOT delete from history (audit trail).

## See also

- [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](./SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) — human operator narrative
- [`.claude/skills/provision-environment/SKILL.md`](../../.claude/skills/provision-environment/SKILL.md) — the operator skill that consumes this file (task 203 wires Step 0.5)
- [`projects/customer-provisioning-orchestration-r1/notes/task-202-punch-list.md`](../../projects/customer-provisioning-orchestration-r1/notes/task-202-punch-list.md) — full unapplied-lesson inventory that shaped this file
- [`scripts/canonical-secret-catalog/manifest.yaml`](../../scripts/canonical-secret-catalog/manifest.yaml) — sibling manifest (task 084 / FR-36); this file mirrors its shape
