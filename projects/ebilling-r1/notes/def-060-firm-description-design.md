# DEF-060: per-rule firm description (option A, EB-D-212) — design for peer review

> Status: **design only; nothing is built.** It is for review by uac-r2 and ontology-r1, and both must accept it before any code is written. The schema step needs the owner's dev go.
> Issue: DEF-060, #2637 ("No writer populates the Signal firm reason shown to partners").
> Decision: EB-D-212 (owner, 2026-11-10). Option A is the per-rule admin description. Option B, per-rejection reviewer text, is rejected.
> Builds on the 130d design section "DEF-060 writer design" (`notes/130-firm-findings-projection.md` on `task/ebilling-130d`, `69a8b2db72`), where this option was called "option 3". This note supersedes that section wherever they differ.

## 1. What changes for a firm

- Today, a rejection finding shows its reason code plus the 130d table text (EB-D-210). A decided line, adjustment or other non-rejecting item shows no reason, because no Signal carries `sprk_firmreason`.
- After this change, a billing admin can write one firm-facing description on a rule version.
  - When a Signal is created from that version, the writer copies the description onto the Signal.
  - The firm then sees that text instead of the table text, for every finding of that rule (Duplicate excepted, §5).
  - Decided items gain a reason when their rule has a description.
- Without a description, or on a Signal created before the change, nothing changes: the table text is shown, and decided items show no reason.

## 2. Data

| Item | Decision | Why |
|---|---|---|
| New column `sprk_policyversion.sprk_firmdescription` | Memo, MaxLength 2000 (storage), nullable. The cap actually applied is 200 characters (§4). | It sits on the **version**, not the policy, so the text is immutable with the version a Signal cites. A published version's text cannot change under an existing Signal. `sprk_shortheadline` and `sprk_messagetemplate` live on the version for the same reason. |
| Field security on the new column | **Recommended: secured.** Read for every profile that reads `sprk_policyversion` today; create and update only for the BFF writer profile. | It is partner-visible free text. Securing it closes the direct model-driven-form edit path, which would bypass the BFF validator (ADR-002: one server-side owner per invariant, no plugins). Memo is field-securable; only BigInt is not. **Question Q1** for uac-r2. |
| `sprk_signal.sprk_firmreason` | No change. It already exists (Memo 2000, field-secured, task 026, D-95); only the ontology writer identity writes it, and the BFF reads it. | — |
| Where the reason code lives | **No new Signal column.** Every billing Signal already stores one `billing.reason` evidence entry per merged reason (`BillingFindingSignalMapper.ReasonEvidenceKind`, `:355`) plus the reason-set fingerprint. Because the text is per rule version, it covers exactly the codes listed in the Signal's own reason evidence. | This meets uac-r2's fixed point "the reason code is stored with the text and the projection matches on it" without schema on an ontology-owned table. **Question Q2** for uac-r2: is evidence-on-the-same-row acceptable, or do you want an explicit code column beside the text? |

Deployment: an idempotent script in `scripts/ebilling/` on `EbillingSchema.psm1` with `-Verify`, plus Pester tests. SpaarkeMaster source is synced by deploy task 079 (no SpaarkeMaster edits outside the serial tasks). #2277 obligation: `sprk_policyversion` is a stamped table, but this column is not `sprk_createdbyperson`, so nothing changes there.

## 3. Write path (who writes what, and how)

### 3.1 The admin writes the description

- **Surface:** the existing rule review/publish endpoints in `RuleSetService`. A draft version accepts `firmDescription`; publish freezes it with the rest of the version (DEF-061's `sprk_publishedon` stamp and the shared published predicate stay the single "is published" test).
- **Who:** the billing-admin privilege, checked with `CallerHoldsPrivilegeAsync` (D-76), which is the same check as editing the rule today. No new privilege.
- **Validator:** one shared `FirmDescriptionRules.Validate(string?)`, used by the publish validator, the mapper (§3.2) and the writer preflight (§3.3). It:
  - trims, and treats blank-after-trim as "none";
  - refuses text longer than 200 characters (after trim, counted in runes);
  - refuses `{` and `}`, so `{{token}}` placeholders and templating are impossible;
  - refuses markup (`<`, `>`);
  - walks runes (`EnumerateRunes`, `Rune.GetUnicodeCategory`) with **the same predicate as the read side**: Control, Format, LineSeparator, ParagraphSeparator.

  `FirmRejectionFindings.IsInvisibleControl` moves to this shared type, so the read and write sides cannot drift. A refusal returns a 422 with a stable code, never echoing the text.
- **Copy and carry-forward:** every list that enumerates version columns gains the new column, so the text survives publish, version copy and Procedure assignment:
  - `RuleSetService` select, publish and copy;
  - `ProcedureAssignmentService` reads and copies;
  - `Seed-BillingSystemPolicy.ps1` and `Apply-DoLanePolicies.ps1` diffs, where they compare version columns.

### 3.2 The mapper carries it to the Signal

- `BillingFactLoader` reads `sprk_firmdescription` with the rule candidate: `BillingRuleCandidate.FirmDescription`.
- `BillingFindingSignalMapper.RequestOf` sets `SignalWriteRequest.FirmReason` from the candidate **only after** `FirmDescriptionRules.Validate` passes.
  - Invalid stored text, for example from a pre-validator row, is **dropped**: the Signal is written without a firm reason and the table text shows. The mapper logs `EBILLING-FIRM-DESCRIPTION-DROPPED` plus the policy version id, never the text.
  - It must not fall into the existing preflight fallback (`:395-398`), which swaps the whole request for the held-sentence request. Bad admin text must never change the Signal's sentence or drop the Signal.
- **The text never comes from data.** `FirmReason` is set only from the stored version. It never comes from a request body, `FactValues`, `ValuesRead`, the Signal sentence or AI output.
- **Merged Signals** (one per policy and subject): all members share one policy version, so there is one text and it covers every code in the Signal's reason evidence.
- **The persisted plan:** `signalPlan` in `sprk_findingsjson` (`InvoiceSubmissionPersister.BuildFindingsJson`, `StoredFindings.ParsePlanEntry`) carries `firmReason`, so a replay through `InvoiceRuleSignalReconciler.WriteAsync` (correction and add-line paths) writes the same text. An older plan without the member parses to "none" and replays as today.

### 3.3 The writer stores it (ontology-r1's file)

`SignalWriter.cs` gets two changes:
- `SignalWriteRequest` gains an optional `FirmReason` init property: nullable and defaulted, like `Episode` and `RequestedBy`, so no existing caller changes.
- `BuildEntity` adds the one line `if (!string.IsNullOrWhiteSpace(request.FirmReason)) entity["sprk_firmreason"] = request.FirmReason;`.
- `CheckRequest`/`PreflightRefusal` runs `FirmDescriptionRules.Validate` as defence in depth: it refuses a request whose `FirmReason` fails, with a new stable code. The mapper filters first, so this fires only for a caller that bypasses the mapper.

ontology-r1's fixed points, each with a test:
- **(a) Writer identity only.** The line rides the one existing create, `OntologyWriterDataverseClient.CreateAsync`, as the ontology writer identity: the only member of the profile that may write `sprk_firmreason`. There is no second client, no BFF-identity write and no fallback. If the identity is unavailable, the write fails closed exactly as today.
- **(b) No impersonation.** The create payload gains one attribute and nothing else. `createdonbehalfby` stays empty and no on-behalf header is sent, so the projection's writer-created gate (`WriterCreatedRecord`) stays true. Test: the payload has no `createdonbehalfby`.
- **(c) A Resolved Signal is never rewritten.** `sprk_firmreason` is set on **create** only.
  - The dedupe refresh still sends only `sprk_lastevaluated`.
  - Reopening (`ResolvedEpisodeRule.ReopenAsNextEpisode`) creates a new episode row that carries its own copy.
  - Tests: a refresh payload has no `sprk_firmreason`; a Resolved row gets no update; an ArchTest source scan shows `sprk_firmreason` appears in the writer only inside the create builder.

## 4. Read path (the projection)

`FirmFindingsProjection` after 130d already prefers a non-blank, writer-created Signal text over the table and filters it (`ShownFirmReason`: rune filter, trim, 200 plus "..." cut). It changes in three ways:

1. **Match on the reason code (uac-r2 K1).** For a rejection finding, the override is taken only from a Signal whose `billing.reason` evidence contains **that finding's** reason code, keyed on (policy version, line, reason code). Any other finding falls back to the table text.
   - The projection adds `sprk_evidencerefs` to its select and parses **only** `billing.reason` entries' codes with an allow-list parse. Other evidence kinds can carry values and are never read here.
   - Codes are checked against `BillingRuleCatalogue` before use; an unknown code means no override.
2. **Decided items** (Reduced, Disallowed, Added): the decision resolves its Signals as a whole, so the item's `FirmReasons` take each resolved Signal's text, as 130d already reads them. **Question Q3** for uac-r2: should a decided item also require a code match? I recommend no, because the decision is per rule, not per reason, and the description is per rule.
3. **Logs carry codes only.** A dropped or refused description logs a stable code plus the version id. The 130d `EBILLING-FIRM-OUTCOME-REASON-TEXT-MISSING` rule is unchanged.

## 5. uac-r2's fixed points (each is a requirement)

| Fixed point | Where it is met |
|---|---|
| Write guard | `FirmDescriptionRules.Validate` at publish (§3.1), in the mapper (§3.2) and in the writer preflight (§3.3); the column is field-secured for write (Q1). |
| 200-character cap | Validator, in runes, at all three points; the read cut is unchanged (up to 203 shown). |
| No placeholders | `{` and `}` refused; the text is never built from values (guard test below). |
| Code-only logs | §3.2 and §4.3; tested so a Warning contains the code and not the text. |
| Same rune predicate on both sides | One shared predicate type, used by validator and projection; one theory test runs both against the same cases (including U+061C, U+FEFF, U+E0041, U+1F600). |
| Reason code stored with the text, matched by the projection | §2 (reason evidence on the same row) and §4.1. |
| Duplicate never takes an override | Unchanged from 130d F-1: the projection ignores Signal text for a Duplicate. The editor also refuses a description on a version whose rule type is Duplicate, so the text never exists to be shown. |

## 6. Flag interaction (owner awareness)

EB-D-210 says the reason text is shown **whatever** the visible-to-firms flag says. With the flag off, the firm sees no clause text, no section reference and no rule set name (EB-D-209). An admin could still type a rule set name or a clause into the description.

The design does not try to detect that. Instead, the editor shows a fixed notice: "Firms see this text even when the rule set is hidden from firms. Do not quote the guideline or name the rule set." **Question Q4** for uac-r2: is an editor notice enough, or should the projection suppress the override when the flag is off? Suppressing it contradicts EB-D-210's "whatever the flag", so it would go back to the owner.

## 7. Tests (summary)

- **Writer:** with `FirmReason`, writes `sprk_firmreason`; without it, the payload has no such key; no `createdonbehalfby`; refresh and Resolved paths are untouched; the ArchTest source scan.
- **Mapper:** a valid description reaches the request; blank is omitted; invalid is dropped with a code-only log, and the Signal's sentence is unchanged (must-fire control: the preflight fallback is not taken); guard test that `FirmReason` is never built from `FactValues`, `ValuesRead` or the sentence.
- **Plan:** a round trip serializes and parses to equal; an old plan parses to none.
- **Validator and projection:** the shared theory (same rune cases); 200 vs 201 runes; `{`, `}`, `<`, `>`.
- **Projection:** the code-match override is shown for its code and not for the Signal's other rule's code; an unknown code means the table; Duplicate is never overridden (existing); decided-item text.
- **Column carry-forward:** publish, copy and assignment each keep the column.
- **Schema script:** Pester, plus `-Verify` asserting the column, its type and length, and the field-security grants.
- **Live (owner go):** one smoke Signal on dev through the real writer, showing `sprk_firmreason` written by the writer identity and read back by the BFF identity.

## 8. Files

- **ontology-r1:** `Services/Signals/SignalWriter.cs` (request member, `BuildEntity` line, preflight check).
- **ebilling:**
  - `Services/Signals/Billing/BillingFindingSignalMapper.cs` and `BillingFactLoader.cs`;
  - `Services/Finance/FirmRejectionFindings.cs` (predicate moves out);
  - new `Services/Finance/FirmDescriptionRules.cs`;
  - `FirmFindingsProjection.cs`;
  - `RuleSetService.cs` and `ProcedureAssignmentService.cs` (column lists, editor input);
  - `InvoiceSubmissionPersister.cs` and `InvoiceSubmissionAfterSteps.cs` (plan);
  - the rule editor page;
  - the schema script and its Pester tests.

## 9. Sequencing and live changes

- **Build after 116 merges.** 116 is editing `RuleSetService.cs` and retiring `RuleSetEndpoints` routes now, and this change touches the same version column lists.
- **It depends on the 130 batch merge.** The projection code it extends (130d) is not on work yet.
- **Live dev changes, each needing the owner's OK in the ebilling-r1 session plus uac-r2 notices before and after:**
  1. the column and its field security on dev;
  2. the smoke Signal.

## 10. Questions for the reviewers

- **Q1 (uac-r2):** field-secure `sprk_firmdescription` for write (recommended), or leave it unsecured like `sprk_messagetemplate`?
- **Q2 (uac-r2):** is the reason code in the same row's `billing.reason` evidence enough, or do you want a dedicated code column?
- **Q3 (uac-r2):** should decided items match on code too? Recommended: no.
- **Q4 (uac-r2, maybe the owner):** is the editor notice enough under "whatever the flag"?
- **Q5 (ontology-r1):** do you accept the three `SignalWriter` changes (§3.3) and the column on your `sprk_policyversion` table, with the preflight refusal as a new stable code?

## 11. Review outcomes

### uac-r2: ACCEPTED with conditions (2026-11-10, design `a58e6ceaa6`)

Answers:
- **Q1: yes, field-secure the column for write.** Read goes to every profile that reads `sprk_policyversion` today; create and update go to the BFF writer profile only.
  - `RuleSetService` publish and copy, and `ProcedureAssignmentService` copy, must write the version **as the app**: the billing-admin privilege is checked as the user, the write is made as the app. An impersonated write would be refused once the column is secured.
  - `-Verify` asserts that no other profile has create or update on the column.
- **Q2: the same row's `billing.reason` evidence is enough; no code column.**
  - The version in the match key comes from the Signal's own writer-set version lookup.
  - The projection reads evidence only from rows passing the `WriterCreatedRecord` gate.
  - Both are stated in the code and tested.
- **Q3: no code match for decided items.** The Signals read must still be the ones that decision resolved (as in 130d), never "any Signal on the line".
- **Q4: the editor notice plus one mechanical guard.** At publish, refuse (422, stable code, text never echoed) a description that contains, case-insensitively, the version's own rule set name or policy name. No owner question; EB-D-210 is not reopened.

Conditions (binding for the build):
- **C1, replay re-derives the text and never trusts the plan.**
  - The plan persists the policy version id only, if it isn't already there; it does not persist `firmReason`.
  - Replay (`InvoiceRuleSignalReconciler.WriteAsync`) reads `sprk_firmdescription` from that version and runs `FirmDescriptionRules.Validate`. Invalid text is dropped exactly as in the mapper: the Signal is kept, there is no preflight fallback, and the log carries the code only.
  - If `firmReason` is ever kept in the plan, replay ignores it.
  - Test: a plan whose `firmReason` differs from the version's text writes the version's text.
  - **This replaces the §3.2 "persisted plan" bullet.**
- **C2, a published version's description cannot change.** The BFF draft-update path refuses a `firmDescription` change on a version that the shared published predicate (DEF-061) says is published.
  - Must-fire: an edit on a published version is refused and nothing is written.
  - Must-not-fire: an edit on a draft is accepted.
  - Copying the version into a new draft carries the text forward.
- **C3, the writer preflight never drops a Signal for a mapper caller.** Test: a mapper-built request with invalid stored text reaches `SignalWriter` with `FirmReason = null`, never with the text. So the preflight code can fire only for a non-mapper caller.

uac-r2 receives the build diff when it is ready, plus notices before and after each live step.

### ontology-r1: ACCEPTED with conditions (2026-11-10, Q5)

- **O1, FirmReason never changes Signal identity.**
  - It is not in the dedupe key, the reason-set fingerprint, the sentence or any hash.
  - The dedupe refresh writes neither `sprk_firmreason` nor the Signal's policy-version lookup.
  - Test: a refresh under a newer version with a different description leaves both the text and the cited version unchanged.
- **O2, the writer refusal is bypass detection only.**
  - The stable code is `firm_reason_invalid`; logs carry ids only.
  - The mapper path never reaches it.
  - Test: the preflight fallback request (`BillingFindingSignalMapper` `:395-398`), if it is built, carries no `FirmReason`.
- **O3, only the billing mapper sets `FirmReason`** (plus the replay, which re-derives it per C1). The ontology evaluator, re-evaluation and decision paths never do. The ArchTest scan is widened to "`FirmReason =` is assigned only in `BillingFindingSignalMapper` and the plan replay". `OntologyAdminAuthoringService` already refuses billing policy versions (uniform 404), so the Ontology admin tab needs no carry-forward.
- **O4, schema by the ontology recipe** (`projects/spaarke-ontology-platform-r1/notes/schema-draft.md`):
  - Web API with SchemaName `sprk_FirmDescription`, Memo 2000;
  - the `MSCRM.SolutionUniqueName` header of the solution that owns `sprk_policyversion` on dev;
  - publish `sprk_policyversion` only, never `PublishAllXml`;
  - `Entity.xml` synced via 079.
  - The owner's dev go in ebilling-r1 is enough for the column; ontology-r1 receives notices before and after.
- **O5, field security: CONFLICTS with uac-r2 Q1.** The ontology project rule (its `CLAUDE.md` §5) forbids field security profiles on the five ontology tables, `sprk_policyversion` included. Securing the column is an exception that needs the owner's explicit go typed in the **ontology-r1** session. ontology-r1's view: unsecured is enough, like `sprk_messagetemplate`, because a form edit already needs Write on `sprk_policyversion` (admins only), and the mapper, replay and projection re-validate. **Resolved by the owner (EB-D-213): unsecured.** uac-r2's Q1 answer and its sub-conditions (write as the app, `-Verify` no-other-writer) are withdrawn. C2 still applies to the BFF path. Accepted limit: a form edit by an admin can still change a published version's text, which affects only Signals created afterwards (the same limit as every other version column). §2's "Field security" row is superseded.
- **O6, scoped ontology review before merge** of `SignalWriter.cs` and everything under `Services/Signals/**`, including the billing mapper and loader.
