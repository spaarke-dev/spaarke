# Task 033 — remove the secret and reconcile the estate

> **Status**: 🔄 IN PROGRESS — step 1 of 7 done. **Nothing has been deleted yet.**
> **Date**: 2026-08-24

---

## 0. Two things surfaced before any deletion

### 0.1 🔴 STEP 1 FINDING — the stated reason for caution about the lowercase alias is FALSE

The project has carried this claim since the spec, in the 033 POML `<background>`, in spec success
criterion 7, and in `config/spaarke-resources.yaml:123`:

> *"a SIXTH lowercase Key Vault alias `bff-api-client-secret` **used by the Office add-in deploy** — any
> removal that ignores it breaks the add-in."*

**It is not true.** Traced exhaustively:

| Consumer | Uses the alias? | Evidence |
|---|---|---|
| `.github/workflows/deploy-office-addins.yml` | **NO** | consumes `BFF_API_CLIENT_ID` (a client **id**), `AZURE_STATIC_WEB_APPS_API_TOKEN`, `GITHUB_TOKEN`. No client secret of any kind |
| `scripts/Deploy-OfficeAddins.ps1` | **NO** | zero matches for `ClientSecret` / `client-secret` / `CLIENT_SECRET` / `KeyVault` |
| `config/spaarke-resources.yaml` | yes — `:289`, `:313`, `:476`, `:494` | but it is a **manifest**, not executable |
| `scripts/naming-conformance-check.ps1` | mentions it | to **flag** the duplicate as *"a rotation hazard"* — it complains about the alias, it does not consume it |

**The real consumer is different, and so is the real risk.** `config/spaarke-resources.yaml` is read by
`scripts/Sync-LocalConfig.ps1` — which resolves `kv:bff-api-client-secret` to sync secrets into a **local
development** config file. So deleting the alias threatens **local `dotnet run`**, i.e. spec success
criterion **9**, and *not* the Office add-in deploy of criterion **7**.

**Why this matters beyond the immediate fix.** This is the same failure this whole project exists to
correct: a **false sentence in text** driving a decision, unexamined, across multiple documents. The
original was `.claude/constraints/auth.md:108` — *"OBO flow (OAuth spec requires confidential client +
secret)"* — which made three prior audits conclude the secret was permanent. This one would have sent 033
to protect the wrong surface and leave the actually-affected one broken.

**Consequences for the remaining steps:**

- Step 3's re-verification target changes: re-verify **`Sync-LocalConfig.ps1` / local `dotnet run`**, not
  the add-in deploy.
- The claim must be corrected in all four places it appears (POML background, spec criterion 7,
  `spaarke-resources.yaml:123`, and any doc repeating it) — corrected, not silently dropped.
- Success criterion 7's wording ("Office add-in deploy succeeds") should be **kept as a check** — it costs
  nothing and the add-in deploy is worth confirming — but its stated *rationale* is wrong and must not be
  cited as the reason the alias is load-bearing.

### 0.2 🛑 CONFLICT-CHECK HARD WARN — `.claude/constraints/auth.md` is contended

Step 6 must edit `.claude/constraints/auth.md` (to close ADR-028 exception **E-3**).
**PR #812 (`work/unified-access-control-r2`) modifies the same file.**

Per the `/conflict-check` decision table this is the *hard warn* case: watchlist hot-path (skill
directives) + another active worktree + **file overlap**. Surfaced for coordination before step 6, not
silently merged into.

Also on that PR: `.claude/agent-memory/researcher/**` (no collision — append-only memory).
PRs #806 and #779 touch `.claude/` but **not** `constraints/auth.md`; #806 and #779 both touch root
`CLAUDE.md`, which 033 does not.

---

## 1. Step 1 — COMPLETE

Verified the Office add-in deploy path's dependency on the lowercase alias: **there is none** (§0.1).
The dependency that does exist is `Sync-LocalConfig.ps1` → local dev.

## 2. Steps 2–7 — NOT STARTED

Nothing removed. Current live state unchanged:

| | |
|---|---|
| App settings (default slot, the only slot) | 4 secret keys still present |
| Key Vault `spaarke-spekvcert` | `BFF-API-ClientSecret` + `bff-api-client-secret` both present |
| Credential order | overrides absent → canonical `[ManagedIdentityFederated, ClientSecret]` |
| Rollback rung | **2 — credential reorder** (proven 031 §5.6). Step 2 retires it |

## 3. Prep carried in from 032

- App-setting **name** baseline for both slots (captured before the staging slot was deleted):
  [`notes/appsettings-baseline-pre-033.md`](../appsettings-baseline-pre-033.md). Diff against it after
  every settings change so a delta is attributable — 032 §4 could not attribute a 213→212 drop.
- Estate numbers corrected: **15** scripts reference a client secret, not the 11 the notes claim; 13
  docs/config/workflow files.
- **Key Vault name corrected: `spaarke-spekvcert`**, not `spaarke-spekv-dev` (which does not resolve).
- ⚠️ **`spe-owning-app-secret` is in the same vault and is OUT OF SCOPE** — ADR-028 **E-1**, per-customer
  owning apps. Do not touch. `Graph-API-ClientSecret` is step 7's item and needs its own check first.
