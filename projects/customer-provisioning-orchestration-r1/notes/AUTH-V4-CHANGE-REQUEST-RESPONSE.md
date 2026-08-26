# Response → `spaarke-auth-v4-dataverse-MI` Change Request

> **From**: `customer-provisioning-orchestration-r1` · **Date**: 2026-08-19 · **Status**: APPLIED
> **Responds to**: [`PROVISIONING-CHANGE-REQUEST.md`](PROVISIONING-CHANGE-REQUEST.md) (filed by auth-v4, ADR-028 Amendment A4 + Exception E-3)
> **Decision authority**: owner sign-off 2026-08-19
> **Applied in**: spec.md (v3.5), design.md (v3.4 → v3.5), tasks 125/126/130/142 (amended)

---

## TL;DR

r1 accepts the FIC adoption for the customer BFF's OBO credential, with a **split**:

- **Model 1 (shared trial/SMB)**: Reading 1 — one shared multitenant BFF app registration (`AzureADMultipleOrgs`, matches live state). No per-customer app-reg or FIC creation. Per-customer trust captured via the existing H0.5/D18 consent-callback.
- **Model 2 (dedicated stamp)**: Reading 2 — per-customer BFF app registration + a FIC trusting the shared BFF UAMI, created by H3 per your §3.1 recipe.
- **New invariant I6** (Model 1 only, ArchTest-enforced): adopted as proposed in your §5.4.
- **R23 closed** per your §4 corrected cap analysis — the 20-FIC-per-app cap does not bind either project's shape.
- **§5.3 pluggability contract**: accepted as spec.md FR-39 — H3/H4 support both secret and FIC without a handler restructure; you own the rollout schedule and secret retirement (Phase 5).
- **§5.2 doc fix**: applied — design.md §9.1's contradictory sentence is corrected and scoped to Model 1.

Four r1 task POMLs (125, 126, 130, 142) were amended with escalation triggers and acceptance-criteria updates reflecting this split. Details below.

---

## r1's own MI-FIC investigation — and where it fell short

Before your change request landed, r1 had already looked at MI-FIC on its own initiative: the Q5 research spike (`notes/graph-spe-2026-08-standards-spike.md`, §3 "Managed Identity for Graph app-only") flagged MI-as-FIC as GA'd in 2026 and material for Model 2's cross-tenant story, and folded it into the design as **Risk R23** (design.md §12, then line 1429 in v3.3, now §12 in the current v3.5 numbering) and the **DS-8 Path Z** framing (design.md §9.6) — a noted-but-not-built r2+ escape hatch for L2's *own* future cross-tenant Dataverse writes.

Both of those readings concluded the same thing: *interesting, not now.* Your §4 cap analysis is the reason that conclusion was wrong. Our spike's own language (line ~59) is the tell: *"you can add a UAMI as a federated credential on an Entra app registration — the app then trusts tokens issued to the UAMI."* We correctly identified the mechanism but reasoned about the cap as if it counted credentials *on the UAMI* (MI-as-recipient framing — "how many app-regs can one UAMI be trusted by") rather than *on the app registration* (MI-as-issuer framing — "how many UAMIs must one app-reg trust," which in every shape we deploy is one or a handful). That inversion made the cap look like a live scaling concern for Model 2's per-customer app-reg count; it isn't, because each per-customer app-reg only ever needs to trust the *one* shared BFF UAMI.

Thank you for the more thorough cap analysis in your §4 — it's the correction that let us close R23 cleanly rather than carry it as an open risk into Phase C+.

---

## Split rationale

Model 1 and Model 2 already diverge on isolation posture (D3): Model 1 accepts logical-over-physical isolation for a shared fixed-floor tier; Model 2 is dedicated-per-customer for regulated/enterprise. The app-reg shape follows the same divergence naturally:

- **Model 1** — the shared BFF App Service composition (one BFF instance serving every Model 1 trial/SMB customer) pairs naturally with a **single shared multitenant app-reg**. The live app registration is already `AzureADMultipleOrgs` — this was always the shape, we just hadn't reconciled spec.md's blanket "per-customer app-reg" MUST rule against it. The D18 consent-callback mechanism was always multitenant-shaped (it exists specifically to capture per-customer `tid` trust against a *shared* app object), so nothing about Model 1's mechanism needed to change — only the doc/rule needed to stop contradicting it.
- **Model 2** — dedicated stamps keep **per-customer app-regs** for tenant-level isolation matching the customer-facing governance/audit posture regulated/enterprise customers expect (a customer can point their own security team at "this Entra app object is ours"). FIC replaces the client secret as that app-reg's confidential credential; the per-customer-object shape is unchanged.

---

## New invariant I6 — adopted, Model 1 only

Text as proposed in your §5.4, adopted verbatim (spec.md FR-40, design.md §4D):

> The app registration used for an OBO exchange MUST be derived from per-tenant request context; no default or fallback app registration. ArchTest-enforced, same pattern as I1–I5.

Scoped to **Model 1 only** per your own framing — Model 2's per-customer app-reg makes the invariant structurally true by construction, so enforcing it there is a no-op-but-still-verified rather than load-bearing. `Spaarke.ArchTests.TenantIsolation.I6_ObApp*` is the ArchTest name (matching the I1–I5 naming convention). Task 130 (H3 heavy port) carries the enforcement acceptance criteria.

---

## Response to your §8 open items that touch us

| # | Item | Response |
|---|---|---|
| 1 | `config/spaarke-resources.yaml` records `AzureADMyOrg`; live is `AzureADMultipleOrgs`. Stale. | **Your side** — no action from r1. |
| 2 | Phantom resource inventory (`spe-api-dev-67e2xz` / `spe-infrastructure-westus2` don't exist; live is `spaarke-bff-dev` / `rg-spaarke-dev`). | **We'll cross-check our Bicep params in Wave G-1 wrap-up** — if any r1 Bicep module references the phantom names, we'll catch it there and file a fix. No known hits as of 2026-08-19, but the check wasn't exhaustive until now. |
| 3 | `docs/architecture/auth-azure-resources.md` doc drift (system-assigned vs UAMI; contradicts itself on which app-reg owns `BFF-API-ClientSecret`). | **Your side** — no action from r1. |
| 4 | `stacks/dev.bicepparam:12` declares `B1`; live is `P1v3`. | **Shared** — r1 will fix in the Bicep chain; already scoped under r1 task 109 (Bicep config-key drift), no new task needed. |
| 5 | Duplicate lowercase KV alias `bff-api-client-secret` (Office add-in dependency). | **Your side** — retirement owner. Flagging for your awareness that r1's Phase G/H canonical-naming work (already landed) did NOT touch this alias, since it wasn't in the canonical secret-catalog manifest's scope at the time — if you retire it, check the Office add-in deploy path first per your own note. |
| 6 | Master IaC creates system-assigned identity; live uses UAMI (r1's branch owns the UAMI Bicep). | **r1 is authoritative here** — `infrastructure/bicep/modules/uami.bicep` + the `app-service.bicep` refactor (Phase C, tasks 027–030, all ✅) is the UAMI source of truth. If master IaC still emits system-assigned by default outside r1's branch, that's a separate drift item between master and r1's Phase C work, not something r1 needs to additionally fix — Phase C's job was precisely to make UAMI the standard, and it has. Flagging this explicitly in case a THIRD project reads "master IaC" as current without knowing Phase C superseded it. |

---

## Coordination overlap in `scripts/`

Per your §7: **`Register-EntraAppRegistrations.ps1`, `Rotate-Secrets.ps1`, `Seed-ProductionKeyVault.ps1`, `Configure-ProductionAppSettings.ps1`** are the overlap.

- **`Register-EntraAppRegistrations.ps1`** — you own the FIC-extension primary home (§3.2). r1's Wave G-3 task 130 (H3 heavy port) will **invoke** your extended script/logic rather than duplicate FIC-creation logic — task 130 now carries an explicit escalation trigger + soft-dependency constraint to check for your extension before authoring FIC creation independently. **Recommend you land the FIC extension before Wave G-3 dispatches** so task 130 can invoke it cleanly instead of building a temporary duplicate that then needs reconciling.
- **`Rotate-Secrets.ps1` / `Seed-ProductionKeyVault.ps1` / `Configure-ProductionAppSettings.ps1`** — these are your rotation/seeding-retirement surface for the BFF secret path; r1's H3/H4 ports (tasks 125/126/130) don't touch these scripts directly (r1's Option D runtime is porting the *handler-level* logic to pure .NET SDK calls, not consuming these PowerShell scripts as collaborators for H3/H4 specifically — `Register-EntraAppRegistrations.ps1` is the one exception, noted above). No conflict expected, but flag if your retirement work touches anything r1's Bicep or app-settings wiring depends on.

---

## Confirmation on your §5.4 I6 proposal

Adopted as-is, scoped to Model 1 only, per the split rationale above. No changes to your proposed text or enforcement mechanism.

---

## What auth-v4 needs to know

r1's Phase C'' (execution engine build, 58 POMLs across 7 waves) is currently in **Wave G-1 (foundation)**, with tasks 100/101/106/107/108🟡/111/112/114/115/116/117 committed. Your FIC migration timing should coordinate with **r1's Wave G-3 dispatch** specifically — that's when task 130 (H3 heavy port, the task that actually creates Model 2's per-customer FIC) executes. If your `Register-EntraAppRegistrations.ps1` FIC extension isn't ready by then, task 130 will build the FIC-creation logic itself per your §3.1 recipe (task 130's prompt now embeds that recipe directly) — not blocked either way, but cleaner if your extension lands first per the soft-dependency note above.

Tasks 125 (H4 SDK port) and 126 (H4 real-value sourcing) execute earlier, in Wave G-2, and carry the pluggability-contract obligations (FR-39) — `BFF-API-ClientSecret` seeding stays runnable through your transition even though its value may not be actively consumed by a FIC-migrated BFF. Task 142 (H7 credential provisioning) is Wave G-4 and is the most likely task to actually retire outright if your Phase 5 lands before r1 reaches it — it now carries that escalation trigger explicitly.

---

## Judgment calls made during reconciliation

Documented here for transparency, per this project's ADR Conflict Resolution Protocol spirit (root CLAUDE.md §6.5) even though this isn't strictly an ADR conflict:

1. **Doc-location drift**: your change request cited `spec.md:236` and `design.md:1006`/`:1857`. By 2026-08-19, r1's spec.md/design.md had moved to v3.4 with more FRs and sections added since whatever snapshot you read from — the actual current locations are spec.md ~line 249 (MUST rule), design.md ~line 1076/1079 (the §9.1 contradiction), and design.md §12 line ~1515 (the authoritative R23 entry — line 1857 in the current doc is a §20 CHANGELOG bullet *describing* v3.3's addition of R23, not the risk-register entry itself). Applied all edits at the correct current locations; content intent unchanged from what you asked for.
2. **FR numbering**: your proposed I6 text suggested "FR-33" — but FR-33 is already in use in r1's spec.md for the silent-fail-trap catalog (§4B). Numbered the new FIC-credential FR as **FR-39** (next sequential slot after FR-38) and I6 as **FR-40** (placed near FR-39 rather than renumbered into the middle of the existing §4D FR-28..FR-32 block, to avoid disturbing that block's stable numbering).
3. **"Already-migrated-to-FIC" sentinel contract for H4/H7 (tasks 126/142)**: your change request didn't specify the exact KV-secret contract for a customer whose BFF has already migrated to FIC before r1's H4 seeds their KV (omit the secret entirely vs. write a documented sentinel value). Left this open in both amended POMLs as an explicit coordination point rather than inventing a format unilaterally — please advise which shape your BFF-side code will actually check for (or ignore) when we reach Wave G-2/G-3.
4. **§9A row 1 + the "one-page mental model" + the §9.1 section heading**: not explicitly named in your change request, but both still said "(1 per customer)" / "(2 Per Customer)" for the BFF app-reg in ways that would have left a Model-1-per-customer claim standing after the MUST-rule split. Corrected both for consistency — flagging in case you want to spot-check the surrounding §9A table for any other artifact your team reads that assumes per-customer Model 1.

---

## Status

**APPLIED 2026-08-19.** See `PROVISIONING-CHANGE-REQUEST.md` for the applied-banner cross-reference. All four amended task POMLs (125, 126, 130, 142) are in `tasks/`; spec.md and design.md carry the full v3.5 amendment set described above.
