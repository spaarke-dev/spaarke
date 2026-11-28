# Word edition: design proposal (draft for owner decision)

> Status: **proposal, not adopted.** Written 2026-11-28 from WP-2, WP-5 and WP-6 and the W4/W5/W6 comparison.
> It answers surfaces synthesis §6 decision 2 ("Word edition now or after WP-6?"): WP-6 passed, so here it is.
> Adopting it would withdraw component protocol rule 4.2 for Word (see §6). Nothing here is built yet.

## 1. The question

Today, review in Word is **Microsoft Legal plus our Word playbook** (protocol rule 4.2): we ship content and no
Word skill. The rule was written when Word ran none of our skills, and says to revisit after T-P7 (the Word probe).
T-P7 is now answered. Should Spaarke ship a **Word review skill** that applies the redline with our own fixed code?

## 2. What the tests established

| Fact | Evidence |
|---|---|
| Word desktop loads and follows **instructions-only** plugin skills | WP-2 (2026-11-25); `spaarke-decisions` admin-deployed, 2026-11-26 |
| Word desktop calls **Spaarke MCP tools** from a skill | `spaarke-decisions`, 2026-11-26 |
| Word desktop does **not** run a skill's Python scripts | WP-1, two tries, 2026-11-26 |
| Word desktop runs **fixed Office.js carried in the skill text**, as written | WP-5 run 3, hash-proven |
| Copilot fills **one data line** (`const EDITS = [...]`) from the request and our fixed code applies it: tracked replacements, one comment per change, all hashes exact | WP-6 6a, 2026-11-27 |
| A skill can carry **17 KB of code** intact (the most a SKILL.md under 20,000 characters can hold) | WP-6 6c–6e |
| Word's Copilot has **two runners**: Office.js (via a sub-agent; Word API; return value not passed back) and an HTML-document runner (returns values and errors; Copilot shims our calls onto HTML) | WP-6 |
| Errors and console output reach Copilot **from the HTML runner**; from Office.js: **WP-7, in flight** | WP-6 6b; WP-7 |
| Word for the web listed our plugin but didn't run it; Word for Mac untested | T-12b; WX-1, WX-3 |
| Cowork has no Office.js runner; given a Word skill it says "code not run" safely | WP-5 Cowork control |

## 3. Why a Word skill now

The W4/W5/W6 comparison (`poc-e2e/results.md`, 2026-11-26), same NDA and playbook:
- **Word's own review (W4) over-edits and breaks structure.** Thirteen changes, four of them not called for: it
  upgraded acceptable fallbacks, deleted two whole sections without renumbering (7 → 9, 9 → 11) and added an
  unnumbered section. Its comments name neither the playbook nor the topic. Same pattern as Test 3, 2026-11-11.
- **Our Cowork skill (W6)** kept the numbering ("Intentionally Omitted") but needs a Python sandbox, Cowork credits
  and a new file. The attorney's open document is in Word.

The gap is not finding issues. All three found the seven required fixes. The gap is **what the edit does to the
document**. That is the part fixed code controls and an instruction alone doesn't (Test 3: instructions alone
didn't hold the redline rules).

## 4. Proposed shape

### 4.1 One skill per practice, in the same plugin as the Cowork skills

- Name: `<practice>-review-word` (e.g. `nda-review-word`). Instructions only, no scripts. Description says "in Word".
- It ships in the practice plugin beside `nda-review` (Cowork), per synthesis rule 5 (one package, per-host skills).
  Each host activates what it supports: Word runs the Word skill; Cowork gets the Cowork skill. Open point: whether
  Cowork could pick the Word skill for a generic request (WP-5 control used chips).

### 4.2 Flow in Word

1. **Playbook in.** The user attaches or `/`-references the Word playbook (today's Word tier, tested in Acts 2 and
   7). Later, a Spaarke MCP tool returns the positions (`spike_get_profile` exists on the dev connector).
2. **Review.** Copilot rates each topic against the playbook using our method rules, including "a clause at a
   fallback passes: no edit, goes to the judgment list" (the W6 fix candidate, PR #3263).
3. **Plan and approval.** A table of the edits it proposes (topic, find text, new text, reason). The user approves
   or trims it in chat.
4. **Apply.** Copilot fills the `EDITS` line from the approved plan and runs the fixed applier (4.3) on the Office.js
   runner. It quotes the applier's result or error.
5. **Report.** Chat summary: changes made, judgment items, the applier's mark. No new files in Word.

**Fallback** when the applier can't run (no Office.js runner, refused, or an error): no partial edits by other
means. Copilot reports the plan table and says either "apply these with Word's own tracked changes, one at a time"
or "run the Cowork edition". It never edits without our rules. Whether Word reports an applier error back to
Copilot is WP-7.

### 4.3 The applier (fixed code; grows from the WP-6 edit probe)

| Rule | Why |
|---|---|
| Runner check first: stop with a fixed error if there's no real Office.js | WP-6: Copilot shimmed our code onto HTML |
| Check **every** edit before changing anything: each `find` must match exactly once in the document | All or nothing. WP-6 6a's per-edit check, widened from one paragraph to the body |
| Operations: `replace`, `omit` (section body → "Intentionally Omitted.", heading and number kept), `insertAfter` (new sentence into an existing clause, never an unnumbered section) | The W4 structural failures |
| Track Changes on for the edits, the user's setting restored after, also on failure | WP-6 code; 7b checks the failure path |
| One comment per change: `[<Topic ID>] <reason> (Spaarke <playbook> v<n>)` | W5's comments were the most useful; W4's named no playbook |
| Never deletes a heading, a whole section or a numbered paragraph; never touches text outside the matched ranges | Numbering kept, as in W5 and W6 |
| Ends with a hashed mark line, so the reply can be checked | WP-5/6 method |
| `find` strings ≤ 255 characters (Word's documented search limit, to verify); long clauses are matched by a unique opening phrase | Office.js `search` limit |

Size budget for one SKILL.md (≤ 20,000 characters): method and rating rules about 5,000; plan, approval and report
rules about 2,000; applier about 6,000 to 7,000 (WP-6 edit probe: 2,500 for one operation). **The playbook doesn't
fit as well** (the NDA master is 11,000), so it comes in as an attachment or an MCP result, never in the skill.

### 4.4 One content model

The Word skill's method text is generated by the build from the same source as the Cowork skill's (design §6, "one
content model"), so the rating rules can't drift between editions. The applier is a build input with its own tests
(the Node stub harness from the probe, `tests/test_package.py`).

## 5. Tests before building

| # | Test | Settles |
|---|---|---|
| WP-7 | (in flight) Errors from the Office.js runner; does the Office.js-only rule hold? | Whether the applier can report failures; whether the rule keeps Copilot off the HTML runner |
| WP-8 | Real-document fidelity: the applier probe on a test copy of a real NDA (curly quotes, § signs, numbered lists, text split across runs, a 255-character find) | Whether `search` finds real contract text as Copilot copies it |
| WP-9 | One full pass with the playbook attached: review → plan → approval → apply, in one chat | Whether Copilot does all four with an attachment plus our skill |
| WP-10 | Both editions in one plugin: does Cowork ever pick the Word skill, or Word the Cowork one? | Packaging |
| AD-1 | Admin center with a skill that ships Python, on a new app id | Whether the practice plugin (with Cowork Python skills) can be admin-deployed at all |

## 6. What adopting it changes

- Component protocol **rule 4.2** ("in Word, review = Microsoft Legal + our playbook; no competing Word skill"):
  withdrawn for Word, replaced by "Word review = our Word skill, using our playbook; Microsoft Legal stays available".
- Design §2.4.7 row 1 and §2.4.8 (Word runs no third-party skill parts): updated to the WP results.
- **App ids** (admin-center finding, 2026-11-27): the admin center refuses an app id already uploaded through Cowork,
  even after removal. Today `build_accelerator.py` gives each package one fixed id. Proposal: keep that id for
  Cowork test uploads, and derive a second id for admin deployment (`uuid5(".../<pkg>/admin")`), chosen by a build
  flag. Small change, to master by its own PR.
- **Cross-project check (specialist-legal-agents-r1, 2026-11-28):** no overlap today. Its Phase 0 builds no Word
  redline, and its NDA agents only review and recommend. Its program design is Microsoft-first for contract markup
  (Spaarke supplies positions, Microsoft's edit-contract/legal plugin does tracked changes), except NDA in Cowork →
  our `nda-review`. Its **D-A13 (Microsoft-first) is still an open owner decision.** If its Phase 1 needs redlines,
  it would use this skill rather than build a second, and flag it first. So adopting this proposal and D-A13 should
  be decided together: the two projects currently assume the same Microsoft-first rule (our rule 4.2).
- Risks carried over from the synthesis §4: Word may tighten or drop skill-supplied Office.js (undocumented), so the
  fallback in 4.2 is required; EU tenants run GPT, so the skill must be tested on GPT; Information Barriers tenants
  can't use skills at all.

## 7. Owner decisions

1. Adopt the Word edition direction (withdraw rule 4.2 for Word)? Or keep Microsoft Legal + playbook in Word?
2. If yes: run WP-8 to WP-10 and AD-1 after WP-7, then build `nda-review-word` first.
3. The app-id scheme in §6 (Cowork id kept; admin id derived).
