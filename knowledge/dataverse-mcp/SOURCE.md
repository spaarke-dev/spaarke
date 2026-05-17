# SOURCE — `dataverse-mcp`

> **Curated**: 2026-05-14 by AI agent · **Topic**: Dataverse-as-MCP-server, Business Skills, App MCP, custom tools
> **Owner**: Ralph Schroeder · **Next refresh**: monthly (first business day)

---

## Why this curation has unusually heavy `docs/` and light `samples/`

Dataverse MCP, Business Skills (Dataverse intelligence), and App MCP are **all recent or preview** Microsoft features (GA timeline starts December 15, 2025 for billing; Business Skills still preview as of 2026-05-14). At curation time, Microsoft has **not** published a sample-code repo dedicated to MCP tool authoring or App MCP. The available material breaks down as follows:

- **Microsoft Learn docs** — well-documented setup, tool list, and admin paths. We snapshot the 6 canonical Learn pages.
- **`microsoft/Dataverse-MCP` (Build 2025 labs)** — three labs covering env setup, Claude desktop, and VS Code. These are *configuration* tutorials, not custom-tool samples.
- **`microsoft/dataverse-business-skills`** — the canonical example library of the Markdown-with-YAML Business Skill authoring format. 16 production-style skills + test scenarios. This is the only place to learn the format by example.
- **`microsoft/Dataverse-skills`** — a Claude Code / GitHub Copilot **plugin** that wraps the Dataverse MCP server, PAC CLI, Python SDK. Useful for understanding tool routing but not a sample of MCP tool authoring.
- **`microsoft/PowerApps-Samples`** — no MCP, business-skill, or app-mcp subdirectories at curation time. Verified via filename search.

Per the curation directive: *"Dataverse MCP / Business Skills / App MCP are recent (some preview). It's likely samples are sparse — that's expected. GAP-log honestly and let the `docs/` snapshots carry weight."*

---

## Source repositories cloned

| Repo | Commit SHA (2026-05-14) | Default branch | What we kept |
|---|---|---|---|
| [`microsoft/PowerApps-Samples`](https://github.com/microsoft/PowerApps-Samples) | `a6d30c10d17938fbeb85245e57a4a2cb435c71c8` | `master` | **Nothing copied** — no MCP / business-skill / app-mcp subdirs found. Listed only for provenance: we verified absence. |
| [`microsoft/Dataverse-MCP`](https://github.com/microsoft/Dataverse-MCP) | `2bc5d0098b8ff49a7228aaf2a08df426a060ef6c` | `main` | The verbatim Claude-desktop transcripts from `lab02/README.md` were quoted into `samples/mcp-invocations/standard-tool-invocations.md`, and the legacy .NET-tool config from the same lab was preserved as `samples/mcp-client-config/local-proxy-dotnet-tool-claude-config.json`. |
| [`microsoft/dataverse-business-skills`](https://github.com/microsoft/dataverse-business-skills) | `10016dd09acfa5f4dfce84de4e34ef294961faaf` | `main` | Two SKILL.md files + one test-scenarios file (see Curated files below). |
| [`microsoft/Dataverse-skills`](https://github.com/microsoft/Dataverse-skills) | `b2f21c1eec233d1b20e89618c3ffcb25cfdd55e4` | `main` | **Nothing copied** — referenced for routing context in NOTES.md but the plugin source itself is out-of-scope for this topic (it's a Claude Code plugin, not a Dataverse MCP authoring sample). |

> **Note on directive vs. reality**: The original directive listed `microsoft/Dataverse-Web-API-Samples`. That repository **does not exist** under that name on 2026-05-14 — `gh repo view` returns "Could not resolve to a Repository." A search for "Dataverse" under the `microsoft` org surfaced the three additional repos above. We treated these as the canonical replacements and clearly attribute everything pulled from them.

---

## Microsoft Learn pages snapshotted (in `docs/`)

All fetched via WebFetch on **2026-05-14**. Each snapshot has YAML frontmatter recording `source`, `fetched`, the `ms.date` declared on the page, and the GitHub commit of the `MicrosoftDocs/powerapps-docs-pr` source markdown.

| File in `docs/` | Source URL | `ms.date` | Why it matters |
|---|---|---|---|
| `data-platform-mcp.md` | https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp | 2026-03-30 | **Landing page** — overview, MCP server URL format, full tool catalog, metering rules. |
| `data-platform-mcp-disable.md` | https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-disable | 2026-05-04 | Admin enable/disable, client allowlisting, and (notably) the full **agent instruction authoring guidance** including the Microsoft-published sample system prompt. |
| `data-platform-mcp-vscode.md` | https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-vscode | 2026-03-09 | VS Code Copilot + Copilot CLI configuration. |
| `data-platform-mcp-other-clients.md` | https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-other-clients | 2026-03-07 | **Most operationally relevant for Spaarke** — npm local-proxy and remote-endpoint paths for Claude Code and Claude desktop. Includes tenant admin consent URL and the Dataverse CLI app-ID (`0c412cc3-0dd6-449b-987f-05b053db9457`). |
| `data-platform-mcp-preview-tools.md` | https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-preview-tools | 2026-01-06 | Preview endpoint (`/api/mcp_preview`), **required** to use Business Skills. |
| `data-platform-intelligence.md` | https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-intelligence | 2026-03-30 | Reached via redirect from `https://aka.ms/DVinWorkIQLearnMore`. The umbrella page for Business Skills / Work IQ extension. Includes a curator-authored **GAP-log** at the bottom of the snapshot listing the original directive URLs that 404'd. |

### Original directive URLs that 404'd on 2026-05-14

Logged here for the next refresh; if Microsoft publishes these later, they should slot into `docs/`:

| Directive URL | Status (2026-05-14) | Probable replacement |
|---|---|---|
| `https://learn.microsoft.com/en-us/power-platform/dataverse/mcp-server` | 404 | `power-apps/maker/data-platform/data-platform-mcp` (captured) |
| `https://learn.microsoft.com/en-us/power-platform/dataverse/business-skills` | 404 | `power-apps/maker/data-platform/data-platform-intelligence` (captured); detailed authoring format is **only in the GitHub repo** |
| `https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/app-mcp` | 404 | No published Learn page found |
| `https://learn.microsoft.com/en-us/power-platform/dataverse/mcp-server-custom-tools` | 404 | No published Learn page found; admin doc hints custom tools surface as **MCP-named custom Dataverse APIs** |

---

## Curated files

### `samples/business-skill/`

| File | Origin | What it demonstrates |
|---|---|---|
| `SKILL.md` | `microsoft/dataverse-business-skills` @ `10016dd0…` → `skills/account-briefing-generator/SKILL.md` (unmodified copy) | **Reference example** of the Markdown-with-YAML Business Skill authoring format. ~680 lines. Shows YAML frontmatter (`name`, `description`, `metadata.author/version/category`), the embedded "use when" trigger phrases in `description`, T-SQL queries embedded as code blocks for the agent to execute via `read_query`, schema field documentation, and multi-mode output templates. |
| `log-call-transcripts-SKILL.md` | Same repo → `skills/log-call-transcripts/SKILL.md` (unmodified copy) | **Smaller, more focused example** of the same format (~290 lines). Better starting point for understanding the minimum-viable skill shape. |
| `account-briefing-test-scenarios.md` | Same repo → `test-scenarios/10-account-briefing-tests.md` (unmodified copy) | Microsoft's test pattern for Business Skills — pre-conditions, prompts, expected behavior, pass criteria. Spaarke can adopt this pattern when authoring its own skills. |

### `samples/mcp-invocations/`

| File | Origin | What it demonstrates |
|---|---|---|
| `standard-tool-invocations.md` | Hand-composed by curator using **verbatim transcripts** from `microsoft/Dataverse-MCP/lab02/README.md` (`2bc5d009…`) and the tool catalog from `docs/data-platform-mcp.md` | Three real prompts and Claude's actual responses for `list_tables`, `read_query` (empty result), and `create_record` (bulk-as-loop). Plus the full GA tool catalog and the metering rule. All quoted material is explicitly attributed. |

### `samples/mcp-client-config/`

| File | Origin | What it demonstrates |
|---|---|---|
| `claude-desktop-config.json` | `docs/data-platform-mcp-other-clients.md` (verbatim) | Current Learn-documented Claude desktop config using `@microsoft/dataverse` npm proxy. |
| `claude-code-add-command.sh` | Same source (verbatim) | The single `claude mcp add` command for Claude Code, plus the `--preview` variant required for Business Skills. |
| `github-copilot-cli-mcp-config.json` | `docs/data-platform-mcp-vscode.md` (verbatim) | HTTP transport directly against `/api/mcp` — no local proxy needed for the Microsoft GitHub Copilot client. |
| `local-proxy-dotnet-tool-claude-config.json` | `microsoft/Dataverse-MCP/lab02/README.md` (verbatim) | **Legacy alternate** — the original .NET dotnet-tool proxy (`Microsoft.PowerPlatform.Dataverse.MCP`) shipped with Build 2025. Kept so curators can recognize the older form. The npm proxy is now the recommended path. |

---

## GAPs explicitly logged (re-list for visibility)

1. **No Microsoft sample of a custom MCP tool** (let alone one with a widget) was found in `PowerApps-Samples` or `Dataverse-MCP` at curation time. Per directive, the widget example is **deferred to `knowledge/mcp-apps/`**.
2. **No dedicated Microsoft Learn page** for Business Skill authoring format (`description`, YAML frontmatter conventions, "use when" / "do not use when" phrasing) — the only normative source is the example library in `microsoft/dataverse-business-skills`.
3. **No dedicated Microsoft Learn page** for "App MCP" as a distinct concept. The admin doc's hint that *"MCP‑named custom APIs are regular Dataverse APIs"* suggests App MCP custom tools are implemented as **custom Dataverse APIs that the MCP server surfaces** — but no published example confirms this yet.

---

## Refresh checklist for next month

- [ ] Re-fetch all six `docs/*.md` pages; check `ms.date` against captured value.
- [ ] Re-clone `microsoft/dataverse-business-skills`; diff against `samples/business-skill/`; refresh if the example skills changed shape.
- [ ] Re-clone `microsoft/Dataverse-MCP`; check if new labs (lab 04+) ship.
- [ ] Re-check `microsoft/PowerApps-Samples` for any new `mcp/`, `app-mcp/`, or `business-skills/` subdirs.
- [ ] Re-check the four 404'd directive URLs — any of them may have shipped.
- [ ] Look for **App MCP** GA / docs and a **widget-attached custom tool** sample. Both are the highest-value gaps.
- [ ] Verify the npm package `@microsoft/dataverse` still exists at its current major version; if it ships breaking changes, update `samples/mcp-client-config/`.
