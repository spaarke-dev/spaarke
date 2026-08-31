# §6.5 Conflict Resolution — ADR-028 A4/E-3 vs the never-delete BINDING on `BFF-API-ClientSecret` / `Dataverse-ClientSecret`

> **Date**: 2026-08-25
> **Deliverable**: 2 of 4 (auth-v4 integration response) — formal root CLAUDE.md §6.5 conflict-resolution record
> **Source docs**: auth-v4 canonical `PROVISIONING-CHANGE-REQUEST.md` (626-line, auth-v4 worktree — §1 TL;DR, §9.1, §10 addendum + 2026-08-25 CORRECTION); `origin/master` ADR-028 (Amendment A4 + E-3 CLOSED banner, commits `dee3df03c` + `39b2bda38`)
> **Companions**: [`../auth-v4-integration-remediation-plan.md`](../auth-v4-integration-remediation-plan.md) §2 · [`../auth-v4-integration-draft-punch-rows.md`](../auth-v4-integration-draft-punch-rows.md) (rows A35, A38, A44) · [`../auth-v4-integration-open-questions.md`](../auth-v4-integration-open-questions.md) (Q3, Q6, Q7)
> **Status**: ✅ **APPROVED 2026-08-25 (owner)** — Q3 signed as proposed; sunset date **2026-11-23** (aligns with auth-v4 obligation 051-E, outer bound governs; auth-v4 to receive one-line confirmation via commit-message reference since discharge reply skipped per Q1). Owner-narrowed disposition to prong 3 per Q7 recorded in §"Owner refinements 2026-08-25" below. EDITs 1-4 unblocked; fire post-A35 merge.

## Owner refinements 2026-08-25 (post-approval)

Owner accepted the hybrid resolution as written with three narrowings:

1. **Q3 sunset date**: **2026-11-23** governs (outer bound; matches auth-v4 obligation 051-E). Soft-delete recovery to 2026-11-22 is a coincidence; the outer date is authoritative for the constraint file's language.
2. **Q7 prong-3 scope narrowing**: no live production environment currently exists. The only live environment is `spaarkedev1` (dev). Prong 3 ("unmigrated environments may still resolve `Dataverse-ClientSecret`") therefore applies to: (a) `spaarkedev1` today, (b) any greenfield Model 2 stamp provisioned during the transition window BEFORE A36-A42 land — but per Q6 disposition, **no such stamp will be provisioned before A36-A42**, so prong 3 collapses to `spaarkedev1` only. `Seed-ProductionKeyVault.ps1` + `Configure-ProductionAppSettings.ps1` are aspirational-not-active until a real prod exists; sweep concerns around them reduce accordingly. Update the constraint-file replacement text (§EDITs below) to say "prong 3 applies to `spaarkedev1` and any as-yet-unprovisioned Model 2 stamp; H4 executor MUST NOT provision new Model 2 stamps under this prong until A36-A42 land per Q6."
3. **Q1 discharge reply skipped**: no reply to auth-v4 required. Their §10.6 "still open" internal drift stays as their historical artifact. The commit landing this resolution will suffice as record for cross-worktree audit.

## Owner sign-off record

- **Date**: 2026-08-25
- **Owner**: ralph.schroeder@hotmail.com (session-verified per `userEmail` context)
- **Path chosen**: Hybrid — Path C for `BFF-API-ClientSecret` (pivot to comply with ADR-028 A4-as-amended) + narrow Path A rider on prong 2 (purge-protection of soft-deleted rollback copies through 2026-11-23) + time-boxed Path A for `Dataverse-ClientSecret` (sunset 2026-11-23, prong-3-scoped to `spaarkedev1` per Q7)
- **Concurrence trail**: `auth-v4-integration-open-questions.md` resolution table Q3 disposition
- **Application gates**: EDITs 1-4 + companion sweep fire post-A35 master merge (they cite the ADR-028 A4 amendment which is absent from this worktree pre-merge); main-session-only per Sub-Agent Write Boundary (root §3)

## Executive summary

The project-wide BINDING rule "NEVER delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret`" now protects, in half its scope, a secret that **no longer exists**: auth-v4 closed ADR-028 Exception E-3 on 2026-08-24 and deleted `BFF-API-ClientSecret` (both casings) from dev Key Vault — exactly the supersession this project's own spec FR-39 pre-authorized. Enforcing the rule as written now *misdirects* provisioning (H4 / rotation / seeding treat the secret as mandatory estate and would re-seed it into secret-free environments, which either refuse to boot under `RequireSecretFreeIdentity=true` or silently mask broken FICs), while the actual live hazards — re-creation, sentinel-writing, purging the rollback copies — go unpoliced. Resolution: **Path C** (pivot to comply with ADR-028-as-amended) for `BFF-API-ClientSecret`, **plus a narrow, time-boxed Path A** exception protecting the rollback copies and the still-live `Dataverse-ClientSecret` through the auth-v4 soak window (sunset **2026-11-23**). Exact replacement text for every inheritor is below.

---

## 🔔 ADR Conflict — Resolution Required

### ADR in question

**ADR-028 (Spaarke auth architecture), Amendment A4 (2026-08-17) + Exception E-3 (CLOSED 2026-08-24)** — versus the r1/r3-handoff BINDING never-delete carried in root `CLAUDE.md` §17, `spec.md:259` / `:275`, `.claude/constraints/provisioning.md:27-36`, and ~7 further inheritors (full sweep table below).

A4 (verbatim, from `origin/master:.claude/adr/ADR-028-spaarke-auth-architecture.md`):
> "MUST NOT call `.WithClientSecret(...)` for any client authenticating as the BFF identity. E-3 is closed (2026-08-24) and its site list is empty."

E-3 closure banner (verbatim, same file):
> "E-3 IS CLOSED. THE SECRET IS GONE. DO NOT CITE THIS EXCEPTION FOR NEW CODE. … Removed: App settings `API_CLIENT_SECRET`, `AzureAd__ClientSecret`, `Dataverse__ClientSecret`, `AgentToken__ClientSecret` (2026-08-24 16:50:25Z); Key Vault `BFF-API-ClientSecret` + `bff-api-client-secret` (2026-08-24 17:14:40Z; soft-deleted, recoverable to 2026-11-22 — not purged)."

⚠️ NOTE: **neither A4 nor the E-3 closure exists in THIS worktree's copy of ADR-028** — verified 2026-08-25: commits `dee3df03c` / `39b2bda38` are NOT ancestors of HEAD `45e14556a` (branch is 281 commits behind `origin/master`; last master merge 2026-08-15). The master merge (punch row A35) is a **prerequisite** for applying this resolution.

### Specific rule being challenged

1. **`.claude/constraints/provisioning.md:27-36`** (verbatim, read 2026-08-25):
   ```
   ## KV secret never-delete list — BINDING per root CLAUDE.md §10 + spec.md MUST

   **NEVER delete these KV secrets** from any KV in any code path, script, or handler:

   - `Dataverse-ClientSecret` — legacy but still referenced; deletion breaks BFF auth path.
   - `BFF-API-ClientSecret` — active BFF client-cred.

   Additionally, any secret with `never_delete: true` in `scripts/canonical-secret-catalog/manifest.yaml`
   MUST NOT be deleted regardless of context (test cleanup, sweep, "temporary" removal).

   `§7.9 pre-check gate` — BINDING per spec.md FR-35: BEFORE any secret rename/delete, verify LIVE
   App Service + KV + Dataverse-persisted config for references. Skipping the pre-check is a HARD violation.
   ```
   Drift note: the header cites "root CLAUDE.md §10", but root CLAUDE.md **§10 (BFF Hygiene) contains no never-delete rule** — the only root occurrence is the §17 `/provision-environment` pointer row. The mis-citation is itself drift and is corrected by these edits.

2. **Root `CLAUDE.md` §17** (`/provision-environment` pointer row, fragment, verbatim): "`BINDING: never delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret`.`"

3. **`spec.md:259`** (verbatim, verified 2026-08-25): "`- ✅ **MUST NOT** delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret` (BINDING pre-check per r3 handoff — OBO + shared-lib Dataverse still depend)`"

4. **`spec.md:275`** (tail clause, verbatim, verified 2026-08-25): "`but **MUST NOT** delete the `Dataverse-ClientSecret` KV secret itself (the BFF shared-lib path still consumes it until NG1 #3b; BINDING never-delete)`"

### Conflict — what changed and why the rule as written is now stale

| Date | Event | Effect on the rule |
|---|---|---|
| 2026-08-17 | ADR-028 **Amendment A4** lands on master (auth-v4, Path B under §6.5): BFF-identity confidential clients MUST be secret-free (MI-FIC default / KV cert fallback); `.claude/constraints/auth.md:108`'s false "OBO requires secret" premise corrected | The rule's *rationale* ("OBO … still depend") becomes conditional on migration state |
| 2026-08-19 | r1's own **spec FR-39** (spec.md:207) pre-authorizes the supersession: "auth-v4 retires the secret at its Phase 5, at which point the never-delete rule for `BFF-API-ClientSecret` specifically is superseded by auth-v4's own retirement runbook" | The sunset clause is now part of r1's own MUST set |
| 2026-08-24 14:51Z | MI-FIC cutover on `spaarke-bff-dev`; zero secret-based sign-ins since (auth-v4 `mi-proof-dataverse-side.md`) | "OBO still depends" is empirically false for dev |
| 2026-08-24 16:50–17:14Z | **E-3 CLOSED** (task 033): 4 app settings removed; KV `BFF-API-ClientSecret` + `bff-api-client-secret` **deleted** (soft-deleted to 2026-11-22, not purged). `Dataverse-ClientSecret` KV secret deliberately **NOT** deleted | The FR-39 sunset clause **fired**. Half the rule now protects a nonexistent object |
| 2026-08-25 | §10 addendum + CORRECTION: live contract is secret-free settings (`Graph__Credentials__Order__0=ManagedIdentityFederated` sole entry; `RequireSecretFreeIdentity=true` fail-fast) | Re-seeding the secret is now the hazard, not deleting it |

**Shared-lib status (disputed during analysis; RESOLVED by direct code read 2026-08-25)**: `origin/master`'s `DataverseServiceClientImpl.cs` / `DataverseWebApiService.cs` ARE migrated (MI branch + ordered secret-free credential; the `AuthType=ClientSecret` connection string was replaced at auth-v4 task 022). **THIS branch's copies are NOT** — `DataverseServiceClientImpl.cs:41-65` here still builds the raw `AuthType=ClientSecret` string requiring `API_CLIENT_SECRET`. This is branch staleness (281 commits), not a live contradiction on master — but it means (a) the merge is prescriptive step 0, and (b) any environment deployed from THIS branch's server code still consumes the secret contract (covered by prong 3 below).

### Proposed path

**Hybrid, per-secret:**

- **`BFF-API-ClientSecret` → Path C (pivot to comply)** with ADR-028-as-amended. The amendment already exists (A4, executed as Path B by auth-v4 2026-08-17); r1's FR-39 pre-authorized the supersession, which fired at task 033. Authoring a second amendment would fork authority. The rewritten constraint's prong 1 (never create/seed/restore in secret-free environments; H4 omits — no sentinel) is **not a new rule** — it restates A4's own MUST NOT in provisioning-surface terms. The one genuinely new element — prong 2's purge-protection of the *soft-deleted rollback copies* to 2026-11-23 — is a **narrow Path A rider** (time-boxed, documented here).
- **`Dataverse-ClientSecret` → Path A (project-scoped, time-boxed exception, sunset 2026-11-23)**. The general rule (secret-free BFF identity) is correct; this project retains a narrow never-delete because: (i) E-3's deletion list deliberately excluded this secret — it still exists in KV; (ii) it is now effectively auth-v4's live rollback copy during the soak window (obligation 051-E; rollback proven config-only in auth-v4 decisions/031 §5.6); (iii) unmigrated environments (and any environment deployed from this branch's stale server code) may still resolve it — premature deletion produces the Δ4-class failure (unresolvable KV-ref → literal-string credential or exit-134 site abort). ⚠️ Caveat on (ii): the config-only rollback proof was demonstrated on an existing slot pair already carrying `keyVaultReferenceIdentity`; a freshly created slot does NOT inherit that site property (§10.2), so the rollback copy's usefulness on a new slot requires re-asserting the property first (see punch row A40's slot-persistence criterion).

### Rationale — concrete failure modes each path avoids

- **Path C avoids (keep-the-blanket-rule failures)**: H4 / `Rotate-Secrets.ps1` / `Seed-ProductionKeyVault.ps1` treating the secret as mandatory estate re-seed it → fresh secret-free environments **refuse to boot** (`RequireSecretFreeIdentity=true` is fail-fast by design), or — if the order is loosened — a secret sitting *beneath* MI-FIC **silently absorbs a broken FIC** with every health signal green (master `auth.md`: strictly worse than no migration; same trap class as the §10.5 `Deploy-AllIndexes` silent admin-key re-mint). H4 writing a sentinel instead fails opaquely with `AADSTS7000215` (§9.1). Meanwhile adr-check/reviewers enforce protection of a deleted object.
- **Path A avoids (over-correct-to-"rule-is-dead" failures)**: a provisioning sweep or cleanup deletes the still-live `Dataverse-ClientSecret` or purges the soft-deleted copies — destroying auth-v4's proven rollback path mid-soak and breaking any unmigrated environment whose KV-reference resolves it (exit-134 class per addendum Δ4).
- **Both avoid (do-nothing failure)**: this worktree ships Wave G-3+ work against a local ADR-028 with no A4 and an `auth.md` still asserting the disproven "OBO requires a secret" — the exact wrong-end-reasoning failure auth-v4 documented as surviving three prior audits.

### §9.2 contingency (fourth lifecycle case — flagged, not resolved here)

If open question **Q2** (Model 2 customer-owned-tenant FIC issuer, auth-v4 §9.2) were resolved as reading (b), that shape cannot go secret-free via MI-FIC. **Even then this resolution stands**: A4's standing guard mandates the fallback is a **KV certificate, never a client secret** — so `BFF-API-ClientSecret` does not revive under any §9.2 outcome. The cert path is, however, unbuilt ("dropped, not deferred") — see Q2's consequences in the open-questions doc. Structural evidence and shipped code (verified: `GraphAppRegistrationProvisioner.cs:547-557` derives issuer per profile — stamp's own tenant) strongly indicate reading (a).

### Impact if accepted

Constraint file + ~9 inheritors rewritten per the package below; **no code change**; H4 behavior contract confirmed (omit, no sentinel — closes the H4 half of punch row A30's open sentinel contract; the **H7/task-142 half is explicitly booked in punch row A38**, not silently absorbed); E-1 per-customer SpeAdmin secrets untouched (open, architectural, no sunset); 2026-11-23 review diaried, coordinated with auth-v4 obligation 051-E. Sunset-date note: soft-delete recovery runs to 2026-11-**22**, obligation 051-E cites 2026-11-**23** — this record uses 11-23 as the outer bound; confirm with auth-v4 (open question Q3).

### Alternatives considered (and rejected)

| Alternative | Why rejected |
|---|---|
| Keep the blanket never-delete unchanged | Protects a nonexistent object; misdirects reviewers; licenses re-seeding into secret-free envs (boot-refusal / silent-FIC-mask failures above) |
| New ADR amendment (Path B) | A4 already covers it; a second amendment forks authority over the same rule |
| Full Path C for `Dataverse-ClientSecret` too | Would license deleting the live rollback copy mid-soak + breaking unmigrated KV-references (exit-134 class); retirement belongs to auth-v4's runbook, not a provisioning sweep |
| Pure Path A (freeze both rules as exceptions) | Freezes a rule whose object no longer exists while the real hazard (re-creation) goes unpoliced |

---

## Exact draft text (apply AFTER the A35 master merge; `.claude/**` targets are MAIN-SESSION-ONLY per root CLAUDE.md §3)

### EDIT 1 — `.claude/constraints/provisioning.md`: REPLACE lines 27-36 entirely with:

```markdown
## KV credential lifecycle — BINDING per ADR-028 Amendment A4 + E-3 closure (§6.5 resolution 2026-08-25; supersedes the r3-handoff never-delete list)

> **History**: the r3-handoff blanket rule "NEVER delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret`" was
> superseded on 2026-08-24 when auth-v4 task 033 closed ADR-028 Exception E-3 — exactly the supersession
> pre-authorized by this project's spec.md FR-39. E-3 closure facts: app settings `API_CLIENT_SECRET` /
> `AzureAd__ClientSecret` / `Dataverse__ClientSecret` / `AgentToken__ClientSecret` removed 2026-08-24 16:50:25Z;
> KV `BFF-API-ClientSecret` + `bff-api-client-secret` deleted 17:14:40Z (**soft-deleted, recoverable to
> 2026-11-22 — not purged**). `Dataverse-ClientSecret` was deliberately NOT deleted. Resolution record:
> `projects/customer-provisioning-orchestration-r1/notes/decisions/adr-028-a4-integration-conflict-resolution.md`.

**1. NEVER create, seed, restore, or re-introduce `BFF-API-ClientSecret` (either casing — `bff-api-client-secret`
included) in any secret-free environment.** Secret-free = `spaarke-bff-dev` (flipped 2026-08-24) and EVERY
newly-provisioned environment on the secret-free contract (`Graph__Credentials__Order__0=ManagedIdentityFederated`
as the ONLY entry + `Graph__Credentials__RequireSecretFreeIdentity=true`). H4 **omits** the secret entirely —
**no sentinel value** (the ordered selector cannot distinguish a sentinel from a real secret and fails opaquely
with `AADSTS7000215`; positive migration markers go in a provisioning-state field or KV tag, never the credential
slot — auth-v4 §9.1). A `.WithClientSecret(...)` site on the BFF identity is a plain ADR-028 A4 violation — E-3
is closed; there is no exception to cite. The FR-39 credential-type seam in H3/H4 stays in code (pluggability),
but the secret path may only be selected for a prong-3 unmigrated environment — never for new provisioning.

**2. NEVER purge or delete the rollback copies before 2026-11-23** (Path A, time-boxed): do not purge the
soft-deleted `BFF-API-ClientSecret` / `bff-api-client-secret` KV entries, and do not delete the still-live
`Dataverse-ClientSecret` KV secret. Its old rationale is stale (the shared-lib consumer is migrated on master),
but it is auth-v4's live rollback copy during the soak window (obligation 051-E; rollback proven config-only,
decisions/031 §5.6 — NOTE: proven on a slot pair already carrying `keyVaultReferenceIdentity`; a fresh slot
needs that site property re-asserted first). Retirement belongs to auth-v4's runbook — never a provisioning
sweep, test cleanup, or "temporary" removal. **Sunset 2026-11-23**: auth-v4 retires it or the owner re-reviews;
do not silently extend.

**3. Unmigrated environments — the original rule survives unchanged**: for any environment whose LIVE credential
order still contains `ClientSecret` (rollout is per-environment; only dev is flipped as of 2026-08-25 — and any
environment deployed from pre-merge branch server code), the original never-delete for BOTH secrets + the FR-35
pre-check gate remain fully in force until auth-v4's retirement runbook executes there.

**4. E-1 secrets are OUT OF SCOPE and stay protected indefinitely**: per-customer SpeAdmin container-type secrets
(ADR-028 E-1 — open, architectural, unaffected by A4/E-3) authenticate OTHER applications, not the BFF identity.
`sprk_specontainertypeconfig` rows + their KV secret names keep `never_delete: true` with no sunset.

Additionally, any secret with `never_delete: true` in `scripts/canonical-secret-catalog/manifest.yaml` MUST NOT
be deleted regardless of context. The manifest's two BFF-identity entries are re-annotated per this resolution;
until re-annotation lands, read their `never_delete: true` as prongs 1-3 above, not the retired blanket rule.

`§7.9 pre-check gate` — BINDING per spec.md FR-35, UNCHANGED: BEFORE any secret rename/delete, verify LIVE
App Service + KV + Dataverse-persisted config for references. Skipping the pre-check is a HARD violation.
```

### EDIT 2 — root `CLAUDE.md` §17 (`/provision-environment` row) — replace the never-delete fragment

- **BEFORE** (fragment): `BINDING: never delete \`Dataverse-ClientSecret\` / \`BFF-API-ClientSecret\`.`
- **AFTER** (fragment): `BINDING credential-lifecycle rule (rewritten 2026-08-25 per ADR-028 A4 / E-3 closure, §6.5 resolution): never CREATE/seed/restore \`BFF-API-ClientSecret\` (either casing) or \`Dataverse-ClientSecret\` in secret-free environments (H4 omits — no sentinel); never purge the soft-deleted rollback copies or delete the live \`Dataverse-ClientSecret\` before 2026-11-23 (auth-v4 owns retirement); the original never-delete survives only for environments still carrying \`ClientSecret\` in their live credential order; E-1 SpeAdmin secrets protected indefinitely. Full rule: \`.claude/constraints/provisioning.md\` §KV credential lifecycle.`
- Grep-verified: this §17 row is root CLAUDE.md's ONLY never-delete occurrence — every "per root CLAUDE.md §10" citation in the inheritors is drift, corrected by these edits.

### EDIT 3 — `spec.md:259` (verbatim BEFORE verified 2026-08-25)

- **BEFORE**: `- ✅ **MUST NOT** delete \`Dataverse-ClientSecret\` / \`BFF-API-ClientSecret\` (BINDING pre-check per r3 handoff — OBO + shared-lib Dataverse still depend)`
- **AFTER**: `- ✅ **MUST** follow the KV credential-lifecycle rule (rewritten 2026-08-25 per ADR-028 A4 + E-3 closure, §6.5 resolution — supersedes the r3-handoff never-delete whose FR-39 sunset clause fired at auth-v4 task 033): never CREATE/seed \`BFF-API-ClientSecret\` (either casing) or \`Dataverse-ClientSecret\` in secret-free environments (H4 omits — no sentinel, per auth-v4 §9.1); never purge/delete the rollback copies before 2026-11-23; the original never-delete survives only for unmigrated environments. Full rule: \`.claude/constraints/provisioning.md\``

### EDIT 4 — `spec.md:275` tail clause (verbatim BEFORE verified 2026-08-25)

- **BEFORE** (tail): `but **MUST NOT** delete the \`Dataverse-ClientSecret\` KV secret itself (the BFF shared-lib path still consumes it until NG1 #3b; BINDING never-delete)`
- **AFTER** (tail): `but **MUST NOT** delete the \`Dataverse-ClientSecret\` KV secret itself before 2026-11-23 (rationale corrected 2026-08-25: the shared-lib consumer is migrated on master — #3b/task-022 landed and E-3 closure removed the \`Dataverse__ClientSecret\` app setting 2026-08-24; the secret is retained solely as auth-v4's rollback copy through its soak window — §6.5 resolution, Path A)`

### Companion edits (same pattern — replace blanket never-delete + stale rationale with pointer to EDIT 1's section)

| File:line | Current stale text (gist) | Action |
|---|---|---|
| `.claude/skills/provision-environment/SKILL.md:63` + `:1068` | "MUST NEVER delete… still consumed by OBO flow" | Replace with pointer to constraint §KV credential lifecycle (the "still consumed by OBO" rationale is false since 2026-08-24) |
| `.claude/patterns/provisioning/manifest-driven-secret-catalog.md:27` + `:48` | blanket never-delete, "no exceptions, ever" | Replace with 4-prong summary + pointer; the "no temporary test deletes" spirit survives as prongs 2-4 |
| `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md:391` | "NEVER delete… (OBO + shared-lib Dataverse still depend)" | Replace rationale + pointer |
| `.claude/skills/azure-deploy/SKILL.md:100` | "`BFF-API-ClientSecret` … retained for OBO only" | Mark deleted 2026-08-24 (E-3 closed); soft-delete recoverable to 2026-11-22 |
| `scripts/canonical-secret-catalog/manifest.yaml` (~:106-109, ~:130-133) | `never_delete: true` + stale exception_notes | KEEP `never_delete: true` on both; rewrite exception_notes: `Dataverse-ClientSecret` → "rollback-window hold per §6.5 resolution, sunset 2026-11-23, retirement owned by auth-v4 runbook"; `BFF-API-ClientSecret` → "DELETED from KV 2026-08-24 (E-3 closed) — entry retained to block RE-CREATION; never seed into secret-free envs". Then run `Invoke-CatalogGenerator.ps1 -Verify` → exit 0 |
| `design.md:804`, `:860`, `:1232` | "NEVER-REMOVE per r3 handoff… pending #3b" | ANNOTATE each ("superseded 2026-08-25 per §6.5 resolution — see spec.md MUST + constraint file"); do not rewrite design history |
| `docs/standards/oauth-obo-patterns.md` + `src/server/api/Sprk.Bff.Api/CLAUDE.md` | local copies carry pre-correction "OBO requires secret" | **NO local edit needed** — verified 2026-08-25: master's copies carry the correction ("That was wrong, and it was load-bearing"); the A35 merge cures both. Post-merge, grep-confirm |
| `.claude/constraints/provisioning.md:~112` ("Client-cred rotation cadence: ≤ once per 90 days") | stale post-A4 for the BFF identity (rotation retired) | Minor follow-up edit, out of this section's scope — fold into A44(b) doc sweep |

---

## Reviewer action requested

1. **Choose or refine the path** (root §6.5): confirm the C + time-boxed-A hybrid above, per secret.
2. **Confirm the sunset date** governing prong 2: 2026-11-23 (051-E) vs 2026-11-22 (soft-delete recovery) — see open question Q3.
3. **Approve the edit package** (EDITs 1-4 + companion sweep) for main-session application AFTER the A35 master merge.
4. **Note the §9.2 contingency**: if Q2 resolves as reading (b), the fallback is a KV certificate (A4 standing guard) — this resolution does not need reopening, but the cert-provisioning work does.

**Owner sign-off**: ______________________ (date) — per root §6.5 the human reviewer chooses or refines the path.
