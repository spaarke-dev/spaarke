# Provisioning-Run Agent Autonomy Design

> **Task**: 202
> **Status**: Design only — task 203 implements
> **Author**: task 202 (2026-08-24)
> **Owner directive (overarching project premise)**: "the expectation for the final delivered solutions is that this process will run E2E with no human interaction. Ultimately the best solution is we have a 'Customer Deployment' web app that allows the user to input whatever information/setting choices and Claude Code / the scripts run everything."

---

## The autonomy spectrum

Provisioning runs today are **fully interactive**: `/provision-environment` skill Step 1 collects operator inputs one at a time; Step 3 requires literal phrase `proceed with provisioning`; Step 5 pauses at each manual gate (H0.5 admin consent, H1 quota, H8 SPE 24h); Step 6 asks operator to confirm handoff. This is correct for the current maturity — every ceremonial pause has caught real errors (owner directive changes, unexpected regional gotchas, admin-consent failures).

But the owner's E2E-no-human premise means we need to plan how to **move each gate up the autonomy ladder** without losing the correctness the ceremony currently provides.

Three autonomy tiers per gate:

| Tier | Semantics | When to use |
|---|---|---|
| **A — Must-stay-interactive** | Human judgment or human-side action required | Admin consent, 24h waits, novel-region deployments, first-of-tier ceremonies |
| **B — Auto-advance-with-verification** | Machine can advance BUT must re-verify state via authoritative source (never trust operator assertion) | Idempotent re-runs, backoff-poll for state, resume-after-restart |
| **C — Can-batch-upfront** | Machine collects all inputs at once via JSON blob or profile file; operator supplies once, machine handles rest | Well-known customer types, batch fleet provisioning, non-interactive CI runs |

---

## Gate-by-gate classification

Every current interactive point in `/provision-environment` SKILL.md, classified:

### Step 0 pre-checks (0a–0f)

| Check | Current | Target tier | Rationale |
|---|---|---|---|
| 0a tool version floors | interactive prompt on failure | **C** — fail-fast + surface remediation URL | Deterministic — no operator judgment |
| 0b operator AAD identity | interactive if not `az login` | **C** — fail-fast with `az login --tenant {tid}` remediation | Deterministic |
| 0c L2 API reachability | interactive on 401/403 | **B** — auto-retry with backoff (60s→180s→300s); fail on persistent | HTTP transient behavior |
| 0d Dataverse MCP status | non-blocking | Keep as **non-blocking** | Informational |
| 0e Working dir + git state | interactive if dirty | **A** — MUST stay interactive | Operator error signal — dirty tree = bug |
| 0f Report + gate | interactive PASS/FAIL summary | **C** — machine-parseable JSON output alongside human table | Both consumers needed |

### Step 0.5 External Prerequisites Verification (NEW, task 203 authors)

All prereq checks in [`PROVISIONING-PREREQUISITES.md`](../../../docs/guides/PROVISIONING-PREREQUISITES.md) are inherently **Tier C** (deterministic). Failures HARD STOP with remediation string. No operator judgment; only operator ACTION (apply remediation, re-run).

### Step 1 intake

Four required inputs today (customerId, tenantId, tenancyModel, profile). Currently interactive one-at-a-time.

**Proposed `--batch` flag**: operator invokes with pre-populated JSON, machine reads + validates + proceeds.

```bash
/provision-environment --batch --input intake.json
```

`intake.json` schema (task 203 authors JSON Schema):

```json
{
  "$schema": "https://spaarke.com/schemas/provisioning-intake/v1.json",
  "customerId": "acme-corp",
  "displayName": "Acme Corporation",
  "tenantId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "environmentId": "11111111-2222-3333-4444-555555555555",
  "tenancyModel": "Model1",
  "profile": "spaarke-hosted-model1-trial",
  "region": "westus2",
  "sharedOpenAiRegion": "westus3",
  "adminContact": {
    "email": "admin@acme.com",
    "displayName": "Acme Admin"
  },
  "upgradeMode": false,
  "batchMode": {
    "autoAdvance": true,
    "requireConfirmationPhrase": false,
    "skipStep6Handoff": false
  }
}
```

Validation happens in-skill at Step 1 (task 203 wires against JSON Schema). Invalid intake → HARD STOP with schema error line-numbers.

**Tier target**: **C** — `--batch` mode enables fleet provisioning + CI-driven runs. Interactive mode remains the default for one-off ops work.

### Step 3 confirmation gate

Currently: operator must type literal phrase `proceed with provisioning` (bare "y" INSUFFICIENT per skill §4.3a.4). This ceremony catches operator-in-wrong-directory / operator-on-wrong-branch errors.

**Tier target**: **A — must stay interactive** for one-off runs. **Suppressible via `--batch` mode `requireConfirmationPhrase: false`** for CI/fleet runs (operator's provisioned CI credential IS the confirmation; separate ceremony would be duplicate authorization surface). Skill Step 3 must log the mode in `handler-log.md` for audit.

### Step 5 manual gates

Three canonical gates:

#### 5a — H0.5 Model 2 admin consent URL

Currently: auto-polls callback every 30s for 2h; then pauses + asks operator.

- **Poll phase**: **B** — auto-advance-with-verification. L2 auto-detects HMAC callback; skill proceeds without operator prompt.
- **Poll-timeout phase**: **A** — must stay interactive. 2h timeout means the customer admin has not consented. Only operator can escalate (email customer admin? extend timeout? abandon?).

**Proposal**: extend max-poll from 2h → configurable per-profile (`spaarke-hosted-model1-trial: 2h`; `customer-owned-model2: 24h` for enterprise customers with slower admin cycles).

#### 5b — H1 Azure quota bump

Currently: waits for operator `resume` or `abandon`.

- **Detect-only phase**: **B** — machine detects `SubscriptionIsOverQuotaForSku`.
- **Response phase**: **A** — must stay interactive if PRQ-S-02 (Support Plan) absent (auto-support-ticket path unavailable per F9). **B** if Support Plan present + auto-support-ticket API succeeds — poll ticket status every 6h (Microsoft SLA for quota tickets: 8-24h).

**Proposal**: skill Step 0.5 verifies PRQ-S-02 upfront; if present + `--batch autoAdvance: true`, auto-file ticket via `az support tickets create` on H1 quota fail. If Plan absent, HARD STOP at Step 0.5 with remediation.

#### 5c — H8 SPE 24h replication wait

Currently: skill exits + auto-reinvokes 25h later OR polls hourly.

- **Both variants**: **B** — SPE replication is asynchronous but auto-detectable via `az rest` on containerType.
- Auto-reinvoke variant is more autonomy-friendly for `--batch` mode; keep as default.

**Note (from user memory `feedback_spe_container_timing.md`)**: "Microsoft's documented 24h SPE container-type provisioning wait is near-instantaneous in practice; do not use as a Wave H-4 blocker or estimate padding." So the 25h auto-reinvoke may be over-conservative; poll-every-15min for first 2h then hourly is likely sufficient. Task 203 may test empirically.

#### 5d — generic WaitingOnGate

Any handler-declared gate not listed above.

- **Tier target**: **A** — interactive by default (novel gates require operator judgment).
- **B** if handler declares `autoAdvanceable: true` + `pollRecipe` + `maxWait`.

### Step 6 completion handoff

Currently: operator confirms handoff.

- **Tier target**: **B** — machine writes `handoff-report.md` unconditionally; operator confirmation is informational.
- **Suppressible via `--batch skipStep6Handoff: true`** for fleet runs.

### Step 7 mandatory postmortem (NEW, task 203 authors)

Every completed run writes `lessons-learned.md`. Skill prompts operator to enumerate 3-5 surprises + fill template.

- **Tier target**: **A** — must stay interactive. Lessons capture is judgment-heavy; auto-generation would produce noise.
- **`--batch` flag CANNOT skip Step 7** — mandatory postmortem is invariant for audit trail.

---

## What autonomy the Web-App proposal buys

Owner's target end-state: "we have a 'Customer Deployment' web app that allows the user to input whatever information/setting choices and Claude Code / the scripts run everything."

The web app becomes:
- **Intake collector** — replaces skill Step 1 with a form; validates against JSON Schema; posts to L2.
- **Progress viewer** — SSE stream of handler outcomes (per follow-on `webapp-prereq-sse-progress`).
- **Gate resolver UI** — surfaces WaitingOnGate items with actionable options (send admin-consent email; file quota ticket; check SPE readiness).
- **Handoff viewer** — renders `handoff-report.md` inline; downloads full run folder.
- **Postmortem editor** — inline editor for `lessons-learned.md` with template + save-to-run-folder.

None of this changes the skill's underlying autonomy classification. Web app is a **surface** over the skill's Tier B + Tier C behaviors. Tier A gates remain — they're the ones where human judgment IS the correctness property.

**Prereq for web-app phase** (from `follow-on-customer-deployment-webapp-proposal.md`):
- ≥3 Model 1 customers successfully onboarded via CLI-mode `--batch` runs
- F1-F9 fresh-sub gotchas fully absorbed into Step 0.5 (Tier C) so operator never hits them mid-run
- SSE progress stream implemented on L2 (new BFF surface)

Task 203 does NOT build the web app. Task 203 builds the CLI-mode autonomy foundation the web app needs.

---

## Task 203 concrete implementation items

1. Add `--batch` flag to `/provision-environment` skill Step 1 + Step 3 (suppressible confirmation phrase).
2. Author JSON Schema at `scripts/provisioning-prereqs/intake.schema.json` (v1).
3. Wire Step 0.5 External Prerequisites Verification (Tier C, reads `prereqs.yaml`).
4. Move Step 5a poll-phase + Step 5c to Tier B (`--batch` compatible).
5. Wire Step 5b auto-support-ticket path (Tier B if PRQ-S-02 present).
6. Write Step 6 handoff-report.md unconditionally (Tier B); flag suppresses only the interactive confirmation.
7. Author Step 7 mandatory postmortem (Tier A always — cannot skip even in batch).
8. Update skill's fallback matrix with `--batch`-specific rows (e.g. "F1 MCP disconnect in batch mode: fail HARD, don't prompt for `pac data` fallback").
9. Log tier decisions per handler in `handler-log.md` for audit trail.

---

## Invariants that MUST hold in every autonomy mode

Per spec.md §NFR-11 + FR-28 + owner directives:

- **Operator's own AAD identity** is the auth context (NEVER a service principal — even in `--batch`/CI mode, the CI runner authenticates as a distinct AAD user with Contributor role).
- **All tenant IDs are explicit** (I1 — no default tenant fallback in `--batch` intake; JSON Schema `required: [tenantId]`).
- **BINDING pre-check gate** (§7.9) applies before any KV secret rename/delete regardless of mode. `--batch` mode CANNOT bypass.
- **Never delete** `Dataverse-ClientSecret` / `BFF-API-ClientSecret` regardless of mode.
- **BFF publish size** verified on every BFF-touching run regardless of mode (NFR-01).
- **Postmortem is mandatory** — Step 7 cannot be skipped by any flag.

These are invariants, not tier decisions. They apply at Tier A / B / C uniformly.
