# Auth-v4 Integration Remediation Plan — customer-provisioning-orchestration-r1

> **Date**: 2026-08-25
> **Deliverable**: 1 of 4 (auth-v4 change-request response, with 2026-08-25 addendum)
> **Source doc**: auth-v4 canonical `PROVISIONING-CHANGE-REQUEST.md` (626-line copy in the `spaarke-auth-v4-dataverse-MI` worktree — incl. §5.1 DECIDED block, §9 replies, §10 addendum Δ1-Δ5, §10 DELIVERED, §11 live invariants, 2026-08-25 CORRECTION). r1's local mirror (280-line) is STALE and superseded per §6.
> **Companions**:
> - [`decisions/adr-028-a4-integration-conflict-resolution.md`](decisions/adr-028-a4-integration-conflict-resolution.md) — formal §6.5 record (never-delete BINDING)
> - [`auth-v4-integration-draft-punch-rows.md`](auth-v4-integration-draft-punch-rows.md) — punch rows A35-A44, copy-paste-ready
> - [`auth-v4-integration-open-questions.md`](auth-v4-integration-open-questions.md) — Q1-Q11 in §6 escalation format
> **Depends on**: `notes/task-202-punch-list.md` · `spec.md` v3.6 · `design.md` v3.6 · `notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md` (2026-08-19)
> **Verified against live state 2026-08-25**: HEAD `45e14556a` (281 behind / 273 ahead of `origin/master`); auth-v4 tip `ef61a5f5a` (1 commit unmerged); direct reads of `GraphAppRegistrationProvisioner.cs`, `DataverseServiceClientImpl.cs` (both branches), `origin/master:scripts/Register-EntraAppRegistrations.ps1`, `.claude/constraints/provisioning.md:27-36`, `spec.md:259/:275`, `task-202-punch-list.md`.

---

## Executive Summary

Auth-v4 is **COMPLETE and merged to master** (PRs #814/#816/#817/#818; sole unmerged tip `ef61a5f5a`). Its change request + addendum land on r1 as five workstreams:

1. A **§6.5 conflict resolution** retiring the stale half of the never-delete BINDING — the FR-39 sunset clause fired 2026-08-24 when E-3 closed and `BFF-API-ClientSecret` was deleted from KV (§2).
2. **Two topology items** — §5.1 is DECIDED (Reading 1, ratifying r1's own 2026-08-19 split; only discharge + drift cleanup remain) and §9.2 is genuinely OPEN (owner ratification of reading (a) required — Q2) (§3).
3. **10 punch rows A35-A44** (28h; 8 block task 186 E2E) (§4).
4. A **silent-failure surface catalog** — the change request's own traps plus newly-found surfaces, provenance-marked (§5).
5. A **merge-shaped coordination protocol** — everything auth-v4 shipped reaches r1 through ONE event, the A35 master merge, and r1 is the resolver for every conflict (§6).

**Recommended sequence** (detail in §7):

| Step | What | Gate |
|---|---|---|
| S0 | Owner decisions: **Q2** (§9.2 = reading (a)) + **Q3** (§6.5 sign-off + sunset date) + **Q9** (186 stamp shape) | none — do first |
| S1 | **A35 master merge** (FULL rigor, prescriptive, main session; freeze broadcast per Q10 first) | gates all `.claude/` + script + handler work |
| S1∥ | **203b-2 mini-wave**: A36, A37, A40 (parallel-safe bicep/verify; no merge dependency) | none — dispatch now |
| S2 | **Task 205**: A38 ∥ A42 → A39; A41/A43/A44 parallel after A35 | A35 |
| S3 | Main-session doc/constraint edits: §6.5 EDIT package + topology cascade + mirror refresh | A35 + Q3 sign-off |
| S4 | Discharge reply to auth-v4 (via `auth-v4-coord-response`) | S0, S3 |
| S5 | Task 186 E2E live-fire | A35-A42 landed + Q9 confirmed |

**Decision list (owner)**: Q2 (critical) · Q3, Q6, Q9 (high) · Q4, Q5, Q10, Q11 (medium) · Q1, Q7, Q8 (low/verify).
**Estimated effort**: ~28h agent + 5-6h main session/owner; critical path to 186 = **11h serial** (§8).

---

## §1 Context — what changed 2026-08-17 → 2026-08-25 and why it shaped this plan

| Date | Event | Consequence for r1 |
|---|---|---|
| 2026-08-17 | ADR-028 **Amendment A4** lands on master (secret-free BFF-identity confidential credential — MI-FIC default / KV-cert fallback; `auth.md:108`'s false "OBO requires secret" premise corrected). **This worktree's last master merge is 2026-08-15** — A4 never arrived here | This branch's ADR-028 / auth.md / adr-check are pre-A4; any auth finding produced here is unreliable until A35 lands |
| 2026-08-19 | r1's owner-signed response applies the Model 1/Model 2 split (FR-39/FR-40, spec:253, design:60, R23 CLOSED); auth-v4's §9 accepts "as applied" | §5.1 was effectively answered by r1 six days before the DECIDED block existed |
| 2026-08-21 | Auth-v4 §10 DELIVERED: `-FicOnly` FIC automation (task 030) with exit codes 0/1/2, triple-keyed idempotency, **measured** error codes (AADSTS**70025** propagation — NOT the documented 70021; **700213** wrong-subject), and the `Assert-SpaarkeFicTenancy` cross-tenant refusal. §9.2 raised: customer-tenant FIC issuer | r1's task 130 later lands its own C# FIC path — the §9.3-anticipated fallback — so a reconciliation is owed (A42) |
| 2026-08-24 | Tasks 051/053 cut dev over; **E-3 CLOSED** (task 033): 4 secret app settings removed 16:50Z; KV `BFF-API-ClientSecret` + lowercase alias deleted 17:14Z (soft-deleted to 2026-11-22); `Dataverse-ClientSecret` deliberately retained; live contract = `Graph__Credentials__Order__0=ManagedIdentityFederated` sole entry + `RequireSecretFreeIdentity=true` | FR-39's sunset clause **fired** — the never-delete BINDING is now half-stale (§2); the Δ1-Δ5 addendum follows from the same cutover |
| 2026-08-25 | §5.1 **DECIDED (owner)**: Reading 1. §10 **CORRECTION**: 2 of 5 deltas already folded into `auth-deployment-setup.md` by task 033's sweep — genuinely new work is Δ1-Δ3. §10.6 carries same-day internal drift (still says §5.1 open). Auth-v4 merges to master; project complete | The change request is consumed via the A35 merge, not PR coordination; r1 is the sole conflict resolver |

Two cross-cutting facts shaped every section below:

- **(a) Branch staleness vs live defect.** This branch is 281 commits behind master. Several "gaps" flagged during analysis — an unmigrated `DataverseServiceClientImpl`, a missing `-FicOnly` mode, a missing A4 — are **branch staleness, cured by A35**, not live master defects. Verified by direct reads: master's `DataverseServiceClientImpl.cs` has the MI branch + secret-free ordered credential (task 022 replaced the `AuthType=ClientSecret` connection string); master's script has `-FicOnly` (16 occurrences) AND `-SkipClientSecret` (4 — both flags real, coexisting); master's ADR-028 carries A4 + the E-3 closure banner.
- **(b) Create-success ≠ first-use-success is the DEFAULT on this estate.** Entra validates nothing at FIC create; App Service passes unresolvable KV refs to the app as literal strings; on this codebase a status code never establishes an outcome (error-open endpoints convert Graph 404s to `200 {"items":[]}`). This is why §5's catalog and the exchange-verification obligations exist, and why every "applied" claim in this plan cites its verification method.

## §2 ADR Conflict Resolution

Formal record: [`decisions/adr-028-a4-integration-conflict-resolution.md`](decisions/adr-028-a4-integration-conflict-resolution.md) — filed per root CLAUDE.md §6.5, awaiting owner sign-off (Q3). Summary of the resolution:

- **`BFF-API-ClientSecret` → Path C** (pivot to comply with ADR-028-as-amended). The secret is GONE (E-3 closed 2026-08-24); r1's spec FR-39 pre-authorized exactly this supersession and the clause fired at auth-v4 task 033. Authoring a second amendment would fork authority over the same rule. The rewritten constraint's prong 1 ("never create/seed/restore in secret-free envs; H4 omits — no sentinel") is a restatement of A4's own MUST NOT in provisioning-surface terms, not a new rule.
- **Narrow Path A rider on prong 2**: purge-protection of the soft-deleted rollback copies through the soak window — the one genuinely new, time-boxed element, classified honestly per the adversarial verifier's demand.
- **`Dataverse-ClientSecret` → Path A** (project-scoped, time-boxed, sunset 2026-11-23): still exists in KV (E-3's deletion list deliberately excluded it); now auth-v4's live rollback copy (obligation 051-E; rollback proven config-only per decisions/031 §5.6 — with the Δ4 caveat that the proof ran on a slot pair already carrying `keyVaultReferenceIdentity`, so a fresh slot needs the site property re-asserted first); unmigrated environments may still resolve it.
- **Disputed claim RESOLVED by direct read** (the verifier's central refutation): master's shared-lib Dataverse clients ARE migrated; THIS branch's copies are raw ClientSecret (`DataverseServiceClientImpl.cs:41-65` here builds `AuthType=ClientSecret;...`). The change request's §2.1 is true on master; the contradiction was branch staleness. Consequence: A35 is prescriptive step 0, and environments deployed from this branch's server code fall under prong 3 (unmigrated) until then.
- **§9.2 contingency (fourth lifecycle case)**: even under reading (b), A4's standing guard mandates the KV **certificate** fallback — never the client secret — so no §9.2 outcome revives the deleted secret. (Reading (b) would, however, reopen the unbuilt cert-provisioning estate — see Q2.)
- **Application mechanics**: EDITs 1-4 + companion sweep (exact before/after in the record) are main-session-only (`.claude/` write boundary, root §3) and land AFTER A35, because the replacement text cites an amendment absent from this worktree. EDIT 3/4 before-texts verified verbatim at `spec.md:259`/`:275` this session. `docs/standards/oauth-obo-patterns.md` + BFF `CLAUDE.md` need NO local edit — master's corrections were verified present ("That was wrong, and it was load-bearing"); the merge cures both; post-merge grep-confirm.
- **E-1 untouched**: per-customer SpeAdmin container-type secrets authenticate other applications, not the BFF identity — protected indefinitely, no sunset.

## §3 Topology Decisions

### §3.1 Model 1 (§5.1) — DECIDED; discharge, don't re-decide

Reading 1 (one shared multitenant app-reg; zero per-customer app-reg/FIC objects for Model 1) was applied by r1 on 2026-08-19 and ratified by the owner in the canonical doc on 2026-08-25. Standing evidence, all previously verified in-estate:

- `spec.md:253` — the v3.5 MUST split (Model 1 shared app-reg / Model 2 per-customer).
- `design.md:60` — D2 corrected (original "per-customer in both models" struck through).
- `design.md:1083-1085` — §9.1 v3.5 tenancy note (the "§5.2 doc fix"), superseding the contradictory v3 sentence.
- `spec.md:207/:208` — FR-39 (pluggable credential seam) + FR-40 (invariant I6, Model 1 only).
- `design.md:1528` — R23 CLOSED (20-FIC cap counts credentials on the app-reg; every shape trusts exactly one UAMI — closure holds under either §9.2 reading, unconditionally).

The DECIDED block's residual edit ask cites 2026-08-19 line numbers (`spec.md:236` / `design.md:57`) — those edits already exist at today's lines 253/60, and the same doc's §9 accepts them "as applied." **Action (S4)**: discharge reply + ratification cites appended to spec:253 / design:60 + ask auth-v4 to fix §10.6's same-day drift ("§5.1 still open"; also the flag naming — both `-FicOnly` and `-SkipClientSecret` are real, with `-FicOnly` the consumption contract).

Under Reading 1 the code-level isolation boundary is I6 + I5: the shared UAMI can mint an assertion for ANY trusting app-reg, so per-tenant request-context routing is the only wall. Task 130's I6 enforcement (explicit `tenancyModel`, no default; Model 1 → `ProvisionCallCount==0`, 2 dedicated tests) is landed per its completion notes. ⚠️ CONFIRM: the fuller three-predicate description of `I6_ObAppRegDerivationTests` circulating in analysis was not bundle-corroborated — treat the test's exact assertions as unverified until read.

### §3.2 Model 2 customer-tenant FIC issuer (§9.2) — OPEN; owner ratification required

🔔 **Human Input Required** — full escalation format in [open questions Q2](auth-v4-integration-open-questions.md). Compressed:

- **Situation**: does a customer-owned Model 2 stamp's app-reg federate (a) its OWN stamp UAMI (same tenant), or (b) the shared Spaarke UAMI (cross-tenant)? r1's own 2026-08-19 TL;DR used (b)-phrasing — the ambiguity that triggered §9.2 — while the shipped code implements (a). **Verified by direct read this session**: `GraphAppRegistrationProvisioner.cs:547-557` derives issuer per profile (`customer-owned-model2` → `request.TenantId`, else `SpaarkeTenantId`); header `:73-78` states the per-profile recipe (issuer = the tenant where the stamp UAMI lives); subject = stamp-UAMI principalId from H2a's `uami.bicep` via `InterStepState.MiObjectId`.
- **Structural evidence for (a)**: (b) is a cross-tenant (app-reg, UAMI) pair — unsupported by Entra's same-tenant FIC rule; and independently, customer-tenant compute cannot mint assertions as a Spaarke-tenant UAMI (managed identities are tenant-bound). ⚠️ Honesty caveats (adversarial-verifier corrections adopted): TENANCY-AND-CREDENTIALS §3 row 3 *assumes* (a) — it restates rather than corroborates; task 130 shipped before §9.2 was formally answered and has never been exchange-verified end-to-end (L2 cannot mint the assertion); the topic is auth-sensitive, so §6 escalation applies regardless of evidentiary strength.
- **Cost of (b), quantified**: MI-FIC becomes structurally impossible for that shape; A4's standing guard mandates the KV-certificate path ("dropped, not deferred") — per-stamp cert issuance + renewal automation, the unexercised ordered-provider middle tier, H3/H4 cert branches, T4 probe changes: effectively the retired rotation lifecycle re-instantiated with certificates, purchasing nothing (a) doesn't provide.
- **Recommendation**: ratify **(a)**; port the cross-tenant refusal guard either way (see §5 item 1 — under (a) it is inert protection; under (b) it correctly converts a silent production failure into a loud provisioning-time refusal).

### §3.3 Textual cascade (rides S3, after A35)

1. Purge the four stale "FIC trusting the shared BFF UAMI" Model-2 phrasings: `spec.md:207` FR-39 (×2 occurrences), `design.md:146` H3 row, `H3EntraAppRegHandler.cs` header comment + `:297` diagnostic string.
2. In the same lines, update `AADSTS70021` → `AADSTS70025 (measured; wrong-subject = AADSTS700213)` — 70021 was never observed live.
3. `design.md:153` H10 row: fix figure drift ("10/14 GUIDs null" vs spec's 11-of-14 +1 = 15; make `GraphAppRoles.cs` the cited source of truth) + add per-model identity sources (Model 1: shared app-reg appId + shared UAMI, objectid = principalId; Model 2: per-customer app-reg + stamp UAMI).
4. Append a §9.2 closure line to spec.md's Unresolved Questions (post-Q2 ratification) + a short decision note in `notes/decisions/`.
5. Append a dated ERRATA block to `notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md` (do not rewrite the 2026-08-19 text): the TL;DR's "trusting the shared BFF UAMI" was imprecise for Model 2; corrected to "the stamp's own UAMI (same tenant as the app-reg)."

## §4 Delta Impact + WBS

Full rows + dedup evidence: [`auth-v4-integration-draft-punch-rows.md`](auth-v4-integration-draft-punch-rows.md). Dedup verdicts (verified against punch list + repo, not assumed):

| Δ | Verdict | Residual scope (row) |
|---|---|---|
| Δ1 SB SAS → MI | **NOT covered** — A20/A13 grant the L2 *provisioning* UAMI (key-extraction on shared source services / its own queue); Δ1 needs the BFF *runtime* UAMI. Different principal, different purpose | A36 (data-plane roles) + A38 (manifest omit) + A39 (FQNS settings) |
| Δ2 Search admin-key → MI | **NOT covered** — same different-principal reasoning | A37 + A38 (✅ verified: `StaticKvSecretManifest.cs:74` still upserts `AiSearch--AdminKey`) + A39 flag |
| Δ3 credential-selection settings | **Total gap** — ✅ verified: 0 of 8 keys in any customer-facing surface (only older `Graph__ManagedIdentity__*` appear, as comments/test-fixture keys) | A39 (H4b `per_env_settings`) with the §10.2 ordering guard |
| Δ4 keyVaultReferenceIdentity | **Provisionally covered** — `ArmAppServiceIdentityPatcher.cs` exists but its body was never read by any reviewer (verifier correction adopted: downgraded from "already covered") | A40: code-read → E2E assert → slot-persistence criterion |
| Δ5 UAMI Dataverse app-user | **Partial** — design intent at `design.md:153`; in-flight agents (`Wave-3E-053-H10-AppUser`, `ds8-uami-dv-appuser`) may have landed pieces | A41: dedupe-first, then dual-row + T2 principalId byte-equality |
| §10 DELIVERED consumption | Task 130's C# path landed — the §9.3 fallback fired; two implementations now exist | A42: FR-C4 reconciliation (Q5) + tenancy-guard port + §11 invariants first-exercise |
| §10.5 traps | Both confirmed live by the change request | A43 (Deploy-AllIndexes gate) + A44 (template guard + doc sweep) |

**Sequencing decision — option (c), split.** Three mechanical rows (A36/A37/A40) are parallel-safe TODAY with no merge dependency (same shape as the applied A20/A21/A25); the rest need A35 and warrant their own FULL-rigor quality gate as task 205. Rejected alternatives: extending 203c (its queue is L2-identity/skill flavored; burying FULL-rigor credential work in a continuation bucket loses its gate); a single serialized 205 (forfeits free parallelism); routing to auth-v4 (§10 is addressed TO provisioning, every landing spot is r1 estate, auth-v4 retained only 051-E and is complete).

**Corrected critical path** (verifier fix applied — A39 now depends on A42, because setting `RequireSecretFreeIdentity=true` before the FIC exists boot-loops a fresh stamp; A38 alone only governs what H4 *omits*, not FIC creation): **A35(3h) → {A38 ∥ A42}(4h) → A39(4h) = 11h serial**, with A36/A37/A40/A41 in parallel lanes.

**§5.1 cascade audit — clean**: no currently-planned r1 row over-specifies a per-customer Model 1 app-reg lifecycle; H10 (A41) and T3 parity (task 178) operate per-environment on the shared app-reg, which is Reading-1-consistent; A41's description pins this explicitly.

## §5 Silent-Failure Surface Catalog

The change request's own catalog (§3.3 silent misconfiguration · §9.1 sentinel pathology · §10.2 fail-fast + site property · §10.4 objectid trap · §10.5 two traps) plus newly-found surfaces. **Provenance-marked** per the adversarial verifier's demand:

- ✅ = verified this session by direct read
- 📄 = bundle-corroborated (a reader quoted the file/doc first-hand)
- ⚠️ = analysis-only citation — requires a fresh file read before being treated as fact

The full 22-entry catalog (SF-1..SF-22) is DRAFT-scoped for `.claude/patterns/provisioning/silent-failure-catalog.md` — a **main-session follow-up** (sub-agent write boundary) that must carry root §11 three-question justification for its new components (T7/T8 probes, manifest-linter class, KV-tag convention) and, for its one BFF-touching mitigation, the §10 bff-extensions checklist (Q11).

### Priority surfaces (act in this order)

1. ✅ **Cross-tenant FIC pair not refused by the C# port** (SF-5). Guard exists only in master's PS script (`Assert-SpaarkeFicTenancy`, 2 occurrences — verified); the C# provisioner creates whatever it's told; failure surfaces at the customer's first OBO, weeks later. → A42 ports the guard into `CreateFic`. Blast-radius honesty (verifier correction): under reading (b) the guard refuses 100% of customer-owned Model 2 runs — *correct* behavior (loud provisioning-time stop vs silent production failure), but the shape is then blocked, not merely "safe."
2. ✅ **L2 cannot exchange-verify the FIC it creates** (SF-4, GOTCHA 2). The Worker runs under L2's own platform UAMI; verification degrades to a re-GET byte-compare against `request.UamiPrincipalId` — garbage-in, garbage-verified. Run reports MUST distinguish "persisted-verified" from "exchange-verified" (mirror the script's exit 0 vs 2); real proof lands post-App-Service in H13/T4; the warmup self-proof is Q11.
3. **Migration-undo vectors in r1's own estate** — every re-deploy or rotation can quietly reverse the auth-v4 migration:
   - ✅ `StaticKvSecretManifest.cs:74` re-upserts `AiSearch--AdminKey` (FromBicepOutput) → A38.
   - 📄 `Deploy-AllIndexes.ps1` silent admin-key fallback ("falling back to live admin key") — confirmed live by §10.5 → A43.
   - ⚠️ `customer.bicep:667/:675` re-seeds admin-key + SAS on every deploy; ⚠️ `Rotate-Secrets.ps1:590/:615/:664` resurrects retired credentials; ⚠️ Bicep `listKeys()` deployment outputs (keys in ARM deployment history) — analysis-only line cites; A38/A43/A44 executors grep-confirm before editing.
   - Gate all of these on a machine-readable secret-free marker (KV tag or provisioning-state field — never a value in the credential slot, §9.1). **Fleet-consistency gap** (verifier addition): under Model 2 the marker must be applied consistently across N per-customer `kv-{customerId}-{secretsVer}` vaults — a missed tag on one vault is itself a silent-skip failure; A38's acceptance criteria must state how that is detected (e.g. T8 profile-coherence probe cross-checks tag vs live settings).
4. ✅ **The four credential-selection settings exist nowhere customer-facing** (SF-17). A fresh customer BFF boots on the secret path; the H3-created FIC is dead weight; a mis-built FIC incubates until cutover — deferred loud failure at the worst time. → A39, under the §10.2 ordering guard ("provision the identity first, then the setting — never the reverse"). **New interaction case** (verifier addition): FIC propagation flaps post-create (~8 failures over ~130s, AADSTS70025) × `RequireSecretFreeIdentity=true` fail-fast — a stamp booting inside the window can fail to START; H4b sequencing must tolerate the window (verified-exchange before settings-apply, or boot-retry allowance).
5. 📄 **Dual app-user objectid trap** (SF-9, §10.4 — auth-v4's "single most-missed item"). The UAMI row's `azureactivedirectoryobjectid` must equal the principalId; a wrong objectid leaves a row that EXISTS, passes count-checks, and 401s every app-only Dataverse call. → A41 extends T2 with byte-equality. ⚠️ The claim that the current probe is count-only is UNCONFIRMED (inferred from test messages "OK (count=1)"/"COUNT=0") — read the verifier before extending; the source analysis contradicted itself here and the catalog entry must carry the caveat.

### Meta-surface and remaining entries

- ✅ **SF-21 — this worktree's governing docs re-teach the retired secret contract**: pre-A4 ADR-028, `auth.md:108` ("OAuth spec requires confidential client + secret" — the sentence whose survival through three prior audits auth-v4 documented), the stale 280-line change-request mirror. Verified nuance: the *code*-level half (shared-lib ClientSecret) is master-cured — the risk is agents and deployments working from THIS branch. A35 + the mirror refresh close it. Treat "knowledge-file drift on auth" as a stop-and-sync trigger for any future task here.
- 📄 **Error-code substring matching** (SF-6): patterns on `AADSTS70021` also match 700211 (wrong issuer) and 700213 (wrong subject) — genuine config faults retried for the whole budget, then the opposite verdict asserted. Retry logic must exact-match numeric `error_codes`; auth-v4 hit and fixed this as code-review critical C1.
- 📄 **Name-based UAMI resolution** (SF-1): 5 UAMIs in the dev subscription; `spaarke-bff-identity` is a decoy not attached to the BFF. ARM resource IDs only, end-to-end; ⚠️ two of our own script help-texts teach `az identity show --name` — fix in A44's sweep.
- 📄 **clientId/principalId/resourceId plumbing chain** (SF-2): three valid-shaped identifiers travel `uami.bicep` → H2a → `InterStepState` → H3/H10/H4; one swapped mapping and everything creates successfully, failing only at exchange (700213) / Dataverse 401 / KV-null. `InterStepState` field NAMES carry the semantic; new consumers cite which field and why.
- ⚠️ **H4b silent optional-skip** (SF-18, `H4bBulkAppSettingsHandler.cs:286`): a load-bearing per-env key mis-flagged `required=false` converts hard failure into silent omission → A39 acceptance: load-bearing keys may not be optional; skipped keys surface in the run record.
- ⚠️ **Model 1 shared app-reg config-trust** (SF-22, `H3EntraAppRegHandler:420-463`): a wrong-but-real app-reg id in `EntraAppReg:SharedBffAppRegistrationId` passes drift checks against the wrong object; shared-KV URI existence is assumed, not probed. Candidate Model-1 acceptance probe — Phase-F scope.
- 📄 **Swap gates trusting health-200** (SF-20): `/healthz` is anonymous and exercises no credential; error-open endpoints turn downstream failures into 200s (auth-v4 031 §5.3/§5.5). H9's "healthy" must include a credential-level signal (Q11); never swap on status code alone.
- 📄 **Sentinel values in credential slots** (SF-16): the ordered selector cannot distinguish a sentinel from a real secret → opaque `AADSTS7000215`. Omit-is-the-signal; markers live in state fields or KV tags.
- 📄 **FIC idempotency by name instead of (issuer,subject,audience)** (SF-7): Entra enforces the triple per application; a name-only check turns a correct no-op into a failed run (hit live on the first run). The C# port's triple-compare must be preserved through A42.
- 📄 **`-FicOnly` exit-2 mishandling** (SF-8): 2 = structurally-correct-but-unverifiable-from-this-host = the NORMAL off-Azure result; treating it as failure breaks legitimate runs, treating it as terminal success ships an unverified FIC — A42 requires a recorded post-App-Service verification on every exit-2.

## §6 Coordination Protocol

**Framing (git-verified this session)**: auth-v4 is complete and merged via PRs #814/#816/#817/#818 (2026-08-24 17:29 → 2026-08-25 10:20). Tip **`ef61a5f5a`** (2026-08-25 10:35, "refresh carried-forward list — 6 of 10 closed") is the ONLY unmerged commit; the prior tip `569f1d6c2` ("promote the MI environment contract into the operator runbook") has merged. The bundle's reader observed `569f1d6c2` at 10:31 — four minutes before the tip advanced — which fully reconciles the apparent "fabricated commit" dispute between analysis and verifier. Our state: HEAD `45e14556a`, **281 behind / 273 ahead**, merge-base `b0f0ddbdc` (2026-08-17), last master merge `41bacbdae` (2026-08-15). Consequence: coordination is **merge-shaped, not PR-shaped** — everything auth-v4 shipped reaches us through the single A35 event, and r1 resolves every conflict.

### Phase 0 — freeze, with a real mechanism (Q10)

`main` broadcasts via SendMessage to the credential-adjacent active agents — at minimum `ds8-uami-dv-appuser`, `task-153-h12c-credential-config`, `task-160-h14-kv-reader-swap`, `task-161-h14a-sidecar-client`, `Wave-3E-053-H10-AppUser`, `g1-task-109-bicep-config-drift` — that no edits land on the watchlist until A35 completes:

- `scripts/Register-EntraAppRegistrations.ps1` · `Seed-ProductionKeyVault.ps1` · `Configure-ProductionAppSettings.ps1` · `Test-EntraAppRegistrations.ps1` (both-sides-modified — conflict certain)
- `docs/guides/auth-deployment-setup.md` + the 3-4 sibling stubbed deployment guides (semantic conflict — Q4)
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` + `appsettings.tokens.md` · `Infrastructure/Auth/ManagedIdentityCredentialFactory.cs` · `Infrastructure/Graph/GraphClientFactory.cs` · `ConfigurationModule.cs` / `AnalysisServicesModule.cs`
- `infrastructure/bicep/stacks/dev.bicepparam`
- Both-sides-modified beyond the change request's own list: `Create-NewContainerType.ps1`, `Register-BffApiWithContainerType.ps1`

### A35 file-class resolution rules

| Class | Rule |
|---|---|
| The script (`Register-EntraAppRegistrations.ps1`) | **Union merge.** Verified shapes: merge-base 532 lines · ours 982 (task-010 idempotency rewrite `fea66c023`, NOT on master) · master 1582 (FIC estate: 8 `*-Spaarke*` functions, `-FicOnly`/`-CreateFederatedCredential`/`-SkipClientSecret`, exit codes 0/1/2, 70025 exact-match retry, `Assert-SpaarkeFicTenancy`). Master's FIC function bodies are live-verified CONTRACT — do not alter during resolution; re-apply our `Ensure-*`/`Reconcile-*`/`Record-*` layer around them; union the param blocks. Acceptance: `-FicOnly` execution path diffs empty vs master; our reconcile flow present. ⚠️ CONFIRM: commit attributions beyond `63c535511`/`1d5ed9824`/`74c9ef333` (e.g. `ce84f50d0`, `117e1b338`) were not bundle-verified — do not cite them in the PR |
| `.claude/**` auth files (ADR-028, `constraints/auth.md`, adr-check SKILL) | **Take theirs unconditionally** — ours verified stale (no A4, false OBO-secret sentence, no A4 adr-check row); no legitimate local divergence |
| Stubbed guides | Semantic conflict → **Q4**. Default: port master's §1/§5.1/§6 MI-contract content into `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`, keep the stubs with refreshed pointers, notify auth-v4 the operational source moved |
| BFF auth infra | Master as base (it carries the ordered-credential-provider estate); re-apply our punch-list edits (B14 TenantId pin) if clobbered. Do NOT remove the template's ServiceBus KV-ref ourselves (auth-v4 obligation 051-E owns it, deferred to 2026-11-23) — but never copy it into new per-customer surfaces (A44 guard) |
| `stacks/dev.bicepparam` | Verify **P1v3** (not B1) survives the merge — the difference between "slots impossible" and "slots available"; slots are load-bearing for rollout + rollback |
| `oauth-obo-patterns.md` + BFF `CLAUDE.md` | ✅ Verified: master carries the corrections — clean take-theirs; post-merge grep-confirm both |
| Theirs-only scripts (`Rotate-Secrets`, `Reconcile-DemoEnvironment`, `Test-SharePointToken`, `naming-conformance-check`) | Clean textual merge; SEMANTIC review — e.g. rotation of the BFF OBO secret is retired; update any r1 doc/POML still citing that lifecycle |
| Ours-only scripts (`Provision-Customer`, `Deploy-Release`, `Deploy-DataverseSolutions`) | Clean merge; then A36-A44 apply the §10 deltas |
| Registries (root `CLAUDE.md`, `projects/INDEX.md`, patterns INDEX, `sdap-ci.yml`, `scripts/README.md`) | Manual union; routine |

### Communication protocol + §8 open-items ownership

- **Canonical-copy rule**: each coordination doc is canonical in its AUTHOR's worktree (`PROVISIONING-CHANGE-REQUEST.md` → theirs; `AUTH-V4-CHANGE-REQUEST-RESPONSE.md` → ours). Refresh mirrors before reading — the 280-vs-626-line mirror is the proven failure this rule exists for. A44(b) replaces our mirror with the canonical + a header naming the canonical path, and deletes `PROVISIONING-CHANGE-REQUEST copy.md`.
- **Master is the medium post-completion**: only `ef61a5f5a` and future wind-down commits warrant an active ping; everything merged arrives via A35.
- **Ask-shaped changes stay asks**: neither side edits the other's contract surface (their FIC function bodies; our H-handler seams).
- **§8 open items** (per §9.4 + closure commit `c17e856f4`): items 1/2/3 = theirs (verify corrected text arrives via the merge); item 4 = shared (r1 verifies P1v3 at merge time); item 5 = residual on r1 (H4 must never re-seed the lowercase `bff-api-client-secret` alias; the Office-add-in re-point is CI estate — verify before any add-in redeploy from this worktree); item 6 = **r1 owns** (Phase C UAMI Bicep is fleet-authoritative, accepted by auth-v4).
- **Discharge reply contents (S4)**: §5.1 ask satisfied (current line numbers) · §9.2 answer (post-Q2) · task-130-landed notice + Q5 reconciliation proposal · §10.6 fixes (§5.1 status + flag naming) · §11 invariant transfer acknowledgment (exchange verification lands in our H13/T4; exit-2 is our normal creation-time result) · Q4 doc-home outcome · Q3 sunset-date confirmation · G-3/E2E dispatch-date notice (their §9.3 ask).

## §7 Sequencing — what fires when, what's blocked on what

```
S0 (owner, ~1h):     Q2 ratify ── Q3 sign ── Q9 verify          [no customer-owned M2 dispatch until Q2]
S1 (main session):   freeze broadcast (Q10) → A35 master merge   [FULL rigor, prescriptive steps;
                                                                  worktree-net10-migrate guard]
S1∥ (agents, NOW):   A36 ── A37 ── A40                           [no A35 dependency; Sonnet-5 @ high]
S2 (task 205):       A35 → { A38 ∥ A42 } → A39                   [A38+A42 = Fable/Opus FULL rigor;
                     A35 → A41 (after 053/ds8 dedupe review)      A39 waits for BOTH per ordering guard]
                     A35 → A43, A44                               [pre-Phase-F, not pre-186]
S3 (main session):   §6.5 EDIT package + §3.3 cascade + mirror    [needs A35 + Q3 sign-off;
                     refresh + .claude/CHANGELOG entries           .claude/ writes main-session-only]
S4 (coord agent):    discharge reply to auth-v4                   [needs S0 + S3]
S5:                  task 186 E2E live-fire                       [needs A35-A42 + Q9 confirmed]
Phase-F sign-off:    + A43, A44 + silent-failure catalog landed
2026-11-23:          Path-A sunset review (diaried; coordinate auth-v4 051-E)
```

Dependency register (blocked ← blocker):

- **A39** ← A36, A37, A38, **A42** — the §10.2 ordering guard; the A42 edge is the verifier-corrected addition (`RequireSecretFreeIdentity=true` must never precede a working FIC on a fresh stamp)
- **A42's customer-owned-tenant branch** ← Q2 ratification (escalation trigger fires otherwise; Model 1 + Spaarke-hosted Model 2 branches unaffected)
- **A41** ← `Wave-3E-053-H10-AppUser` / `ds8-uami-dv-appuser` output review (dedupe-first; may collapse to probe-only)
- **S3 doc/constraint edits** ← A35 (replacement text cites A4, absent locally) + Q3 sign-off (BINDING rewrite needs the owner per §6.5)
- **Task 186** ← A35, A36, A37, A38, A39, A40, A41, A42 + Q9 (stamp tenancy shape confirmed)
- **Silent-failure catalog pattern file** ← main session (`.claude/` write boundary) + root §11 three-question justification
- **Any prod credential work** ← Q7 (live prod config verified first)
- **Any Office add-in redeploy from this worktree** ← §8-item-5 verification (lowercase KV alias deleted 2026-08-24; confirm the add-in deploy path was re-pointed on master)
- **Path-A sunset actions** ← 2026-11-23 review (or auth-v4's earlier 051-E execution — Q3 confirms which date governs)

## §8 Estimated Effort + Cost

| Bucket | Effort | Model tier / notes |
|---|---|---|
| S1∥ mini-wave (A36, A37, A40) | 5.5h agent | Sonnet-5 @ high; ~half-day wall-clock; dispatch today |
| A35 master merge | 3h main/agent | FULL rigor, prescriptive; +2h escalation if the script union merge needs function-level interleaving |
| Task 205 (A38, A39, A41, A42, A43, A44) | 19.5h agent | A38 + A42 on Fable/Opus (credential-selection seam + cross-worktree reconciliation — auth-tagged, FULL rigor, code-review + adr-check unconditional); rest Sonnet-5 @ high; ~1.5-2 days wall-clock with lanes |
| S3 doc/constraint edits | 2-3h main session | EDITs 1-4 + companion sweep + §3.3 cascade + manifest re-annotation (`Invoke-CatalogGenerator.ps1 -Verify` → exit 0) |
| S0 decisions + S4 discharge | ~1h owner + 0.5h agent | |
| Silent-failure catalog pattern file + review-trigger wiring | 2h main session | Phase-F gate, not 186-blocking |
| **Total** | **~28h agent + 5-6h main session/owner** | **Critical path to 186: 11h serial** (A35 → A38∥A42 → A39) |

Cost posture: Fable/Opus for the two credential-logic rows is deliberate — the blast radius is every future customer's authentication; everything else runs at the §8.5 Sonnet-5 default. Cost of NOT executing: task 186 provisions a BFF that fails one of three ways — **boot-abort** (fail-fast without a working FIC, or missing `keyVaultReferenceIdentity` → exit 134), **boots-but-dead** (no SB/Search data roles; missing/mis-keyed UAMI app-user → app-only Dataverse 401s), or **silently WRONG** (secret beneath MI-FIC absorbing a broken FIC with green health; admin-key re-mint reporting success) — while reviewers keep enforcing a BINDING that protects a secret deleted on 2026-08-24.

## §9 Open Questions

All eleven, each in root §6 escalation format (situation / options / recommendation / needed-by / consequences): [`auth-v4-integration-open-questions.md`](auth-v4-integration-open-questions.md).

| Priority | Questions |
|---|---|
| CRITICAL | Q2 — §9.2 reading (a) ratification |
| HIGH | Q3 (§6.5 sign-off + 11-22 vs 11-23 sunset) · Q6 (H4 default credential for new Model 2 customers) · Q9 (sub `cd95fcec` tenancy shape) |
| MEDIUM | Q4 (MI-contract doc home) · Q5 (task-130 vs `-FicOnly` reconciliation shape) · Q10 (freeze broadcast) · Q11 (BFF warmup self-proof ownership) |
| LOW / verify | Q1 (§5.1 discharge + §10.6 drift) · Q7 (prod estate secret consumption) · Q8 (D3 Model-1 MI wording) |

## §10 Confidence Assessment

Per-dimension: adversarial-verifier verdict → post-verification standing. This session re-ran the disputed reads; several verifier refutations were themselves refuted, and all accepted corrections are integrated above.

| Dimension | Verifier verdict | Key disputes → resolution | Standing after integration |
|---|---|---|---|
| adr-conflict-resolution | LOW | "Shared-lib consumer NOT gone" → **branch-vs-master split confirmed by direct reads of both copies: master migrated, this branch stale — Path C holds, gated on A35.** "Merge cures only 2 of 4 stale docs" → ✅ all 4 verified cured on master. "EDIT 4 before-text may not exist" → ✅ verified verbatim at spec.md:275. Path-classification nuance → adopted (C + narrow time-boxed A rider, stated honestly). §9.2 fourth-case + Δ4 rollback caveat → added to the record | **MEDIUM-HIGH** (was LOW). Residual: prod estate unverified (Q7); sunset-date ambiguity (Q3) |
| topology-decisions | LOW | "Provisioner :547-557/:73-78 citations unsupported" → ✅ **verified real and reading-(a)-consistent by direct read this session.** "Escalation NOT warranted" posture → corrected: Q2 is a formal §6 escalation; all reading-(a) doc edits gated on ratification. Circular TENANCY-doc citation → caveated. I6 test internals → ⚠️ marked unverified | **MEDIUM-HIGH** for the (a) recommendation; the decision itself remains the owner's |
| delta-wbs | MEDIUM | All 7 corrections applied: A39←A42 dependency + recomputed critical path (11h) · A40 downgraded to verify-then-assert · A38 scope honesty (H4 half of A30; H7/task-142 half explicit) · slot-persistence criterion added · per-customer-KV acceptance criteria added · oauth-obo/BFF-CLAUDE verified merge-cured · `cd95fcec` flagged ⚠️ (Q9) | **HIGH** for the row set as drafted |
| silent-failure-audit | LOW | Unverified line cites → provenance-marked ✅/📄/⚠️ throughout §5 · SF-9 self-contradiction → carried as unconfirmed · guard blast-radius under (b) stated · SF-6×SF-17 interaction + KV-tag fleet gap added · §10/§11 justification obligations attached to all new components · "shared-lib contradiction" resolved as branch staleness | **MEDIUM** as a review checklist; every ⚠️ entry requires a fresh read before enforcement |
| coordination-protocol | MEDIUM | "Fabricated commit `ef61a5f5a`" → ✅ **verified REAL and the current tip** (the bundle reader pre-dated it by 4 minutes) · 281/273, HEAD `45e14556a`, 532/982/1582 line counts → ✅ all verified this session · residual: 2 commit attributions ⚠️ excluded from PR citations · freeze mechanism concretized (Q10) · §9.2 promoted to formal escalation | **HIGH** for the merge plan and watchlist |

**Overall confidence: MEDIUM-HIGH.** The load-bearing facts — E-3 closure, branch drift (281 commits; A4 absent locally), the provisioner's issuer logic, both script shapes and flags, the punch-list state (rows end at A34), and the spec verbatims — are now first-hand-verified. The residual lows are explicitly fenced: every ⚠️-marked catalog citation, the two unverified commit attributions, the T2 probe's current assertion, I6 test internals, the prod estate, and sub `cd95fcec`'s tenancy shape each has a named owner-question or an executor verify-step gating it. Nothing in the execution plan depends on an unverified claim without a gate in front of it.

---

## Appendix A — Change-request → plan traceability

Every section of the canonical `PROVISIONING-CHANGE-REQUEST.md` (incl. the 2026-08-25 additions), mapped to where this plan consumes it. Use this to confirm nothing in the inbound document was dropped.

| CR section | Content | Consumed by |
|---|---|---|
| §1 TL;DR (7 change rows) | H3 → FIC creation; H4 secret deleted; rotation retired; never-delete MUST rewritten; cert provisioning dropped; new FIC automation | §1 context · §2 (never-delete rewrite) · A38/A42 rows |
| §2.1 app-only Dataverse already on MI | `#3b`/task 011 + task 022 migration claims | §2 — verified TRUE on master, FALSE on this branch (staleness); A35 |
| §2.2 constraint-file correction | `auth.md:108` false-premise fix + A4 + adr-check row | §1 · §5 SF-21 · A35 (take-theirs class) |
| §2.3 dev FIC live state | `mi-bff-api-dev-assertion` FIC inert-then-consumed; UAMI-only App Service | §3.2 background; no action owed |
| §3.1 FIC object shape | issuer/subject(principalId NOT clientId)/audience recipe | A42 contract · §5 SF-2/SF-3 |
| §3.2 script inventory (11 + ~25 docs) | operational-estate framing of secret removal | §6 watchlist · A44(b) doc sweep |
| §3.3 three failure modes | silent misconfig · AADSTS propagation · UAMI decoy trap | §5 items 1/2 + SF-1/SF-6 · A42 (70025 exact-match) |
| §4 R23 closure | 20-FIC cap counts on the app-reg — non-factor | §3.1 (closed unconditionally at design.md:1528) |
| §5.1 DECIDED (2026-08-25) | Reading 1 for Model 1 + stale edit-ask | §3.1 · Q1 · S4 discharge |
| §5.2 doc fix | design.md §9.1 contradictory sentence | §3.1 (applied 2026-08-19 at :1083-1085; confirm in discharge) |
| §5.3 pluggability contract | secret/FIC seam without handler restructure | FR-39 · A38/A42 · Q6 (transition default) |
| §5.4 invariant I6 | OBO app-reg derived from per-tenant context | §3.1 (FR-40, landed via task 130; ⚠️ test internals unread) |
| §7 sequencing + scripts overlap | rollout phases; 4-script conflict warning | §6 (verified: 4 both-sides + 2 unlisted) · §7 |
| §8 open items 1-6 | yaml drift · phantom name · doc contradiction · SKU drift · lowercase alias · UAMI Bicep | §6 ownership table (1/2/3 theirs · 4 shared · 5 residual · 6 r1-owned) |
| §9.1 sentinel ruling | OMIT, never sentinel; markers outside the credential slot | §2 prong 1 · A38 · §5 SF-16 |
| §9.2 customer-tenant FIC issuer | reading (a) vs (b) — STILL OPEN | §3.2 · **Q2** · A42 escalation-gate · §5 item 1 |
| §9.3 FR-C4 timing caveat | task-130 fallback pre-authorized; notice requested | A42 (reconciliation) · S4 (dispatch-date notice) |
| §9.4 accepted items + cap-inversion note | items closed; "wrong-end reasoning" failure class named | §5 preamble + SF-21 · §6 §8-items table |
| §10 DELIVERED (2026-08-21) | `-FicOnly` contract: subprocess-only, exit 0/1/2, triple idempotency, cross-tenant refusal | A42 · Q5 · §5 SF-7/SF-8 · §6 script class |
| §10 ADDENDUM Δ1-Δ5 (2026-08-25) | SB SAS retire · Search key retire · 8 settings · keyVaultReferenceIdentity · dual app-user | §4 dedup table · A36-A41 |
| §10.2 live settings contract | 10 settings + site property + fail-fast ordering warning | A39 (+ ordering guard incl. A42 dep) · §5 item 4 |
| §10.3 four RBAC role groups | SB / Search / KV / Cognitive roles for the runtime UAMI | A36/A37 (KV Secrets User already applied via A25) |
| §10.4 dual Dataverse app-user | UAMI row objectid == principalId (NOT clientId) | A41 · §5 item 5 (SF-9) |
| §10.5 two traps | template SAS ref (051-E) · Deploy-AllIndexes fallback | A44(a) · A43 |
| §10.6 status lines | ⚠️ same-day drift: says §5.1 still open; `-SkipClientSecret` naming | Q1 (auth-v4 to fix; both flags verified real) |
| §10 CORRECTION (2026-08-25) | 2 of 5 deltas already in `auth-deployment-setup.md`; guide promoted to operational source | §4 (Δ4/Δ5 verdicts) · Q4 (doc home at merge) |
| §11 live-test invariants 1+2 | wrong-subject detection · propagation-flap retry — handed to Wave G-3 for first real exercise | A42 acceptance · Q11 (self-proof home) · S4 acknowledgment |

Cross-file map: formal §6.5 record → [`decisions/adr-028-a4-integration-conflict-resolution.md`](decisions/adr-028-a4-integration-conflict-resolution.md) · executable rows → [`auth-v4-integration-draft-punch-rows.md`](auth-v4-integration-draft-punch-rows.md) · every decision gate → [`auth-v4-integration-open-questions.md`](auth-v4-integration-open-questions.md).

*End of plan. Maintained by customer-provisioning-orchestration-r1; append dated entries below on any material change (canonical-copy rule — this file is canonical in THIS worktree).*

### Landing log

- 2026-08-25 — plan authored (deliverable 1 of 4, auth-v4 integration response); all four companion files created; verification pass completed against live git/code state.
