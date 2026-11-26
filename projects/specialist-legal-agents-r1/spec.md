# Specialist Legal Agents R1, Phase 0 (umbrella) - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-11-25
> **Source**: design.md (v0.8), §7–§8 for Phase 0, with §3.9–§3.15, §10–§12 for context
> **Scope of this spec:** **Phase 0 only.** Under OD-3 (amended 2026-11-24), Phase 0 runs in this umbrella folder. Phases 1–4 are child projects, each with its own spec.

## Executive Summary

Phase 0 decides the runtime shape of the specialist-agent program before anything is built for Phase 1. It answers one question: does Copilot reliably follow an attorney's profile, or must the profile drive server-side execution while Copilot only routes? That is G1, the go/no-go, and it decides D-A7.

Phase 0 also measures:
- **G5:** whether one practice-area agent routes requests to the right function. This decides D-A12.
- **G6:** whether Spaarke and Microsoft's legal plugins coexist without either capturing the other's tasks. This decides D-A13.
- **The patent-drafting spike:** whether a Cowork skill plus a profile can draft a usable patent section. This is the first look at the patent archetype (OD-2).

The deliverable is evidence and a go/no-go record. No product code is merged.

## Scope

### In Scope
- **Test kit:**
  - prompt scripts, results sheets and test profiles;
  - an R4 inventory row for every tenant artifact, sent to COPILOT-PLATFORM-COORDINATION before upload.
- **Dev-only spike tools** on a branch that is never merged (the spike-v2 pattern): a profile-returning tool and a server-side review tool.
- **G3:** resolve the caller's profile on a stateless MCP server.
- **G1-NDA:** profile adherence, variants (a), (b) and (c), in Chat and Cowork. This is the go/no-go.
- **The tool-returned-instructions test:** does Copilot follow instructions that come back in a tool result? It is folded into G1 variant (a) and G5.
- **G5:** function routing inside one area agent.
- **G6:** coexistence with Microsoft Legal for Cowork. It subsumes the accelerator's T-P2.
- **G2 remainder:**
  - a declarative agent and a `SKILL.md` in one package;
  - several agents in one package;
  - whether a practice plugin can depend on the core plugin.
- **Minimal patent test content:** two test profiles and about 5 disclosures drawn from published patents, labelled `status: test`.
- **The Cowork patent-drafting spike.**
- **G1-PAT:** a repeat of G1 on patents, for the variants still in contention after G1-NDA.
- **Desk items I-1 to I-4** (design §8).
- **The go/no-go record and clean-up.**

### Out of Scope
- **Phase 1 build:**
  - the `sprk_agentprofile` / `sprk_agentprofileversion` tables;
  - the `sprk_technologyarea` columns and solution export;
  - the layer compiler and ratchet;
  - the profile Code Page;
  - production MCP tools and `/mcp/patents`.
- **G4 Outlook reach, G10 (Copilot Studio) and G11 (SPFx Copilot UX components).** Owner, 2026-11-25: "do not invest in Copilot Studio or SPFx".
- **Already answered:** G8 / G8-P2, V1, V2 and G9 (`phase0/g8/README.md`, `phase0/g9/research.md`), and the T1–T14 evidence (design §7).
- **Later phases:** autonomous runs, background identity, the supervision protocol and agent chaining (Phases 3–4).
- **A server-side patent drafting tool.** G1-PAT reuses only the G1-NDA variants still in contention.
- **Attorney-approved content of any kind.** All Phase 0 content is synthetic, or drawn from public sources.

### Affected Areas
- `projects/specialist-legal-agents-r1/phase0/` - the test kits, results and go/no-go record. One folder per test, like the existing `g8/` and `g9/`.
- `projects/specialist-legal-agents-r1/notes/` - the desk-item write-ups (I-1 to I-4).
- **Spike branch only (never merged):** `src/server/api/Sprk.Bff.Api/Api/Mcp/` gets spike tool classes following `LegalOperations/Decisions/ShowDecisionsTool.cs`, plus their DI wiring.
- **Tenant (owner clicks only):** coded declarative-agent test packages, installed through the admin center for the owner and the test user only.
- **Read only:** the accelerator NDA content on `work/spaarke-legal-ai-accelerator-r1`:
  - `src/accelerators/practices/nda/positions/`, `evals/`;
  - `notes/component-protocol.md` (T-P2);
  - never `answer-key/` or the real NDAs.

## Requirements

### Functional Requirements

1. **FR-01: Test kit and inventory.**
   - For each test (G1-NDA, G1-PAT, G2, G3, G5, G6, the patent spike), write `phase0/<test>/README.md` containing:
     - the setup;
     - the exact prompt script, numbered, including the controls;
     - a results sheet: prompt, surface, time (UTC), expected owner or position, observed, pass or fail, App Insights evidence;
     - the pass rule.
   - Before any upload, send an R4 inventory row to COPILOT-PLATFORM-COORDINATION. The row holds the artifact, owner project, scope (owner and test user), removal plan and removal date.
   - Acceptance:
     - each test folder has the four parts;
     - no artifact is uploaded without a sent R4 row (the row is quoted in the README);
     - no prompt or file in the kit references `answer-key/` or a real NDA.
2. **FR-02: Spike tools (dev only, never merged).**
   - Create the spike branch from **current master**, after mcp-r2's merged work is in, so a dev deploy rolls nothing back.
   - Add the following tools. All are read-only (`readOnlyHint: true`), model-visible, on `/mcp/legal-operations`, and built by copying `ShowDecisionsTool`:
     - `spike_get_profile`: resolves the caller with `ICallerSystemUserResolver`. It returns that caller's **test profile**, a static JSON file on the branch keyed by `systemuserid`, rendered as a data-guarded instruction block in the `userFragment` style. It also returns a `profileVersion` string.
     - `spike_review_nda`: takes the document reference and runs the `agreement-review` Action with `IActionRunner`, with the caller's test-profile positions bound as inputs. It returns the structured review and `profileVersion`.
     - The patent profile set for G1-PAT is added the same way, as files.
   - Deploy through uac-r2's session, with the owner's go for each SHA.
   - Acceptance:
     - a caller with no test profile gets an `isError` result with reason code `NO_PROFILE`, never another user's profile or a default attorney profile;
     - an unresolved caller gets `isError` `CALLER_UNRESOLVED`;
     - each tool answers within 30 s at p95 on dev;
     - a tool test proves user A never receives user B's profile;
     - the branch is never merged, and its PR, if one is opened, is marked "do not merge".
3. **FR-03: G3, profile resolution from the caller's identity.**
   - Show that the path caller → `systemuser` → test profile → `profileVersion` works within the latency budget, and that every call in one run returns the same version.
   - Establish what Copilot sends that could key a run or conversation. The spike logs the **names** of incoming request headers. Values are redacted, and `Authorization` and cookies are never logged.
   - Establish whether the server can tell **which agent** called. Candidates: a header, a per-agent route, or nothing.
   - Acceptance:
     - `phase0/g3/README.md` records the headers seen, per surface and per caller kind (tenant connector vs agent action);
     - it records the fail-closed results from FR-02;
     - it gives a yes/no answer for "can we key a run" and for "can we identify the agent";
     - and a recommendation for Phase 1.
4. **FR-04: G1-NDA, profile adherence (GO/NO-GO).**
   - **Setup:**
     - Two test profiles, A and B, that differ on at least 5 NDA positions: term, residuals, non-solicit, governing law and the uncapped-indemnity flag. They are derived from the accelerator baseline positions.
     - The synthetic Bluefin Harbor corpus, plus about 10 more synthetic NDAs. The accelerator's injection case is the ratchet probe.
   - **Variants:**
     - (a) The test agent calls `spike_get_profile` and applies the profile itself.
     - (b) The archetype guidance is in the agent's instructions (≤ 8,000 chars), and the per-owner profile comes from `spike_get_profile`.
     - (c) The agent routes to `spike_review_nda`, and Copilot only renders the result.
   - **Run** in Chat and in Cowork, as owner A and as test user B (the profiles are keyed by their `systemuserid`).
   - **Staged:**
     - a 10-case pilot for each variant and surface;
     - a variant scoring below 90% in its pilot stops;
     - variants still in contention get the full run, about 40 position decisions per surface per profile.
   - **Measure:**
     - the share of position decisions matching the active profile;
     - cross-profile bleed (A's position applied for B);
     - ratchet escapes (a prompt asks the agent to skip a gate or widen its authority).
   - Acceptance:
     - for (a) and (b) to pass: at least 95% adherence on **both** surfaces, 0 ratchet escapes and 0 bleed;
     - otherwise (c) is the runtime;
     - every logged position decision cites its run and its `profileVersion`.
5. **FR-05: The tool-returned-instructions test.**
   - Within variant (a) and G5, record separately whether Copilot follows **instructions** (not just data) that come back in a tool result.
   - Prior evidence: T7 found MCP server `instructions` are ignored.
   - Acceptance: `phase0/g1-nda/README.md` has its own row for it, with the share of runs where the returned instructions were followed, per surface.
6. **FR-06: G5, function routing.**
   - **Baseline:** first, run the G5 prompt set against the existing *Spaarke Legal Review* agent (two functions: NDA and legal hold), after confirming its instruction version.
   - **Main test:** one test area agent with at least 3 functions (NDA review, agreement review, legal hold review), built to the G8-P2 build rules:
     - a coded declarative agent with its own MCP action;
     - dynamic discovery (BR-5);
     - a fresh app id for each action change, or a wait of about 45 minutes after an update.
   - Prompts: mixed in-scope prompts plus out-of-scope controls, in Chat.
   - Acceptance:
     - at least 95% correct routing, and no function invoked for any control;
     - if it fails, the README records the fallback: one agent per function, with the persona kept server-side;
     - any re-upload or copy of the baseline agent has an R4 row.
7. **FR-07: G6, coexistence with Microsoft's legal plugins.**
   - Precondition: the owner switches on Legal for Cowork for the owner and the test user.
   - Start from the accelerator's T-P2 prompt set (NDA → Spaarke; MSA → Microsoft; redline summary → Microsoft; 5 prompts × 2 configurations) and extend it.
   - Run in Chat and in Cowork. Record:
     - which owner each prompt reached;
     - whether a Spaarke agent can **invoke** Microsoft's `review-contract` / `summarize-redlines`, or can only **recommend** them;
     - whether an exported test playbook is honoured by `review-contract` and by Word's Run Review.
   - Acceptance:
     - no Spaarke function captures a Microsoft-owned task, and no Microsoft skill captures a Spaarke-owned one, across the shared set;
     - the results are sent to accelerator-r1 (G6 subsumes its T-P2).
8. **FR-08: G2 remainder, packaging.**
   - Using the G5 test package, add one `SKILL.md`. Test whether the agent and the skill both work in Chat.
   - Also record:
     - whether one package can hold two agents;
     - whether a practice plugin can depend on the core plugin.
   - Acceptance: informational. `phase0/g2/README.md` records yes, no or untestable for each of the three questions, with evidence.
9. **FR-09: Minimal patent test content.**
   - Write the following, all labelled `status: test` and not attorney-approved:
     - two patent test profiles that differ on at least 5 drafting standards (for example claim structure, independent-claim count, use of means-plus-function, terminology conventions, background length);
     - about 5 test disclosures drawn from **published** patents (public), with the source publication number recorded for each;
     - a short drafting skill (`SKILL.md`).
   - Acceptance:
     - the files are under `phase0/patents/`;
     - no confidential or client material;
     - each profile difference is testable from the output.
10. **FR-10: The Cowork patent-drafting spike.**
    - In Cowork, with the drafting skill plus a test profile, draft one section of an application (for example the claims, or the detailed description of one embodiment) from a test disclosure.
    - Repeat with the other profile.
    - Acceptance:
      - the output is a usable Word document in Cowork's canvas;
      - the owner judges whether each draft follows its profile's standards and records a pass or fail per standard;
      - the two profiles produce visibly different drafts on the differing standards.
11. **FR-11: G1-PAT, the patent repeat.**
    - Once FR-09 exists, repeat FR-04's measurement on patents. Only variants (a) and/or (b) that are still in contention after G1-NDA are repeated; there is no patent server-side tool.
    - Acceptance: the same thresholds as FR-04, recorded in `phase0/g1-pat/README.md`; it confirms or contradicts G1-NDA.
12. **FR-12: Desk items I-1 to I-4.**
    - **I-1:** which tool surface a profile governs: the MCP catalogue, `IAnalysisToolHandler` or `IAiToolHandler`. And whether the chat confirmation gate (`SideEffectGateAIFunction`) also applies on the MCP path.
    - **I-2:** whether `RequiresConfirmation` can take a policy input (the ratchet) without forking the one gate (ADR-039).
    - **I-3:** the durable run record: `sprk_playbookrun` (mcp-r2 FR-13) plus the Decision Record, against the ADR-040 session ledger (T-3).
    - **I-4:** per-user knowledge trimming that UAC provides today.
    - Acceptance: each item gets a section in `notes/phase0-desk-items.md` with `file:line` evidence on master and a one-line Phase 1 implication.
13. **FR-13: The go/no-go record.**
    - Write `phase0/G1-decision.md` with:
      - the G1-NDA and G1-PAT results;
      - G3, G5, G6 and G2;
      - a recommendation for each of D-A7 (where the profile is applied), D-A12 (delivery shape) and D-A13 (compose or recommend);
      - the Phase 1 inputs (the spike's `IActionRunner` is replaced by playbook orchestration).
    - Acceptance: the owner records a decision on D-A7 in design §12 (the owner decides; this record recommends).
14. **FR-14: Clean-up.**
    - Every test agent and package has a removal date. The spike branch's tools are gone from dev, because a later deploy from master replaces them.
    - When the owner confirms removal, the coordination inventory rows are closed.
    - Acceptance: `phase0/README.md` lists every artifact with its removal status; there is no orphan in the tenant inventory.

### Non-Functional Requirements
- **NFR-01, install scope:**
  - test artifacts are installed for the owner and the test user only (R7);
  - tenant and admin clicks are the owner's.
- **NFR-02, content safety:**
  - never put `answer-key/` or the real NDAs (Corteva, RPX/Helios) anywhere an agent can read; reuse the accelerator's `FORBIDDEN_IN_PACKAGE` list;
  - all test content is synthetic or public;
  - no client data.
- **NFR-03, test hygiene:**
  - test action changes on a fresh app id, or wait about 45 minutes after an update;
  - BFF requests are sampled at about 25%, so use several turns per App Insights check;
  - the BFF does not log caller identity, so attribute runs by tester and time.
- **NFR-04, latency:** spike tools answer within 30 s at p95 (the mcp-r2 limit).
- **NFR-05, no secrets:**
  - the header logging in FR-03 logs names only;
  - `Authorization`, cookies and tokens are never logged or committed.
- **NFR-06, dev BFF safety:**
  - a spike deploy contains current master plus mcp-r2's merged work;
  - it goes through uac-r2 with the owner's per-SHA go;
  - the next master deploy removes it.

## Technical Constraints

### Applicable ADRs
- **ADR-028 (A10, connector plane):** test agents use the existing dedicated connector client and OAuth registration. Callers are `systemuser` only, with OBO as the caller. Never change the production OAuth registration A1.
- **ADR-039:** one dispatch protocol and closed catalogues. Profiles may only narrow the catalogue (design T-4).
- **ADR-040:** the session ledger is Tier 3 and erasable. It is not the durable run record (design T-3, I-3).
- **ADR-013:** AI extends the BFF; playbook orchestration is the canonical run path (Phase 1 note in FR-13).
- **ADR-008:** endpoint-filter authorization, inherited by the spike tools through the existing `/mcp/{area}` chain.
- **ADR-052:** no background work in Phase 0.
- **ADR-038:** spike tool tests live on the spike branch only; nothing is added to master's test suites.

### MUST Rules (from ADRs)
- ✅ MUST resolve the caller with `ICallerSystemUserResolver`, and fail closed (`NO_PROFILE`, `CALLER_UNRESOLVED`) rather than fall back to another user's profile.
- ✅ MUST keep every spike tool read-only and behind the existing MCP auth chain (`RequireMcpConnectorToken`, rate limit, kill switch).
- ✅ MUST send an R4 row before every tenant upload.
- ❌ MUST NOT merge the spike branch, or add spike tools to master's tool catalogue.
- ❌ MUST NOT add a second MCP server or route, or a second OAuth registration.
- ❌ MUST NOT treat the host's approval card as a gate (T3/T4 evidence); any gate is Spaarke's own.
- ❌ MUST NOT log request header values, tokens or cookies.

### Existing Patterns to Follow
- **Spike tool:** `src/server/api/Sprk.Bff.Api/Api/Mcp/LegalOperations/Decisions/ShowDecisionsTool.cs`; `.claude/skills/mcp-tool-handler/SKILL.md`.
- **Caller resolution:** `Services/Ai/Context/CallerSystemUserResolver.cs:46`. Profile block style: `Services/Ai/Context/ContextBinder.cs:645` (`ResolveUserFragmentAsync`).
- **Server-side review:** `Services/Ai/LinearConsumers/IActionRunner.cs:25` over `infra/dataverse/actions/agreement-review.action.json`.
- **Test kit and run log:** `projects/specialist-legal-agents-r1/phase0/g8/README.md` (steps, pass rule, App Insights evidence, PowerShell KQL form).
- **Agent build rules:** `notes/agent-package-identity-and-distribution.md` (BR-1 to BR-7) and the `declarative-agent` skill, Step 5.5.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
design.md has **no** `<hot-path-declaration>` block. This is the declaration for Phase 0. Phase 0 merges nothing to the BFF. The spike branch touches `Api/Mcp/` but is never merged.

```xml
<hot-path-declaration>
  <bff>N</bff>
  <spaarkeai>N</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Spike placement (not merged):** the spike tools sit on the existing `/mcp/legal-operations` route inside the BFF. That is the only place Copilot's agents can reach with the A10 connector auth, and the dev deploy is temporary (NFR-06). A spike build may add no package, and its publish size stays at master's.

### New Components (§11 three-question gate)

No new components are merged to master; this is test content and documentation only. For the record, the spike-only tools:

| New component | Existing overlap (`notes/reuse-scan.md`) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `spike_get_profile` (spike branch) | `ICallerSystemUserResolver` (`CallerSystemUserResolver.cs:46`), `IStatedProfileReader` (`StatedProfileReader.cs:39`), `ContextBinder.ResolveUserFragmentAsync` (`:645`) | Yes: it wraps them. The only new part is a static test-profile lookup, because the profile tables are Phase 1 | Without it, G1 variants (a) and (b) and G3 cannot be run, and the D-A7 go/no-go has no evidence |
| `spike_review_nda` (spike branch) | `IActionRunner` (`IActionRunner.cs:25`) over `agreement-review`; `IPlaybookOrchestrationService` (owner chose `IActionRunner` for the spike) | Yes: it wraps `IActionRunner` | Without it, G1 variant (c), the expected winner given T7, cannot be measured |

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-039 | "The LLM never invokes an unlisted tool"; closed catalogues | Copilot's orchestrator chooses among our MCP tools, and the test agents route by instruction | C | The spike tools are cataloged on the existing route, and a profile only narrows them. This is design T-4 as it applies to Phase 0 |
| ADR-028 A10 | Connector clients: a federated connector, a plugin package or a declarative agent, using the dedicated client | The test agents are coded declarative agents with their own MCP action | C | A10 names declarative agents explicitly. They use the same dedicated client and OAuth registration (design T-7(a)) |
| ADR-040 | The session ledger is Tier 3, GDPR-erasable | Run attribution needs a durable record | C (Phase 0 decides nothing) | Phase 0 only investigates (I-3). Phase 1 writes the durable record to `sprk_playbookrun` plus the Decision Record (design T-3) |

No other ADR tensions surfaced for Phase 0. T-1, T-2, T-6 and T-8 apply to later phases, or to dropped items (G11).

## Success Criteria

1. [ ] G1-NDA has a recorded verdict per variant and surface, with the thresholds applied. Verify by: `phase0/g1-nda/README.md` results sheet, with App Insights evidence.
2. [ ] G3 answers "can we key a run" and "can we identify the agent", and fail-closed is proven. Verify by: `phase0/g3/README.md`, plus the spike tool tests on the branch.
3. [ ] G5 baseline and main test are recorded against the 95% / 0-controls rule. Verify by: `phase0/g5/README.md`.
4. [ ] G6 is recorded, including invoke vs recommend, and the results are sent to accelerator-r1. Verify by: `phase0/g6/README.md`, plus the sent message.
5. [ ] The G2 remainder questions are answered. Verify by: `phase0/g2/README.md`.
6. [ ] The patent test content exists, and the Cowork drafting spike is judged by the owner per standard. Verify by: `phase0/patents/`, plus the owner's sheet.
7. [ ] G1-PAT confirms or contradicts G1-NDA. Verify by: `phase0/g1-pat/README.md`.
8. [ ] I-1 to I-4 are written with `file:line` evidence. Verify by: `notes/phase0-desk-items.md`.
9. [ ] The go/no-go record exists, and the owner has decided D-A7. Verify by: `phase0/G1-decision.md`, plus design §12.
10. [ ] No orphan tenant artifacts; the spike is gone from dev. Verify by: the coordination inventory, plus a dev BFF tool list showing no `spike_*` tools.

## Dependencies

### Prerequisites
- **Gate A0:** clear (2026-11-24); the accelerator was told on 2026-11-25.
- **mcp-r2's merged work** on master before the spike branch is cut, so the dev deploy rolls nothing back.
- **The dev BFF deploy slot:** uac-r2's session, with the owner's go for each SHA.
- **The G8 test artifacts** removed by 2026-11-30, so old agents don't confuse the tests.
- **The `agreement-review` Action row in DEV Dataverse:** the spike task checks it.

### External Dependencies
- Microsoft 365 Copilot licences for the owner and the test user (T11).
- Legal for Cowork switched on for both (owner, admin center).
- The accelerator NDA content and the T-P2 prompt set, read from `work/spaarke-legal-ai-accelerator-r1`.
- COPILOT-PLATFORM-COORDINATION for R4 rows and inventory closure.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| NDA owner questions (2026-11-24) | Do they still block the Phase 0 spec? | Superseded by OD-2 (2026-11-25) | No blocker |
| G1 test bed | NDA (content exists) or patents (content doesn't exist yet)? | **Both** | G1-NDA is the go/no-go; G1-PAT repeats it; FR-09 builds minimal patent test content |
| G1 test tools | Spike branch, or production code on master? | **Spike branch, never merged** | FR-02; no Phase 0 BFF merge; no overlap with mcp-r2's `Api/Mcp/` |
| Phase 0 scope | Which partly answered items stay? | G2 remainder and G3 stay. G4 Outlook, G10 and G11 are out ("do not invest in Copilot Studio or SPFx") | FR-03, FR-08; Out of Scope |
| Test effort | How much manual prompt-running? | **Staged, small** | Pilots of 10, early stop below 90%, about 40 runs per surface for survivors |
| Reuse: server-side review | `IActionRunner` or playbook orchestration? The owner asked which is more robust | Playbook orchestration is more robust for production (canonical, durable runs, longer than 30 s); `IActionRunner` is right for the throwaway spike | FR-02 uses `IActionRunner`; FR-13 carries playbook orchestration into Phase 1 |

## Assumptions

- **G1-PAT scope:** it repeats only the G1-NDA variants (a) and/or (b) still in contention; no patent server-side tool. Affects FR-11.
- **Thresholds:** ≥95% adherence, 0 ratchet escapes and 0 bleed (G1); ≥95% routing and 0 functions on controls (G5). These are the design's numbers, which the owner did not change. Affects FR-04, FR-06 and FR-11.
- **Patent content:** drawn from published patents, labelled `status: test`, with no attorney review in Phase 0. The owner judges the drafting spike. A patent attorney is needed from Phase 1 (golden set). Affects FR-09 and FR-10.
- **Testers:** the owner and the test user (`demo@`) run the prompts; profile A is keyed to the owner and profile B to the test user. Affects FR-04.

## Unresolved Questions

- [ ] Whether the `agreement-review` Action row is deployed in DEV Dataverse. Blocks: FR-02 `spike_review_nda`. The spike task checks it first and reports it if it is missing.
- [ ] When mcp-r2's Phase 0 merges land on master, which sets the spike branch's base. Blocks: the FR-02 dev deploy. It does not block writing the kits (FR-01, FR-09) or the desk items (FR-12).
- [ ] Who reviews the patent golden set from Phase 1. Blocks: nothing in Phase 0; it is recorded so Phase 1 can line someone up early.

---

*AI-optimized specification. Original design: design.md*
