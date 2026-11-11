# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-10 late (checkpoint #111, desktop session `uac-r2`; ebilling's session is now `ebilling-r1`). Private details: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-handoff-2026-11-09-198.md` (outside the repo; read it first, never copy it into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. This session (`uac-r2`, Claude desktop) is authoritative. Merge: `git merge-tree`, then squash with `--match-head-commit`. Live actions, deploys, and role or settings changes need the owner's go IN THIS SESSION; a peer's go is never the owner's. Bash `grep`/`sed` loops HANG: use Grep/Read/PowerShell. |
| **Active task** | **198**, the share side of fm 054 (Direct 1:1 messages), FULL. **MERGED to master as `c71414c7a0`** (#2670, squash at head `7ac8eb19de` = `4979b39bed` + a whitespace-format fix for the Tier 1 check; 42 checks green). **Not deployed** (deploy order below). `C:\wt198` kept for 198b. |
| **198 gates (merged tree `a0e867796f`)** | Build 0/0; ArchTests 2144/2144 (master 2131; ArchTests not in the sln, so build it explicitly); Core 64/64; IntegrationTests 212/0 (8 skipped); Spe.Integration 353/0 (23 skipped). Full `Sprk.Bff.Api.Tests` run: 40,680 passed and 381 failed (379 timeouts, 1 HTTP 499, 1 Compose fidelity assertion); all 351 failed methods passed on a batched re-run. Publish +0.06 MB vs fresh master (39.32 → 39.38, 191/191 files). CVE: none. `--remerge-diff` empty. |
| **Next action** | (1) fm 2b APPROVED (incl. F-2b-1 fix `9d2fdfbeab`); merged into fm's work branch as `a215006537`, not on master yet. (2) **198b** after fm's 2b lands on master: `FindExistingAsync` (`DirectThreadAccessService.cs` ~:626) calls fm's `DirectMessageRecipient.ReadStrictPairShapeAsync`; replace the unordered 50-candidate scan with one query through the existing access-grant seam (no third POA client). Fallback: newest-first paging with a stated bound and a warning. Send the diff to fm. It must land before the 198 deploy. (3) Put the deploy sequence to the owner. (4) Fix #2669 (my 188 chain cap: set truncated and log a warning) in wrap-up scope. |
| **Deploy order (binding, agreed with fm)** | fm 054a piece 2 (`Set-DirectRecipientSchema.ps1 -Apply` first, then the stamp) + 2b → fm 054b field security → fm 054c recipient backfill (dry run, owner go, apply) → 198 + 198b. Part A Direct role setup goes before fm 054a piece 3. Each live step needs the owner's go in its own session. |
| **Dev state** | BFF = **`755987b385`** since 2026-11-10 (owner go in THIS session): mcp-r1's `spike/spaarkeai-mcp-phase0` + master through #2660 (ontology 176). Live-before `6fbb3a7642` (the spike). Verified: ProductVersion, /healthz 200, anon POST /mcp/legal-operations 401. **Dev runs the MCP spike: a later deploy must contain `755987b385` (ancestor check) or be coordinated with mcp-r1.** ebilling dev schema 2026-11-10: DEF-061 column; 116 `sprk_procedureversion.sprk_notconvertedlist` (field-secured, verified). |
| **Scratch** | `C:\wt198m`, `C:\p198m`, `C:\p198b` and `C:\tmp\p198*.zip` were removed on 2026-11-10. Untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree was left by an agent; leave it alone. |

## Owner answers (2026-11-09/10)
- ebilling 130c: with "visible to firms" off, the rule set name is hidden (EB-D-209). Rejections carry a reason code plus a firm-safe description, and a written reason overrides it (option C, in this project; EB-D-210).
- Budget inquiry email (ontology D-216): filed under the inquiry AND its matter; **internal-only** (2026-11-10).
- Earlier: Direct 1:1 = a separate owner team; keep access after filing; #2270/#2278 = a customer-BU column.

## Reviews state
- **ebilling 130d:** FINAL and APPROVED at `69a8b2db72`. Firm reason text: rune walk, dropping Control/Format/LineSeparator/ParagraphSeparator characters on both paths; a Duplicate never takes the override; the DEF-060 writer must use the same predicate. Ships in one dev batch with 130-0b/130b/F3b; nothing goes to dev without the owner's go here.
- **ebilling F3b diff 3 (EB-D-215, bound 5):** APPROVED, no F (`task/ebilling-130-0-f3b` at `8b413d3d22`). Rulings: (a) a single-record GET on an undecidable chain keeps the 503 (D-70; the single-record counterpart of a list's truncated); writes keep the uniform 403. (b) The 188 narrowing's silent chain cap is mine: #2669. Ks accepted: message-list truncated set for a row other filters would drop; the warning has no rate limit; the 187 private item (see the handoff). Next from ebilling: merge the batch into work, verify the tip, send me the batch diff note.
- **ontology 177 (#2666): APPROVED at `8382047421`** (F-177-R2-1 fixed: on-demand embed feeds verified `linkedDocument` ids into the narrowing). R1 known limit (environment without the access columns) + deploy-note line in the PR body. Ontology merges on green after merge-tree.
- **provisioning #2618 (task 267):** my `SecureFlagFieldSecurityAssertion.cs` clause 2 would flag the new memberless "Spaarke Ontology Writer" BU team. Provisioning is fixing it IN THEIR BRANCH per my 5 points (exempt that one team; reverse finding WRITER UNIT READS; exactly one such unit; 4 Evaluate tests; comment). Review their commit when sent.
- **ebilling batch: APPROVED** at `017a965312` (130-0, 130-0b, 130a/b, 130c, 130d, F3b; the `WorkforceRoleCeilingGate.cs` union is right; housekeeping OK; nit: `ExternalGrantLifecycle.cs:444` spacing, which will fail the Tier 1 format check). Ships WITHOUT 116. Path: ebilling opens a PR to master → merge on green → **I** coordinate the dev deploy with mcp-r1 (dev runs the spike `755987b385`, which master lacks) and ask the owner for the go here.
- **ebilling 116 R1:** I recommended option (c) to the owner via ebilling (deactivate superseded drafts; Write per row, as the user; guard that every draft reader filters `statecode`; publish refuses an inactive draft). The owner decides in ebilling's session.
- **Still to review:** ebilling 116 fix round 2; the DEF-060 build diff; R4's #2269 and FR reviews. Waiting on ontology's 176 smoke (needs the owner to send one Budget inquiry).
- **ontology 176 (D-216): DONE.** Rounds 2 (`73b7bb7787..3e89e7e8e3`) and 3 (`..bbeb1cfcd3`: R3-1 test, R3-2 Console reply puts the primary first so the footer signs the inquiry) approved. Merged to master as #2660 `26d135bc8f`; on dev in `755987b385`. Ontology runs the 176 smoke and sends the triple. Ks: K5, K8 (DI-147), K9.
- **fm 054a piece 2 + fail-safe:** APPROVED; in #2659. Follow-up #2652 (converge onto my reader after 198).
- **ebilling DEF-060 option A (EB-D-212) design:** ACCEPTED with C1 (replay re-derives from the version, never plan text), C2 (BFF refuses a description change on a published version), C3 (mapper never hands invalid text to the writer), C4 (rule-set/policy name check at copy time on mapper + replay; publish 422 stays). Owner EB-D-213: column UNSECURED (ontology table rule), so my Q1 is withdrawn; form-edit gap accepted. Build diff comes after 116 + the 130 batch merge.
- **R4 (spa-r4):** #2270/#2278 moved to me; owner decision already (a) customer-BU column. R4 routes #2269 and FR-08/18/24/28/31 reviews + BFF deploys through me.
- **fm outbound Direct create routing:** the owner's decision in fm's session; I review it.

## Open owner items
- Private: one dev identity-hygiene question (in the handoff).
- To report: the 130-0 F1-2 trade-off (known limit).
- #2270/#2278 design (customer-BU column).

## Filed this stretch
#2647, #2648, #2651, #2652 (fm, DI-025), #2669 (188 chain cap, NFR-03). Opened: PR #2670 (198).

## My work queue after 198
1. #2270/#2278 design. 2. Backlog: #2627, #2621, #2585, #2576, #2524, #2543; parked 193/197/196a/196b; TASK-INDEX reconcile at 090.

## Peers (cross-session names)
ebilling `ebilling-r1` (`local_04b9d03a…`) · field-mapping `field-mapping-r1` (`local_b58f031d…`) · ontology `ontology-r1` (`local_72ca5a70…`) · provisioning `provisioning-r1` · word-add-in `word-add-in-r1` · mcp `mcp-r1`.

## Private items (never in issues/PRs/commits)
See the private handoff. Identifiers are fine in public; "X can still read/write Y today" for an open gap, or an identity's role holdings, is not.
