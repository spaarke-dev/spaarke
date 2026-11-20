# Task 043 — per-session C# language-server A/B trial

> spec FR-13 · design.md §7.6 (gates) · owner OK to install: "Yes, install it" (2026-11-15).
> Plugin under trial: `csharp-lsp@claude-plugins-official` (Anthropic), which runs `csharp-ls` (MIT, `dotnet tool install --global csharp-ls`).
> This trial does not reopen the shared-index rejection (design.md §10.6).

## 1. Questions and checked answers (written before either arm ran)

**Scope for every question**: the `Sprk.Bff.Api` project only (`src/server/api/Sprk.Bff.Api/`), at commit `71df6a2f44`. A "reference" is a code reference that compiles; XML-doc `cref`s, comments, JSON and test projects do not count. A Grep or LSP answer is **correct** when it names exactly the set below (no misses, no extras). Answering "none" or missing an item for a reference/implementation question is a **false "no references"** when the true set is non-empty.

**How the answers were checked (compiler, not reading).** In a throwaway detached worktree of `71df6a2f44`:
- implementations: a probe member was added to each interface, so every implementing class failed with CS0535 (one build; no derived or abstract implementors exist, checked);
- references and the signature change: each symbol was renamed (interface and implementation together) or given an extra required parameter, so every use site failed (CS0117 / CS1061 / CS7036; second build, because the compiler skips method bodies when declarations have errors);
- receiver types: `int probe = <variable>;` made the compiler print the exact type (CS0029).

The worktree was then removed. Sources of the questions: this project's sessions (the `ExecutorType` rename behind ISS-001, item 2's "does an implementation already exist" reuse scan, the 080 review), and the root CLAUDE.md entry points (`CoreAncestorResolver`, `GraphClientFactory`).

| # | Kind | Question | Checked answer |
|---|---|---|---|
| Q1 | find-references | Every call site of `IGraphClientFactory.ForUserBetaAsync` (or `GraphClientFactory.ForUserBetaAsync`). | **1**: `Infrastructure/Graph/SpeAdminGraphService.cs:2255` |
| Q2 | find-references | Every code reference to `CoreAncestorResolver.IsStampedChildEntity`. | **4**: `Services/Dataverse/CoreAncestorRestamper.cs:122`; `Services/Dataverse/CoreAncestorRestampJobHandler.cs:47`; `Services/RecordCreation/RecordCreationService.cs:994`, `:1077` |
| Q3 | find-references | Every code reference to the enum member `ExecutorType.QueryDataverse`. | **5**: `Services/Ai/Nodes/INodeExecutor.cs:359`; `Services/Ai/Nodes/QueryDataverseNodeExecutor.cs:50`, `:57` (`nameof`), `:58`; `Services/Ai/PlaybookOrchestrationService.cs:2403`. Not counted: the `cref` at `PlaybookOrchestrationService.cs:2392`, the JSON comment in `matter-health-single.playbook.json:77` |
| Q4 | find-references | Every call site of `INodeExecutorRegistry.GetExecutor` (or `NodeExecutorRegistry.GetExecutor`). | **1**: `Services/Ai/PlaybookOrchestrationService.cs:1199` |
| Q5 | find-implementations | Every class implementing `INodeExecutor`. | **25**: AgentServiceNodeExecutor, AiAnalysisNodeExecutor, AiCompletionNodeExecutor, ConditionNodeExecutor, CreateNotificationNodeExecutor, CreateTaskNodeExecutor, DeclineToFindNode, DeliverCompositeNodeExecutor, DeliverOutputNodeExecutor, DeliverToIndexNodeExecutor, EntityNameValidatorNodeExecutor, EvidenceSufficiencyNode, GroundingVerifyNode, IndexRetrieveNode, LiveFactNode, LoadKnowledgeNodeExecutor, LookupUserMembershipNodeExecutor, ObservationEmitterNodeExecutor, QueryDataverseNodeExecutor, ReturnInsightArtifactNode, ReturnResponseNodeExecutor, SanitizerNodeExecutor, SendEmailNodeExecutor, StartNodeExecutor, UpdateRecordNodeExecutor |
| Q6 | find-implementations | Every class implementing `IJobHandler`. | **23**: AppOnlyDocumentAnalysisJobHandler, AssignedAccessCreateGrantJobHandler, AssignTaskEmailJobHandler, AttachmentClassificationJobHandler, BillingRuleDraftJobHandler, BulkRagIndexingJobHandler, CoreAncestorRestampJobHandler, DocumentProcessingJobHandler, EmailAnalysisJobHandler, GateDecisionRecordJobHandler, IncomingCommunicationJobHandler, IncomingMessagingJobHandler, InsightsIngestJobHandler, InvoiceExtractionJobHandler, InvoiceIndexingJobHandler, InvoiceSubmissionProcessingJobHandler, InvoiceSubmissionRerunJobHandler, MembershipReconcileJob, ProfileSummaryJobHandler, RagIndexingJobHandler, RelocationReleaseRetryJobHandler, SignalEvaluationTriggerHandler, SpendSnapshotGenerationJobHandler |
| Q7 | find-implementations | Every class implementing `ISprkChatAgent`. | **6**: AgentContentSafetyMiddleware, AgentCostControlMiddleware, AgentServiceRoutingMiddleware, AgentTelemetryMiddleware, PromptShieldChatMiddleware, SprkChatAgent |
| Q8 | receiver type | The static type of `analysis` at `Services/Ai/AnalysisOrchestrationService.cs:94`. | `Sprk.Bff.Api.Services.Ai.AnalysisInternalModel` |
| Q9 | receiver type | The static type of `scopes` at `Services/Ai/AnalysisOrchestrationService.cs:403`. | `Sprk.Bff.Api.Services.Ai.ResolvedScopes` |
| Q10 | signature change | What stops compiling if `CoreAncestorResolver.PartyRegardingColumnNames(string entity)` gains a second required parameter? | **10** call sites: `Infrastructure/Dataverse/RecordContainerResolver.cs:1130`; `Services/Communication/IncomingAssociationResolver.cs:702`, `:746`; `Services/Dataverse/CoreAncestorRestamper.cs:300`, `:405`, `:553`, `:683`, `:811`; `Services/Dataverse/CoreAncestorStampReconciliationJob.cs:375`; `Services/RecordCreation/RecordCreationService.Repair.cs:849` |

Text-search traps these contain (why they are fair tests): Q1 and Q4 match declarations, a registry with the same method name and comments; Q3 has a `cref` and a JSON comment; Q5–Q6 have files that mention the interface without implementing it (text grep finds 25 / 26 files for 25 / 23 true implementors).

## 2. Setup and time-box

- **Clock start** (install): 2026-11-16 04:04Z (23:04 local on 2026-11-15). Time-box: half a day; all measurements were done by about 04:30Z, well inside it.
- **Server**: `dotnet tool install --global csharp-ls` → `csharp-ls` 0.28.0 (`%USERPROFILE%\.dotnet\tools`), 9 s.
- **Plugin**: `csharp-lsp@claude-plugins-official` 1.0.0, installed with `claude plugin install … --scope local`. It writes only `.claude/settings.local.json`, which is gitignored (`.gitignore:112`). **Nothing is committed** (POML negative criterion).
  - The global `claude` CLI on PATH (2.1.6) could not install it ("not found in marketplace"): its marketplace cache is the desktop app's snapshot. The desktop app's own Claude Code (2.1.325) installed it at once. A teammate on an old global CLI hits the same error and should update the CLI first.
- **How the arms run**: headless `claude -p` from the desktop app's 2.1.325 binary, in this worktree, `--model sonnet` (resolved to `claude-sonnet-5-5`), `--setting-sources local` so project hooks do not run in either arm, `--output-format stream-json` for the measurements.
  - Grep arm: Read/Grep/Glob, plugin disabled for that run.
  - LSP arm: the same plus the `LSP` tool.
  - Both arms get the same prompt text (the 10 questions and the scope rules above, without answers), except for one line naming the tools.
- **Smoke check** (a symbol outside the 10 questions: `CoreAncestorResolver.IsStampedOutsideAccessTaxonomy`): `findReferences` answered with the declaration plus 4 test-project references. That agrees with a text search (no other `src/` hit).
- **Cold start** (context, not a gate): `csharp-ls` loads all of `Spaarke.sln` (15 projects) on first use. The plugin's request limit is 60 s, so the first three `findReferences` calls timed out and the fourth answered, about 3–3.5 minutes after the session started.
  - An agent that does not retry gets no answer and must fall back (the first smoke run gave up after one timeout).
  - Every new session pays this again: each session starts its own server.

## 3. Results per question

Three runs, all headless Sonnet (`claude-sonnet-5-5`) in this worktree on 2026-11-16, scored against §1.

| # | Grep arm | LSP arm, run 1 (no warm-up) | LSP arm, run 2 (warm-up, LSP primary) |
|---|---|---|---|
| Q1 | ✅ | ✅ (from Grep) | ✅ findReferences, test-project hits filtered |
| Q2 | ✅ | ✅ (from Grep) | ✅ findReferences |
| Q3 | ✅ | ✅ (from Grep) | ✅ findReferences, but LSP returned the `cref` at `:2392` as a reference; Grep was needed to drop it (it first wrote "8", then corrected to the right 5) |
| Q4 | ✅ | ✅ (from Grep) | ✅ findReferences |
| Q5 | ✅ 25 | ✅ (from Grep) | ✅ goToImplementation |
| Q6 | ✅ 23 | ✅ (from Grep) | ✅ goToImplementation |
| Q7 | ✅ 6 | ✅ (from Grep) | ✅ goToImplementation |
| Q8 | ✅ by reading the return type | ✅ (from Grep; the one LSP hover timed out) | ✅ hover: `AnalysisInternalModel?` (the nullable annotation `var` carries; same type) |
| Q9 | ✅ by reading the return type | ✅ (from Grep) | ✅ hover: `ResolvedScopes?` |
| Q10 | ✅ 10 (heading said 11, corrected in the same answer) | ✅ (from Grep) | ✅ findReferences |
| **Score** | **10/10** | 10/10, but it is a Grep score | **10/10** |
| False "no references" | 0 | 0 | 0 |

| Measure (context, not gates) | Grep arm | LSP run 1 | LSP run 2 |
|---|---|---|---|
| Wall time | 46 s | 114 s | 278 s (about 190 s of it cold start) |
| Tool calls | 16 (Grep 14, Read 2) | 17 (Grep 13, Read 2, LSP 1, ToolSearch 1) | 29 (LSP 16, Grep 8, Read 4, ToolSearch 1) |
| LSP operations | — | hover 1 (timed out) | documentSymbol 4 (3 timed out), findReferences 5, goToImplementation 3, hover 3, goToDefinition 1 |
| Output tokens | 7,119 | 7,512 | 9,608 |
| Cache-read tokens | 346,806 | 404,389 | 916,603 |
| Cost (reported) | $0.23 | $0.24 | $0.37 |

What the runs show:
- **Run 1 is a finding, not a result.** Even when told to retry, Sonnet gave up on LSP after one cold-start timeout and answered from Grep. In everyday use, the server goes unused unless it is already warm or the prompt insists.
- **Run 2 (LSP made primary) matched Grep, with no gain.** Every question was already answerable by Grep on this codebase: names are unique enough, and the traps (`cref`, comments, JSON, declarations) are easy to read past. LSP had a trap of its own: `findReferences` counts an XML-doc `cref`, so the agent still needed Grep to apply the scope rules.
- **LSP costs more:** 1.6× the cost and 6× the wall time of the Grep arm, mostly cold start plus results that are mainly test-project hits to filter.

## 4. Memory

Sampled every 10 s: each `csharp-ls` process **plus its child processes** (the MSBuild BuildHost) as working set and private bytes, and the machine's free RAM. Machine: 61.6 GB visible (63,127 MB). The first sampler counted `csharp-ls` alone and read about 120 MB; that missed the server's real load and is discarded.

- **1 session** (LSP run 2): working set rises during the solution load and levels off at **1,914–1,922 MB** (private about 1,735 MB) for the rest of the run. Steady state ≈ **1.9 GB**.
- **3 concurrent sessions** (same short LSP script each, 2026-11-16 04:23–04:27Z): every session warmed up in 3 attempts and returned identical results. Peak combined working set **4,806 MB** (each server 1.6–1.7 GB and still rising when its session ended, so steady state is about 3 × 1.9 ≈ 5.8 GB). Free RAM at that peak: **24,546 MB = 38.9%**. With the projected 5.8 GB, about 37%.
- **Caveat (other load):** free RAM on this machine swings with other sessions' builds and tests. It was 17.2 GB (27%) just before the 3-session run, and as low as 3.4 GB (5%) during the single-session run. Three language servers add about 5.8 GB (9% of RAM). That is enough to tip a busy hour below 25% free, even though the gate passed when it was measured.

## 5. Gates and recommendation

| Gate (design.md §7.6) | Threshold | Measured | Result |
|---|---|---|---|
| Correctness | ≥ 9/10 with LSP, none worse than Grep | 10/10, every answer equal to Grep's | ✅ pass |
| False "no references" | zero | zero | ✅ pass |
| Memory | ≤ 2 GB per session at steady state; ≥ 25% free with 3 sessions | 1.9 GB; 38.9% free at the 3-session peak (about 37% projected at steady state) | ✅ pass, narrowly per session; the 25% depends on other load |

**All three gates pass, but the gain §7.6 expected did not show up.** The goal was fewer wrong answers and fewer tool calls. Instead, Grep alone was already 10/10. The LSP arm made more tool calls (29 vs 16), cost 1.6× as much, took 6× the wall time, and still needed Grep to apply the scope rules (it reports `cref`s and test projects). Each session also pays about 3 minutes of cold start, and an agent that is not told to retry quietly falls back to Grep anyway (run 1).

**Why Grep was enough here:** the names are distinctive. The questions came from real sessions, and in this codebase real questions mostly concern uniquely named symbols, so text search with a careful reader is exact. LSP would earn its cost on heavily overloaded or generic names (`ExecuteAsync`, `Validate`, extension methods), which these sessions did not ask about.

**Recommendation: do not adopt.** It passes the gates, but adds no measured value. A project-wide setting would make every session pay about 1.9 GB and a 3-minute load for nothing, across about 17 worktrees. An "opt-in per session" option was considered and dropped: it would be a how-to for a use case nobody has hit.

## 6. Owner decision

**2026-11-16 — Do not adopt** (owner: "yes agree - don't adopt. ensure we have this documented in case it is evaluated again in the future").
- Uninstalled the same day: `claude plugin uninstall csharp-lsp@claude-plugins-official --scope local`, `dotnet tool uninstall --global csharp-ls`. The `.claude/settings.local.json` that the install had created was removed. No repo configuration was ever changed.
- Repo-wide record: `docs/procedures/testing-and-code-quality.md`, "Agent Code-Navigation Tools", points here.

## 7. Evaluating again

**Re-run it when** one of these is true:
- agents get navigation questions wrong on overloaded or generic names (`ExecuteAsync`, `Validate`, extension methods, partial classes), which this question set did not cover;
- the plugin's request limit (60 s) or the server's cold start (≈ 3 min on `Spaarke.sln`) changes;
- a different C# server appears in the official marketplace.

**What to change next time:**
- Add 3–4 questions on overloaded names. That is the only place LSP can beat Grep.
- Re-check the answers with the compiler probes in §1 at the commit you test; line numbers drift.
- Always warm the server first, and tell the agent to make LSP its primary tool (run 1 shows it falls back to Grep otherwise).
- Sample memory across the `csharp-ls` process **tree**, not the single process.

**Steps (about an hour):**
1. `dotnet tool install --global csharp-ls`
2. `claude plugin install csharp-lsp@claude-plugins-official --scope local` from this worktree. Use the desktop app's bundled Claude Code (`%APPDATA%\Claude\claude-code\<version>\…\claude.exe`) or a current CLI; the 2.1.6 global CLI could not see the plugin.
3. Run each arm with the runner below (`-Arm grep`, then `-Arm lsp`), and extract the measures with the metrics script.
4. For memory, run the sampler alongside one LSP arm, then alongside three concurrent LSP sessions.
5. Uninstall both, as in §6, and remove `.claude/settings.local.json` if the install created it.

### Harness (copied from the 2026-11-16 run)

Arm runner (`run043.ps1`):

```powershell
param(
  [Parameter(Mandatory)][string]$PromptFile,
  [Parameter(Mandatory)][ValidateSet('lsp','grep')][string]$Arm,
  [Parameter(Mandatory)][string]$OutFile
)
$ErrorActionPreference = 'Stop'
$cc = '<path to a current claude.exe>'
Set-Location '<worktree root>'
$prompt = Get-Content $PromptFile -Raw
$cliArgs = @('-p', $prompt, '--model', 'sonnet', '--output-format', 'stream-json', '--verbose',
          '--setting-sources', 'local', '--permission-mode', 'default')
if ($Arm -eq 'lsp') {
  $cliArgs += @('--allowedTools', 'Read,Grep,Glob,LSP')
} else {
  $cliArgs += @('--settings', '{"enabledPlugins":{"csharp-lsp@claude-plugins-official":false}}',
             '--allowedTools', 'Read,Grep,Glob', '--disallowedTools', 'LSP')
}
$t0 = Get-Date
& $cc @cliArgs 2>&1 | Out-File -FilePath $OutFile -Encoding utf8
"WALL_SECONDS=$([int]((Get-Date) - $t0).TotalSeconds)" | Out-File -FilePath $OutFile -Append -Encoding utf8
```

Prompt template. Both arms get the same text. `TOOLS_LINE` is the only difference.

```text
Answer the 10 code-navigation questions below about this repository. Do not edit any file.

Scope rules for every question:
- Only the project `src/server/api/Sprk.Bff.Api/` counts. Ignore test projects and any other project.
- A "reference" or "call site" is a code reference that the C# compiler binds (including `nameof(...)`). Do NOT count XML-doc `cref`s, comments, string literals, JSON files or the declaration itself.
- Be exact: list every item, and nothing extra. If you are unsure whether you found everything, say so in that question's answer rather than guessing.
- Paths relative to `src/server/api/Sprk.Bff.Api/`; lines 1-based as in the current files.

TOOLS_LINE

Questions: (Q1–Q10 as in §1, without the answers)

Output format: one section per question, headed `Q1` ... `Q10`, then the count and the list. End with one line per question saying which tool(s) produced the answer.
```

- Grep arm `TOOLS_LINE`: `Tools: you have Read, Grep and Glob only.`
- LSP arm `TOOLS_LINE` (run 2, the one that counts):

```text
Tools: the LSP tool (C# language server: findReferences, goToImplementation, goToDefinition, hover, documentSymbol, workspaceSymbol and so on) plus Read, Grep and Glob.
This is a test of the language server, so:
1. WARM-UP FIRST: before answering anything, call LSP documentSymbol on src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs and then LSP hover on a symbol in it. The server loads the whole solution on first use and each call times out after 60 s for the first few minutes. Retry until a call answers with real data, up to 10 attempts. Report how many attempts the warm-up took.
2. Then answer every question with LSP as the primary tool: findReferences for Q1-Q4 and Q10, goToImplementation (or findReferences on the interface) for Q5-Q7, hover for Q8-Q9. You may use Grep or Read only to find a symbol's line and character position, or to filter LSP results to the scope rules (for example, to drop test-project results). Do not answer a question from Grep alone; if LSP fails for a question after 3 retries, say "LSP failed" for it and then give your Grep-based answer marked as such.
```

Metrics extraction (reads the runner's stream-json output):

```powershell
param([Parameter(Mandatory)][string]$OutFile)
$raw = Get-Content $OutFile
$wall = ($raw | Where-Object { $_ -like 'WALL_SECONDS=*' }) -replace 'WALL_SECONDS=', ''
$lines = $raw | Where-Object { $_.StartsWith('{') } | ForEach-Object { $_ | ConvertFrom-Json -AsHashtable }
$init = $lines | Where-Object { $_.type -eq 'system' -and $_.subtype -eq 'init' } | Select-Object -First 1
$uses = $lines | Where-Object { $_.type -eq 'assistant' } | ForEach-Object { $_.message.content } | Where-Object { $_.type -eq 'tool_use' }
$r = $lines | Where-Object { $_.type -eq 'result' } | Select-Object -Last 1
[ordered]@{
  model = $init.model; lspToolLoaded = ($init.tools -contains 'LSP'); wallSeconds = [int]$wall
  turns = $r.num_turns; toolCalls = @($uses).Count
  byTool = (($uses | Group-Object { $_.name } | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ', ')
  lspOps = (($uses | Where-Object { $_.name -eq 'LSP' } | Group-Object { $_.input.operation } | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ', ')
  outputTokens = $r.usage.output_tokens; cacheRead = $r.usage.cache_read_input_tokens; costUsd = $r.total_cost_usd
} | ConvertTo-Json
$r.result
```

Memory sampler (every `csharp-ls` process tree, plus machine free RAM):

```powershell
param([Parameter(Mandatory)][string]$OutCsv, [int]$Seconds = 1800, [int]$Every = 10)
"utc,rootPid,procs,wsMB,privateMB,freeMB,totalMB" | Out-File $OutCsv -Encoding utf8
$total = [int]((Get-CimInstance Win32_OperatingSystem).TotalVisibleMemorySize / 1KB)
$end = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $end) {
  $free = [int]((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1KB)
  $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, WorkingSetSize, PrivatePageCount
  $now = (Get-Date).ToUniversalTime().ToString('HH:mm:ss')
  foreach ($r in ($all | Where-Object { $_.Name -eq 'csharp-ls.exe' })) {
    $tree = @($r); $frontier = @($r.ProcessId)
    while ($frontier.Count) {
      $kids = @($all | Where-Object { $frontier -contains $_.ParentProcessId })
      $tree += $kids; $frontier = @($kids | ForEach-Object { $_.ProcessId })
    }
    $ws = [int](($tree | Measure-Object WorkingSetSize -Sum).Sum / 1MB)
    $pv = [int](($tree | Measure-Object PrivatePageCount -Sum).Sum / 1MB)
    "$now,$($r.ProcessId),$($tree.Count),$ws,$pv,$free,$total" | Out-File $OutCsv -Append -Encoding utf8
  }
  Start-Sleep -Seconds $Every
}
```
