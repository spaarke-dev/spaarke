# Phase 0 index and artifact inventory

> **Program:** specialist-legal-agents-r1, Phase 0 (spec FR-01 inventory before upload, FR-14 no orphans).
> **Tasks:** `../tasks/TASK-INDEX.md`. **Rules:** `../CLAUDE.md` §2 and §5.
> **Written by task 001 from files only.** It observed nothing in the tenant, Dataverse or Azure. Any date or state it could not read from a project file is "unknown".

## 1. Test index

"Kit task" authors the folder; "Run task" records the results in it. A folder is created by its kit task, so a link may not resolve until that task is done.

| Test | Folder / file | Kit task | Run task | What it decides |
|---|---|---|---|---|
| G1-NDA | [g1-nda/](g1-nda/README.md) (profiles, prompt script, results sheet; synthetic corpus in `g1-nda/corpus/`) | 002 (kit, profiles), 003 (corpus, ratchet probes) | 022 | D-A7: where the profile is applied (the GO/NO-GO) |
| G1-PAT | [g1-pat/](g1-pat/README.md) | 006 | 026 | Repeats G1-NDA for the variants still in contention |
| G2 | [g2/](g2/README.md) (packaging questions, `g2/package/`) | 004 (kit), 020 (packages) | 023 | G2 remainder: packaging |
| G3 | [g3/](g3/README.md) (spike SHA, results) | 011 (spike branch), 021 (run record) | 021 | Profile resolution, run key, agent identity |
| G5 | [g5/](g5/README.md) (routing kit, `g5/package/`) | 004 (kit), 020 (packages) | 023 | D-A12: delivery shape |
| G6 | [g6/](g6/README.md) (extends T-P2) | 005 | 024 | D-A13: compose or recommend; results go to accelerator-r1 |
| Patents (content and Cowork spike) | [patents/](patents/README.md) (published-patent test content, drafting skill) | 006 | 025 (Cowork spike) | FR-09 content and FR-10 drafting spike |
| Desk items I-1 to I-4 | [../notes/phase0-desk-items.md](../notes/phase0-desk-items.md) | 007 | none (desk work) | File:line evidence |
| Decision record | [G1-decision.md](G1-decision.md) | 030 | n/a | Go/no-go record for D-A7, D-A12, D-A13 |

Existing before this index: [g8/](g8/README.md) (G8 and G8-P2, result recorded, removals pending; see section 2) and `g9/` (research notes only, not a Phase 0 test in this table). Spike tools and the DEV copy `agreement-review-spike` come from tasks 010, 011 and 015; they are listed in section 2.

## 2. Artifact inventory (FR-01, FR-14)

One row per tenant, DEV or branch artifact. The R4 row is the inventory row sent to COPILOT-PLATFORM-COORDINATION (Issue #3006) before any upload. Removal status values: `owner removal pending`, `planned (not created)`, `not created`, `removed (date)`, `closed in coordination (date)`.

| Artifact | Kind | Owner project | Scope | R4 row sent (date) | Installed (date) | Removal date | Removal status |
|---|---|---|---|---|---|---|---|
| G8-a `spaarke-legal-g8-ab-test` plus two skills in it (`spaarke-mcp-operations`, `spaarke-tool-test`) | Agent Builder declarative agent, private to its creator, no actions | specialist-legal-agents-r1 | Owner only (private to creator) | sent before creation; date unknown | unknown (g8/README records runs on 2026-11-23, creation date not recorded) | 2026-11-30 | owner removal pending |
| G8-p2 `spaarke-legal-g8p2-test` (app id `802fd9cc-feae-425a-aaef-8e97aba87ff5`) | App package, declarative agent v1.6 with one MCP action (`/mcp/legal-operations`, OAuthPluginVault = A1) | specialist-legal-agents-r1 | Org catalog, owner and test user only (R7) | sent before upload; date unknown | unknown (g8/README: uploaded 2026-11-24 as versions 1.0.0 to 1.0.4) | 2026-11-30 | owner removal pending |
| G8-p2-sl `spaarke-legal-g8p2-sl` (app id `d38f8df8-d248-4ab9-9c3d-4f7e6b513b19`) | App package, same action, namespace `spaarkeg8sl`, pinned `spaarke_show_decisions`; versions 1.1.0 to 1.1.2 | specialist-legal-agents-r1 | Org catalog, owner and test user only (R7) | sent before upload; date unknown | unknown (g8/README: runs on 2026-11-24) | 2026-11-30 | owner removal pending |
| G8-p2-org-learn `spaarke-legal-g8p2-org-learn` (app id `0421c980-686d-4953-b817-c4b4960c8d17`) | Control package, action on Microsoft's public Learn MCP server, auth None | specialist-legal-agents-r1 | Org catalog, owner and test user only (R7) | sent before upload; date unknown | unknown (g8/README: worked 2026-11-24) | 2026-11-30 | owner removal pending |
| G8-p2-dd `spaarke-legal-g8p2-dd` (app id `cb5afd1c-c0ef-409a-a2a7-fe7e00ddafa5`) | App package, own MCP action, dynamic discovery (V2) | specialist-legal-agents-r1 | Org catalog, owner and test user only (R7) | 2026-11-24 | unknown (g8/README: first run 2026-11-24 about 18:04Z) | 2026-11-30 | owner removal pending |
| G8-p `spaarke-legal-g8-pkg-test` (app id `ac7da782-71bc-4c13-ba90-44eea8ef48b8`) | App package, `GraphConnectors`, no actions | specialist-legal-agents-r1 | n/a | sent; date unknown | never uploaded (per g8/README) | n/a | not created, nothing to remove |
| G8-p2-sl-learn `spaarke-legal-g8p2-sl-learn` (app id `125c3b10-1833-468f-8141-6c54b8ab2085`) | Control package | specialist-legal-agents-r1 | n/a | sent; date unknown | never uploaded (per g8/README) | n/a | not created, nothing to remove |
| `agreement-review-spike` | DEV Dataverse Action row `17285c7b-98e9-f111-84ea-00224829aef9`, a copy of `agreement-review` with the five profile positions declared as inputs | specialist-legal-agents-r1 | DEV Dataverse (`spaarkedev1`) | not applicable (no tenant upload); owner go in chat 2026-11-26 | 2026-11-26 | at clean-up, task 031 | live in DEV |
| Spike tools `spike_get_profile`, `spike_review_nda` | Code on branch `spike/specialist-agents-phase0`, deployed to the dev BFF via uac-r2; never merged | specialist-legal-agents-r1 | Dev BFF, route `/mcp/legal-operations` | not applicable (no tenant upload) | 2026-11-26 ~18:00Z (dev BFF `d307d67e36`) | until the next dev deploy from master replaces them (date set at deploy) | live on dev |
| G1-a `spaarke-legal-g1a-nda-test` (app id `f80243d0-b9ef-409c-8a8f-787c184162ea`, v1.0.0) | App package, declarative agent v1.6 with one MCP action (`/mcp/legal-operations`, A1 `reference_id`, dynamic discovery); G1-NDA variant (a), Chat | specialist-legal-agents-r1 | Org catalog, owner and test user `demo@` only (R7) | not sent yet (row due before upload; text in `g1-nda/README.md` "R4 row (to send)") | not installed | at task 031 | built, not uploaded |
| G1-b `spaarke-legal-g1b-nda-test` (app id `67f754d4-9327-4451-9c79-552047a17ed4`, v1.0.0) | As G1-a; variant (b), Chat | specialist-legal-agents-r1 | As G1-a | not sent yet (same) | not installed | at task 031 | built, not uploaded |
| G1-c `spaarke-legal-g1c-nda-test` (app id `080ac94c-c863-4802-acf0-ec2f71dea925`, v1.0.0) | As G1-a; variant (c), Chat | specialist-legal-agents-r1 | As G1-a | not sent yet (same) | not installed | at task 031 | built, not uploaded |
| G1-a Cowork `spaarke-legal-g1a-cowork-test` (app id `9c52e606-2574-48f5-b557-1f7246c19414`, v1.0.0; skill `spaarke-test-g1-nda-review-a`) | Plugin package, manifest 1.30, one skill, no action (uses the tenant connector "Spaarke MCP"); variant (a), Cowork | specialist-legal-agents-r1 | Plugin registry, owner and `demo@` only (R7); switched on in Cowork by those two | not sent yet (row due before upload; text in `g1-nda/README.md`) | not installed | at task 031 | built, not uploaded |
| G1-b Cowork `spaarke-legal-g1b-cowork-test` (app id `6940e661-d067-4154-adb4-06366648c697`, v1.0.0; skill `spaarke-test-g1-nda-review-b`) | As G1-a Cowork; variant (b) | specialist-legal-agents-r1 | As G1-a Cowork | not sent yet (same) | not installed | at task 031 | built, not uploaded |
| G1-c Cowork `spaarke-legal-g1c-cowork-test` (app id `391d8de8-e48b-4a66-84ba-ef9c764222fc`, v1.0.0; skill `spaarke-test-g1-nda-review-c`) | As G1-a Cowork; variant (c) | specialist-legal-agents-r1 | As G1-a Cowork | not sent yet (same) | not installed | at task 031 | built, not uploaded |
| G5-a `spaarke-legal-g5-area-test` (app id `f7af4143-02b0-4912-b3ee-e73b0e202422`, v1.0.0) | App package, declarative agent v1.6 with one MCP action, three functions (NDA review, agreement review, legal hold review) | specialist-legal-agents-r1 | Org catalog, owner and `demo@` only (R7) | not sent yet (row due before upload; text in `g5/README.md` "R4 row (to send)") | not installed | at task 031 | built, not uploaded |
| G2-q1 `spaarke-legal-g2-q1-test` (app id `7bcab05e-3d88-4006-96c9-545edfc5a6ee`, v1.0.0) | App package, manifest 1.30: declarative agent v1.6 with one MCP action plus one marker skill `spaarke-test-g2-marker` | specialist-legal-agents-r1 | Org catalog, owner and `demo@` only (R7) | not sent yet (row due before upload; text in `g2/README.md` "R4 row (to send)") | not installed | at task 031 | built, not uploaded |
| G2-q2 / G2-q3 packages | Not built by task 020. Q2: the v1.30 manifest schema allows one declarative agent per package (`g2/README.md`), so no package. Q3: no package built; task 023 decides | specialist-legal-agents-r1 | n/a | not applicable | never uploaded | n/a | not created, nothing to remove (Q2 proposed DROPPED, task 023 confirms) |
| Cowork patent-drafting spike assets (task 025) | Skill or plugin used in Cowork, if any is installed | specialist-legal-agents-r1 | Owner (and test user) only | not sent yet (row due before any install) | not installed (planned) | set by task 025 | planned (not created) |
| G6 coexistence setup (task 024) | Owner switches on Legal for Cowork; any added skill or plugin | specialist-legal-agents-r1 | Owner only | not sent yet (row due before any install) | not installed (planned) | set by task 024 | planned (not created) |

Planned rows are placeholders so that every artifact has a row before it exists. Update the row in the same change as the action; never record an install or removal that the owner has not confirmed.

## 3. How to add an artifact

1. Add a row to the table above first, with owner project, scope (owner and test user only, R7) and a removal date.
2. Send the R4 row to COPILOT-PLATFORM-COORDINATION (#3006) and fill in the "R4 row sent" date. Quote the row in the test folder's README (FR-01).
3. Only then upload or install. If the route cannot be scoped to the owner and test user, do not upload.
4. After the owner confirms removal, set the status to `removed (date)`, tell coordination, and mark the row `closed in coordination (date)` (FR-14, task 031).
