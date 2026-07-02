# 01 — LLM hallucinations + determinism strategy

> **Priority**: HIGH — architectural
>
> **Source**: R7 W12 UAT feedback, 2026-07-01
>
> **Predecessor work**: R4 already delivered "hallucination Round 1" (temperature 0, explicit grounding instruction, `EntityNameValidator` post-scrub, elimination of baked-in legal-genre example names). This document is Round 2.

## Verbatim operator feedback

> "in the activities some don't seem to match. For example: **'Prioritize reviewing the high priority task 'Follow up with client' related to CMRCL-680582 ↗.[2]'** But when I open the link the Event Name is **'Call outside counsel'** and its due date was 4/28/2026 and the Regarding is **PAT-863412**. So this seems like a complete hallucination? Who do we audit and improve; **should these be more deterministic statements where pull from specific fields and phrases**?"

> "in tl;dr what is the best way to review and improve what the LLM is assessing and writing? Also, I'm not sure what this means 'Review priority tasks including 'Call outside counsel' and 'Follow up with client''"

## What went wrong

The LLM cross-paired attributes from **different** input items and asserted the pairing as fact:

- Item A: `title="Follow up with client"`, `regardingName="???"` (real)
- Item B: `title="Call outside counsel"`, `regardingName="PAT-863412"` (real)
- Emitted bullet: `title="Follow up with client"` + `regardingName="CMRCL-680582"` + citation `[2]` pointing to Item B

Two distinct failures:

1. **Title–regarding pairing hallucination**: LLM lifted `title` from one item and `regardingName` from another, then linked with `[N]` citation pointing to yet a third source.
2. **TL;DR abstraction drift**: TL;DR sentence enumerates specific titles ("Call outside counsel" and "Follow up with client") without matching an item-specific anchor — reads as "important-sounding filler" rather than an actionable summary.

## What R7 Wave 12 already did (Round 2a — LLM prompt tightening via MCP)

Both `BRIEF-NARRATE-CHANNEL` and `BRIEF-NARRATE-TLDR` Dataverse Action rows were updated in spaarkedev1 via MCP `update_record`:

- **PAIRING RULE**: "When you cite a title alongside a regarding record (e.g., 'Follow up with client related to CMRCL-680582'), those two fields MUST come from the SAME input item in `items[]`. Do not combine a title from item A with a regarding from item B."
- **GROUNDING CHECK**: "Before emitting any bullet that names both a `title` and a `regardingName`, locate the input item where `{title: X, regardingName: Y}` appears together. If no single item matches, either omit the bullet or rephrase to name only one attribute."
- **AGGREGATION PREFERENCE**: "Prefer aggregated bullets ('3 new emails this week, 2 tagged urgent') over item-specific bullets ('Email X from Y') when the aggregate carries the same actionable information."
- **STRUCTURAL PREFERENCE (TL;DR only)**: "TL;DR describes counts + themes + top action. Do NOT enumerate item titles in TL;DR sentences — item-specific detail belongs in Channel sections."

Both Action rows bumped `$version` to 2 in metadata, `lastModifiedBy` marked with the tightening tag.

**Open question**: Round 2a is instruction-based mitigation. It relies on the LLM following instructions — historically an unreliable failure mode. R5 needs to decide whether to iterate on prompt tightening, adopt deterministic rendering for narrative bullets, or a hybrid.

## Round 2b — the operator's strategic proposal ("fully-deterministic Activity Notes")

Operator floated but explicitly deferred to R5:

> "Kill LLM channel narration entirely. Render structured item rows per channel deterministically. Preserve TL;DR as LLM-generated for the abstract summary. Zero hallucination risk on the item-level detail."

### What "deterministic Activity Notes" would look like

Instead of the current LLM-generated Channel narrative bullets ("Prioritize reviewing the high priority task 'X' related to Y [N]"), a channel section would render:

```
📧 Emails (7 new)
  • Contract review – Smith Industries → 04/28 (urgent flag)
  • Discovery response – ABC Corp → 04/28
  • ... (5 more)
```

Where each row is composed by concatenating known-safe fields:
- Subject / title (from `sprk_name` or `subject`)
- Sender / assigned party (from `emailsender` / `assignedto`)
- Date (from `createdon` / `modifiedon` / `sprk_duedate`)
- Flags (`sprk_highpriority`, `sprk_monitor`, `sprk_todocolumn`)
- Regarding record name + optional link

**Zero LLM involvement** in the row rendering. The regarding link and item ID are always trustworthy because they come from the source record, not from an LLM output.

### What LLM stays responsible for

- **TL;DR abstract**: 2-3 sentence high-level summary (counts, themes, top action). No item-specific titles.
- **Top action recommendation** in TL;DR (still 1 sentence).
- **Key takeaways** (3-5 bullets, phrased as aggregated observations — "3 documents pending review", not "Review 'Contract Amendment 4'").

### What we lose vs the current approach

- The current LLM Channel narrative can synthesize connections across items ("your 3 high-priority tasks all relate to the Chen matter closing this week"). Deterministic rendering can't do this.
  - **Mitigation**: TL;DR still gets to synthesize — it just doesn't enumerate item titles.
- Some operators prefer prose over lists.
  - **Mitigation**: Deterministic rows can be styled as prose-like ("Contract review for Smith Industries due 4/28") without losing determinism.

### Design considerations for R5

- **Where does the per-channel Action prompt go?** Today `BRIEF-NARRATE-CHANNEL` Action row drives channel prose. Deterministic mode either (a) retires that Action, or (b) demotes it to an optional channel-abstract sentence separate from the item list.
- **Layer 2 template**: R7 Wave 11 introduced the `PromptSchemaRenderer ## Input` layer. Deterministic channel rendering doesn't need a prompt — but the TL;DR still does. Clean split: TL;DR keeps Layer 1 + Layer 2; Channel goes straight from `items[]` → view model → React render.
- **Widget side**: `HighPrioritySection.tsx` is already a proven deterministic-render pattern (Wave 12 mini-report cards). Channel sections would follow the same pattern — pure props → structured cards, no LLM output.
- **Existing playbook**: `DAILY-BRIEFING-NARRATE` currently has narrator nodes per channel. Deterministic mode simplifies the playbook — one TL;DR node + channel data collection + a single "assemble view model" node. Fewer LLM calls, faster render.

## Round 2c — hybrid options if R5 doesn't want full determinism

If leaving Channel narrative as LLM-generated:

- **Layer 3 grounding validator**: after LLM narration, extract `{title, regardingName, citation}` tuples from output; verify each tuple exists in `items[]`. Drop or rephrase failed tuples before render. R4's `EntityNameValidator` was a first-cut of this idea for firm names — R5 extends to title/regarding pairs.
- **Structured schema output**: force the LLM to emit JSON `{title, itemId, regardingName?}` per bullet; render bullets from JSON in the widget. Widget rejects bullets where `itemId` doesn't resolve. Removes the possibility of cross-item pairing at the widget layer.
- **Bullet templates with slot filling**: LLM chooses a template ("Follow up on {title} for {regardingName}"), we substitute from a single input item picked by LLM. No free-form generation.

## Recommendation for R5 design.md author

Two tracks worth spec'ing:

- **Track A — Adopt fully-deterministic Channel rendering** (operator's proposal). Reserve LLM for TL;DR + top action + key-takeaway bullets only. Retire or demote `BRIEF-NARRATE-CHANNEL`. Simplest, zero item-level hallucination risk.
- **Track B — Structured schema output + widget-side validation** (Round 2c option 2). Keep LLM-generated Channel bullets but constrain to `{itemId, phrasing}` and enforce `itemId` correctness at widget render. Lower rewrite cost than Track A, most hallucination classes gone.

Recommend Track A unless there's a compelling reason to keep LLM Channel narration — the R7 W12 hallucination proves the class of bug is not solved by prompt engineering.

## Test cases for whichever direction R5 chooses

1. **Mixed-item corpus**: Feed 4 items with distinct `{title, regardingName}` pairs; verify no cross-pairing in output.
2. **Aggregation preference**: Feed 10 similar items (10 new emails); verify output does NOT enumerate 10 subjects (aggregation preferred).
3. **Grounding round-trip**: For every `[N]` citation, verify the linked record matches the surface text of the bullet (regarding name, title, or aggregate).
4. **TL;DR abstraction**: Verify TL;DR does not name specific item titles; only counts + themes + top action.

## References

- Current source of truth for narrative prompts: Dataverse Action rows `BRIEF-NARRATE-CHANNEL` (id `dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6`), `BRIEF-NARRATE-TLDR` (id `ce299eb4-fc70-f111-ab0e-7ced8ddc4cc6`) in spaarkedev1
- R7 W12 tightening commit: `f6938617a`
- R4 Round 1 hallucination fix: `EntityNameValidator` Executor + JPS grounding (see `spaarke-daily-update-service-r4/spec.md` §MUST Rules → Consumer)
- Playbook-driven LLM output pattern (Layer 1 orchestrator + Layer 2 `PromptSchemaRenderer`): [`docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](../../../../docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md)
- R7 W12 rewrite of HighPriority section as deterministic mini-report cards: [`src/client/shared/Spaarke.DailyBriefing.Components/src/components/HighPrioritySection.tsx`](../../../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/HighPrioritySection.tsx)
