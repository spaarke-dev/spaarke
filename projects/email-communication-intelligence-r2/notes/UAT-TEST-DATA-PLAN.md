# Email Intelligence — UAT Test-Data Plan (dev / spaarkedev1)

> Built 2026-08-13. Answers: *what automated/AI-assisted testing can we do, and can Claude Code create test email records?*
> **Yes** — in three modes. Two are fully automated by Claude Code (built + verified below); the third (true capture) needs mail delivered to a monitored mailbox.

## The determining fact

There is **no synthetic-email injection endpoint**. The capture stage (row create → association rungs → triage AI → Job B/C *propose*) runs only inside `IncomingCommunicationProcessor.ProcessAsync`, which re-fetches a **real** Graph message. So the pipeline splits into:

- **POST-CAPTURE** features (reconciliation grid, triage display, queue-feed, association review, Job B/C **apply/dismiss/create-task**) — operate on existing `sprk_communication` rows → **seed rows directly**.
- **CAPTURE-TIME** features (rung **writes**, message-id dedup, triage **AI**, Job B/C **propose**, footer signing, `.eml`→SPE archive) — need a **real inbound email**.
- **Read-only bridge**: `POST /api/communications/{id}/suggest-associations` runs the full rung engine **evaluate-only** on a stored row (no writes) — lets us assert engine *decisions* automatically.

## Mode A — Review corpus (POST-CAPTURE) — ✅ BUILT + VERIFIED

`scripts/seed-uat-communication-corpus.ps1` (idempotent; marker `sprk_correlationid` = `uat-seed-20260813-NN`). Seeded **14 `sprk_communication` rows** + **3 `sprk_emailreviewlog` proposals** against real matters (PAT-411021 Drowsy Digital NDA, PAT-415062 Head-Mounted Display, PAT-545148 Hing Canadian, PAT-897705 Network patent).

Coverage: priorities Urgent→Low · triage categories Court/Filing…Marketing-Noise · association states Suggested / Ambiguous / Pending-Review / Unresolved · confidence 0.15–0.94 · one team-owned row (per-team grid) · two Job B field proposals (`sprk_nextreviewdate`, `sprk_matterdescription`) · one Job C create-task proposal.

**Exercises (open these on dev):** the deployed **needs-review grid** (`sprk_communicationreconciliation` code page + SpaarkeAi widget) · **per-team grid** (row 12, Spaarke team) · triage priority/category display · **queue-feed** ranking · association-review modal · **Job B apply/dismiss** (rows 02, 08) · **Job C create-task/apply** (row 09).

| row | subject | exercises |
|---|---|---|
| 01 | USPTO Office Action — response due 60 days | Urgent + Court/Filing; deadline triage |
| 03 | Both matters — PAT-411021 **and** PAT-415062 | **Ambiguous** (conflict withheld) |
| 08 | Please update next review date to Sept 30 | **Job B** `sprk_nextreviewdate` proposal |
| 09 | Action required: file the amendment by Friday | **Job C** create-task proposal |
| 11 | New patent application — PAT-942665 | new-record-referenced |
| 12 | Filing deadline (team queue) | **per-team grid** (team-owned) |
| 07/14 | newsletter / meeting notes | Low + Dismiss (noise) |

**Caveat:** seeded rows have no SPE `.eml` archive, so the "open email as sent" reading pane (`eml-render`) won't render on them. Add a `sprk_document` + uploaded `.eml` per row if you want that surface exercised (not yet done).

## Mode B — Rung-decision preview harness (read-only) — ✅ BUILT + PASSING

`scripts/uat-rung-preview-harness.ps1` — calls `suggest-associations` on every seeded row and prints the engine's evaluate-only decision (status + candidates + conflict + rung provenance). **No writes.** Auth = `az account get-access-token` for the BFF audience `api://1e40baad-…`.

Result: **golden assertions 2/2 pass** — row 03 → `Ambiguous` + `conflict:true` on both candidates (ExplicitReference reverse-lookup + RecordNameMatch), row 11 → `Suggested` against the real PAT-942665 matter. Rows without a verbatim matter number → `Pending Review` (correct: needs human review). This reproduces the R1 golden "misfile" behaviors ([`fixtures/r1-golden-emails.md`](fixtures/r1-golden-emails.md)) automatically and re-runnably.

## Mode C — Synthesized real emails (CAPTURE-TIME) — authored; send needs the mailbox

Monitored mailbox exists: **`mailbox-central@spaarke.com`** (shared) + `testuser1@spaarke.com` (`sprk_communicationaccount`). To exercise the true engine, **deliver** these to that mailbox → the webhook (or 5-min `InboundPollingBackupService`) picks them up → full capture runs.

Authored `.eml` fixtures in [`fixtures/uat-emails/`](fixtures/uat-emails/):
- `01-recipient-alias-autofile.eml` — `matter-PAT411021@spaarke.com` in **Bcc** → **RecipientAliasRung** → auto-file to the Drowsy Digital matter (conf 1.0).
- `02-conflicting-matters-ambiguous.eml` — body references PAT-411021 **and** PAT-415062 → **Ambiguous** (real write path, vs Mode B's preview).

Per-rung matrix to author next (templates below):
| rung | signal the email must carry |
|---|---|
| TrackingTokenRung | a valid Spaarke HMAC footer token quoted in the reply (send a Spaarke email first, then reply) — needs the real signing key |
| ThreadContinuityRung | `In-Reply-To:` / `References:` header = the `internetMessageId` of an already-captured comm |
| RecordNameMatch / ExplicitReference | verbatim matter number (e.g. `PAT-545148`) in subject/body |
| Dedup (message-id) | send the **same** `Message-ID` twice → second must reconcile to the canonical, not create a row |
| SPE content-hash dedup | attach a **byte-identical** file across two uploads (upload path, not capture) |
| AffinityRung | seed `sprk_affinity` from repeated confirmations, then a matching sender |

**Send runbook:** (a) forward/send each `.eml` to `mailbox-central@spaarke.com` from any account, OR (b) Graph `sendMail` with app perms; then verify a new `sprk_communication` appears with `sprk_associationprovenance` populated and the expected `sprk_associationstatus`. Confirm dedup by sending the same Message-ID twice and checking only one row exists.

## AI-assisted angle

The AI steps (triage classification, Job B proposals, association ranking) are *under test*, so inputs are engineered with **known expected outputs** (an explicit next-review date → Job B `sprk_nextreviewdate` proposal with citation; two matters → Ambiguous). R1's pinned golden descriptor gives exact expected outcomes → machine-assertable via Mode B.

## Cleanup

`pwsh scripts/seed-uat-communication-corpus.ps1 -Clean` deletes all `uat-seed-20260813-*` rows. (Proposals cascade via the `sprk_communication` lookup or delete by `sprk_sourceref LIKE 'uat-seed-20260813%'`.)

## Security follow-up (flagged 2026-08-13)

`AzureAd__ClientSecret` is a **plaintext app setting** on `spaarke-bff-dev` — move it to a **Key Vault reference** (`@Microsoft.KeyVault(...)`). Value not recorded here.
