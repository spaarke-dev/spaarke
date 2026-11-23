# Specialist Legal Agents — Design Synopsis

Nov 22, 2026 · @Ralph Schroeder

## Summary

**Build one governed runtime and many attorney-owned Role Profiles, not many bespoke agents.** A "CRE partner" or a "biotech patent claim-drafting associate" is a versioned configuration document (charter, positions, knowledge scopes, skills, tools, authority limits, output contracts) that a shared runtime compiles at invocation. The attorney owns and edits the profile; the platform owns the guardrails the profile cannot loosen. Attorneys meet the agent in Microsoft 365 Copilot, through plugin-packaged declarative agents backed by the Spaarke BFF, with a custom engine agent as the pro-code fallback.

Three decisions carry the design:

1. **Profile layering with a one-way ratchet.** Platform, archetype, firm policy, owner and matter layers merge in that order. Lower layers may narrow authority and tighten gates; they can never widen them.
2. **Attorney tuning is proposal-and-approve, never silent learning.** Conversation, redline corrections and example documents all produce a reviewable profile diff, tested against the agent's golden set before it goes live.
3. **Attributed to the owner, enforced in tools.** Chat runs execute under the attorney's own delegated identity, and every run is ledgered to the owner and profile version; background agents get Entra Agent IDs. Everything stays attributable, reviewable and revocable in the customer's own tenant.

This is the Action Engine's Template / Action split applied to professional roles, and it fits ADR-013 (extend the BFF), D-F0 (deterministic facts first) and the read/write gate asymmetry already in the platform.

## Core idea: a specialist is a governed configuration

An attorney does not get a new deployed agent; they get an **Agent Instance**: an Archetype (for example "Commercial Real Estate Partner") plus their own overrides, bound to a shared runtime. At invocation the runtime compiles the layered profile into one effective, versioned instruction-and-permission set, and every run records which profile version produced it.

Why configuration rather than code:

- **Attorney ownership is real.** The thing they tune is a document they can read, diff and roll back, not a prompt buried in an app.
- **Reliability.** Per the template-matching doctrine, most work collapses into parameterized templates; a role changes the parameters, positions and gates, not the machinery.
- **Cost and operations.** One runtime to secure, evaluate and meter (ADR-013, ADR-016) regardless of how many specialists a firm configures.
- **Distribution.** Archetypes become distributable Templates; a firm's tuned instance is a private fork that never flows back without consent.

## Primitives

Eleven building blocks. Most already exist in Spaarke in some form; the new work is the profile, the layering rules and the tuning loop.

| Primitive | What it is | Where it lives | Status |
| --- | --- | --- | --- |
| Archetype | Curated role template: charter, default positions, skills, tools, output contracts | Dataverse, solution-aware | \[PROPOSED\] extends Action Engine Template |
| Agent Instance | Owner + archetype + overrides + autonomy level | Dataverse | \[PROPOSED\] |
| Role Profile | Versioned, layered config compiled at runtime (charter, positions, voice, scopes, gates) | Dataverse JSON Prompt Schema | Extends existing Scope System |
| Skill | Reusable instruction fragment with worked examples (for example "claim differentiation review") | Scope System | Exists (Skills) |
| Tool | Deterministic or probabilistic operation behind a typed contract | BFF `IAiToolHandler`, exposed as MCP | Exists |
| Knowledge Scope | Named, security-trimmed corpus (firm precedent, matter-type forms, prosecution history) | Azure AI Search, SharePoint Embedded | Exists (index v2) |
| Position Library | Attorney's preferred clause or claim positions, with fallbacks and rationale | Dataverse, feeds Playbook nodes | Extends Playbooks |
| Authority and Gate Policy | What the agent may read, draft, send, file; which actions need attorney confirmation | Gate engine, session ledger | Exists, needs per-profile binding |
| Supervision Protocol | Partner-to-associate delegation, review and hand-back, with a named human reviewer | Agent Framework workflow | \[PROPOSED\] |
| Golden Set | Owner-approved example matters and expected outputs, the regression suite for the profile | Eval harness | Exists, needs per-agent scoping |
| Run Ledger | Immutable record: profile version, inputs, tools called, sources, gate decisions, reviewer action | Dataverse + Cosmos | Exists (session ledger) |

Two role shapes to model explicitly: a **partner** profile is weighted toward judgment framing, risk positions and what to escalate; an **associate** profile is weighted toward production (drafting, claim sets, diligence tables) under a named supervising profile or human.

## How an attorney owns and tunes their agent

The profile is five layers. Each layer may tighten what the layer above allows and may never loosen it. Authority, gates and data access ratchet one way; style and emphasis merge freely.

| Layer | Owner | Typical content | Locked against lower layers |
| --- | --- | --- | --- |
| L0 Platform | Spaarke | Privilege handling, D-F0 fact rules, epistemic-class grounding, write gates, audit | Everything |
| L1 Archetype | Spaarke or firm knowledge team | Role charter, default skills, output contracts, baseline positions | Tools and scopes it does not list |
| L2 Firm or department policy | GC, managing partner, practice head | Approved clause positions, conflicts and ethical-wall rules, model tier, autonomy ceiling | Autonomy ceiling, data scopes |
| L3 Owner profile | The owning attorney | Personal positions and fallbacks, voice, review habits, preferred precedents, escalation thresholds | Nothing above it |
| L4 Matter overlay | Matter lead | Client instructions, deal specifics, jurisdiction, deadlines | Nothing above it |

**Four ways to tune, one result.** Whichever the attorney uses, the output is a **proposed profile diff** shown in plain language, never an immediate change:

1. **Direct edit** in a profile editor (Fluent v9 Code Page): positions, fallbacks, voice, thresholds, tool toggles.
2. **Conversational** ("from now on, never accept uncapped indemnity without flagging it"): the Builder Agent elicits variables and emits the diff, consistent with template matching over open composition.
3. **Correction harvesting**: edits and rejections the attorney makes on agent output are clustered, and a repeated pattern becomes a suggested position change with the supporting examples attached.
4. **Exemplar-based**: the attorney marks a past document as "how I do this"; the agent extracts style and structure rules as a proposed diff, with the source excerpts cited.

**Activation is gated.** A diff runs against the agent's Golden Set; the attorney sees what changed in behavior (outputs that moved, checks that now fail), then approves. Profiles are versioned, every run records its version, and rollback is one action.

**What an attorney cannot tune:** the platform gates, the privilege and confidentiality rules, the fact layer (dates, arithmetic, deadlines are computed, never generated), and the human-review requirement for anything with external effect. Those are structural, which is the AI-directed, human-controlled boundary.

## Mapping to the Microsoft AI platform

Use Microsoft where it meets the need without compromise; build only where it cannot. Microsoft 365 Copilot is the attorney's chat surface, and Spaarke's runtime sits behind it. Spaarke model-driven apps carry embedded AI but are not the primary chat.

| Concern | Microsoft service | Recommendation |
| --- | --- | --- |
| Attorney chat surface | Microsoft 365 Copilot, Teams, Outlook, Word | Primary UX for all specialist agents. |
| Packaging and distribution | [Plugin](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugins-overview) (Microsoft 365 app package), plugin registry, Marketplace | Ship Spaarke capabilities as plugins; see the next section. |
| Default front door (Path B) | Declarative agent with skills and MCP actions | One per archetype. Copilot supplies the model and orchestrator. |
| In-chat interaction | [MCP Apps](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-mcp-apps) widgets | Tuning buttons and review cards. Widget-to-tool callbacks are \[VALIDATION NEEDED\]. |
| Pro-code fallback (Path A) | [Custom engine agent](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/overview-custom-engine-agent) on the Agents SDK or Foundry, published to Copilot Chat | Per archetype, only where Path B fails a requirement or an eval. |
| Backend orchestration | Microsoft Agent Framework (.NET) | In the BFF, behind tools: delegation, drafting pipelines, long-running jobs. Identical under both paths. |
| Knowledge | Foundry IQ knowledge bases, Azure AI Search, SharePoint Embedded | Knowledge Scopes as filtered, security-trimmed views. |
| Work context for tuning | [Work IQ APIs](https://www.microsoft.com/en-us/microsoft-365/work-iq) (MCP, A2A, REST) | Delegated user auth with admin consent; opt-in per attorney. |
| Tools | Remote MCP connector | BFF exposes deterministic tools; the profile whitelists them; gates are enforced here. |
| Identity and governance | Entra Agent ID, Agent 365 admin center | Chat runs use the attorney's delegated identity. Agent identities for background agents. Admins assign plugins to groups. |
| Models | Azure OpenAI and Foundry model catalog | Backend calls take a tier from the profile. Copilot's own model serves front-door chat. |
| Memory | Foundry memory (preview), Copilot Studio memory | Non-authoritative. The approved profile in Dataverse is the source of truth. |
| Tuning UI and storage | Power Apps Code Page, Dataverse | Profile editor and diff review; versions in Dataverse. |
| Observability and evaluation | Foundry tracing, Microsoft 365 Copilot Agent Evaluations (preview), Application Insights | Feed the existing golden-set harness. |

**Choosing a path.** Default to a declarative agent (Path B). Move one archetype to a custom engine agent (Path A) only when it fails a requirement or an eval, for example if Copilot's orchestrator cannot follow the compiled profile reliably. The backend (BFF, Agent Framework, Foundry IQ, gates, ledger) is identical under both paths; only the location of the conversation loop changes.

## Delivery in Copilot: plugins, agents and the pro-code fallback

As of 22 Nov 2026, a **plugin** is [an installable package](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugins-overview) that adds capabilities to Microsoft 365 Copilot. It bundles skills, tools (remote MCP connectors) and optionally agents, which today means declarative agents. It is a standard Microsoft 365 app package, [the same artifact](https://microsoft.github.io/wiqd/concepts/plugins/) whether it appears in Copilot chat, Cowork or Teams. Admins govern plugins in the Agent 365 admin center and can enable, block or assign them to users and groups ([plugin registry](https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/introducing-the-plugin-registry---one-place-to-discover-and-govern-plugins-for-m/4559682)). Distribution routes are direct sharing, organization upload and Marketplace, built with [Work IQ Dev Tools](https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development) (preview) and the Agents Toolkit.

The older terms "API plugin" and "MCP plugin" now mean actions of a declarative agent. "Plugin" alone means the package. The plugin is the packaging and distribution unit, not a third architecture beside Path A and Path B.

| Plugin component | Spaarke primitive | Notes |
| --- | --- | --- |
| Skills (`SKILL.md`, open Agent Skills standard) | Skill, archetype guidance | Static, shared across tenants, versioned and tested by Spaarke. Up to 20 per package. |
| Connector (remote MCP, OAuth) | BFF MCP server | Deterministic tools, profile loader, gates, ledger, widgets. Up to 10 per package. |
| Declarative agent | Archetype front door | One per archetype. Instructions are capped at 8,000 characters. |
| Per-attorney profile | Not in the package | Resolved server-side from the caller's Entra identity at first tool call. |

**Proposed packaging.** A core plugin carrying the connector and shared skills, plus one plugin per practice area (for example patent) carrying its archetype agents and skills. Admins then assign a practice plugin to the matching group. Whether one package can hold several agents, and whether plugins can depend on each other, are \[VALIDATION NEEDED\].

**Constraints to check before committing.**

- Each user needs a Microsoft 365 Copilot license, and MCP registration needs admin consent.
- Purview Information Barriers are not supported for plugin management. Firm ethical walls must therefore be enforced in Spaarke's scope tools, which the design already requires.
- Work IQ Dev Tools is in preview, and the manifest schema version differs across Microsoft's own pages (1.9, 1.28, 1.29).
- Custom plugins are not supported in Cowork on mobile.

## Deployment model: what ships as what, and where users meet it

Copilot's Plugins page is one catalog that lists agents, skills and connectors, split into a Chat tab and a Cowork tab. In our tenant, Chat lists agents (Spaarke AI, NDA Review, Legal Review, Legal Hold Review) and Cowork lists skills and a connector (nda-review, nda-library-review, legal-hold-review, Spaarke MCP). Spaarke ships each asset in the form its surface loads.

**Terms, used consistently in this doc**

- **Plugin (catalog):** anything a user can add on the Plugins page.
- **Plugin package:** the Microsoft 365 app package that can carry agents, skills and connectors together.
- **Declarative agent:** a conversational specialist, invoked in Chat or by @mention in Microsoft 365 apps. Copilot supplies the model and orchestrator.
- **Skill:** `SKILL.md` instructions. Used two ways: as a component inside a declarative agent, or as a standalone skill in Cowork.
- **Connector:** a remote MCP server that provides tools. It is the same server whichever surface calls it.

| Spaarke asset | Ships as | Loaded in | Notes |
| --- | --- | --- | --- |
| Specialist archetype (CRE partner, patent associate) | Declarative agent | Copilot Chat; also Teams, Outlook, Word, Excel, PowerPoint | Conversational, tuned per attorney, hosts tuning widgets. |
| Multi-step or long-running procedures (NDA library review, legal hold review, batch diligence, scheduled checks) | Standalone skill that calls the Spaarke connector | Cowork | Cloud execution with approval before actions; billed per task in credits. |
| BFF tool server | One remote MCP connector | Chat (agent actions) and Cowork (connector) | Two entries exist today (Spaarke Dataverse MCP, Spaarke MCP). Consolidate to one server and register it per surface. |
| Per-attorney profile | Server-side, not packaged | Both surfaces | Loaded by the first tool call. Evaluate on both surfaces; their orchestrators and model selection differ. |
| Tuning buttons and review cards | MCP Apps widgets | Chat. Cowork support is \[VALIDATION NEEDED\] | Fallback: a link to the Spaarke profile editor. |
| Coach nudges | Outlook and Teams cards; agent opening message | Outlook, Teams, Chat | Proactive, under a nudge budget. |
| Profile editor, diff review, dashboards | Spaarke Code Page or model-driven app | Spaarke apps | Embedded AI there, but not the primary chat. |
| Pro-code fallback agent | Custom engine agent | Copilot Chat | Only where the declarative agent fails a requirement or eval. |
| Background jobs (Coach, indexing) | Service Bus jobs | No user surface | Results reach users through the surfaces above. |

**Other places users meet AI.** Copilot Chat on web and desktop, with agents added from the Plugins page and invoked by @mention or from navigation. Agents also work inside Teams, Outlook, Word, Excel and PowerPoint, and can respond to notifications there. Cowork handles delegated work. The Copilot mobile app offers chat, but custom plugins are not supported in Cowork on mobile. External MCP clients can call the BFF; they are an unmanaged channel and meet the same gates.

**Decision rule.** Conversational and tuned: agent. Multi-step, long-running, approval-gated or scheduled: Cowork skill. Needs data or actions: the single connector. Write each procedure once in the skill library and package it both ways, as an agent component and as a standalone Cowork skill, so the two never drift.

**Open items \[VALIDATION NEEDED\].** Whether Cowork reliably follows a profile returned by a tool; whether Cowork renders widgets; whether Cowork can invoke a declarative agent; whether admins can pin or pre-enable plugins for a group; and Cowork credit cost per task for typical legal workflows.

## What Ignite 2026 changed (17–20 Nov)

Checked on 22 Nov against Microsoft's [Ignite Book of News](https://news.microsoft.com/events/ignite/2026/book-of-news/) (first 100,000 of about 127,000 characters read; the ISV marketplace and distribution section was cut off) and the product-team posts for [Cowork](https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/whats-new-in-copilot-cowork-at-ignite-2026/4572242), [Agent 365](https://techcommunity.microsoft.com/blog/agent-365-blog/whats-new-in-microsoft-agent-365-ignite-2026/4571753) and [Foundry Agent Service](https://devblogs.microsoft.com/foundry/whats-new-in-foundry-agent-service-at-ignite-2026/). Statuses are Microsoft's own words; secondary recaps are noted where they disagree. Net effect: Path B stays the default and nothing here forces a redesign, but four items change what we plan around.

**Confirms the design**

- Copilot Home puts Chat and Cowork in one app (generally available), and Chat can hand a larger task to Cowork with its context. This matches the agent-in-Chat, skill-in-Cowork split.
- Plugins and skills governance is generally available: admins decide which plugins and skills are available in Copilot and Cowork and assign them to specific users and groups. A plugin catalog also launched. This partly answers the open item on admin control; pinning or pre-enabling is not confirmed.
- MCP governance: before blocking an MCP server, admins can see which agents and plugins depend on it. The Tools Registry is generally available; Azure AI Gateway and LiteLLM support is preview. Our single consolidated connector is the right shape for this.
- Evaluation: multi-turn evaluation, Insights and Agent Optimizer (proposes and tests configuration changes) are generally available in Foundry; human annotation and golden datasets are preview. This maps to our Golden Set and tuning loop, but it applies to Foundry-hosted agents. \[VALIDATION NEEDED\] whether it covers declarative agents.
- Foundry IQ: SharePoint indexed knowledge source is generally available; knowledge graphs and a serverless plan are preview (serverless GA planned December).

**Changes what we plan around**

- **Cowork calling declarative agents is private preview (Frontier), not generally available.** Do not depend on it. The skill-plus-connector route in Cowork remains the plan; revisit when it reaches general availability.
- **Business plugins (Microsoft IQ, Frontier):** curated, hierarchically inherited, governed context, skills and sources that agents reuse across Copilot, Foundry and third-party tools via MCP. This overlaps our layered profile (L0 to L3). Keep our model: our gates must be enforced in Spaarke tools, and the feature is Frontier. \[PROPOSED\] Evaluate it as a possible carrier for the firm-policy layer (L2).
- **Copilot harness and Copilot SDK 2.0:** the harness is generally available (SDK 2.0, multi-tenant, six languages) and in public preview in Foundry, where Microsoft runs the agent loop and sandbox and we supply knowledge, skills and tools. This is a new middle option for Path A: a pro-code agent without our own orchestrator. \[VALIDATION NEEDED\] whether such an agent can be published into Copilot Chat.
- **Work IQ:** memory, a listen capability (agents respond to events), granular MCP policies and human-approval options. Status conflicts between sources (the Book of News lists memory as generally available; another recap says public preview). Listen could trigger Coach nudges from attorney activity; treat as \[VALIDATION NEEDED\] until the status is confirmed.

**Smaller items that touch our open questions**

- Cowork cost: Agent 365 Cost Management (preview, with forecasting in Frontier) lets admins set spending limits and alerts across Cowork, Work IQ APIs and Copilot Studio. The credit cost per task is still not published.
- Foundry hosted agents: egress controls and private CA trust are generally available; running on your own AKS clusters is preview (early December). Relevant if a firm demands data-boundary controls on Path A.
- Entra Agent ID is assigned to connected agents automatically; local agents and Autopilot are preview.
- Not addressed in Microsoft's Ignite posts and still open: custom or third-party plugins in Cowork, MCP Apps widgets in Cowork, Cowork on mobile, and scheduling. A Book of News line about "declarative agents in the new app" is unexplained beyond private preview.

## Autonomous runs: a specialist started by an event

The same specialist can be started by an event instead of a person. The example: an incoming email flagged as an NDA triggers the NDA specialist, which reviews the document, assesses it, marks it up and prepares or sends a response. A declarative agent is an interactive Copilot session, and we found no Microsoft documentation of events invoking one (our reading; \[VALIDATION NEEDED\]). So autonomous runs go through our own backend, which is the main reason to keep Path A capability.

**Microsoft's own options, none adopted yet**

- Copilot Studio event triggers (an email arriving, a row updating) exist, but Copilot Studio is not our agent environment.
- Agent accounts give an agent its own Entra identity, mailbox and Teams presence. Frontier preview, with public preview announced for December 2026.
- Triggers in the Copilot harness (an event or a schedule starts the agent): preview.
- Work IQ listen (agents respond to events): preview.
- Cowork proactive recommendations (Frontier) suggest tasks to a person; they are not an event trigger for our agents.

**Proposed flow for the NDA example**

1. A Graph change notification on the attorney's mailbox or a shared intake mailbox flags a candidate NDA (rules plus a classifier).
2. A Service Bus job is queued (ADR-004) with an idempotency key and the run's authority envelope.
3. The backend compiles the same profile layers (L0 to L4) and runs the NDA specialist headlessly with the same tools used in Chat and Cowork.
4. Deterministic checks run first (D-F0): parties, term, governing law, carve-outs. Then GREEN, YELLOW or RED triage against the owner's playbook.
5. The document is marked up through the OOXML redline path and a reply is drafted.
6. The send is a write gate. The result goes to the attorney as an Outlook draft and a Teams approval card.

**Autonomy ladder.** Each level is a setting that firm policy can cap and the attorney can only tighten, never widen.

- Triage only: classification and summary, nothing drafted.
- Prepare (default): markup and a draft reply saved for one-click review and send.
- Auto-send: only GREEN NDAs on standard paper, only where firm policy and the attorney's profile both allow it. Every send is logged and sampled for review.
- Never automatic: RED, non-standard counterparties, anything flagged privileged, and the first runs of any new or newly retuned specialist.

**Identity and controls.** \[PROPOSED\] Start by running as the delegated attorney so permissions and audit trace to a person; move to an Entra Agent ID identity for the agent once the preview features are validated. The run ledger, feature flags and kill switches (ADR-018), and cost and rate limits (ADR-016) apply as for any job.

**Open.** Supervision, privilege and disclosure obligations for autonomous legal communications need counsel's view before any auto-send is enabled. Even the AI-native firms are still working on this: [Crosby is pursuing professional liability insurance for autonomous legal agents](https://ainativelawfirm.com/news).

## How a request moves through a specialist agent

&#91;embedded content: specialist agent run · 9 steps, 1 feedback loop\]

The profile is compiled once per run and pinned, so the ledger can always show which instructions produced an output. Steps 4 and 5 keep facts deterministic and judgment probabilistic; step 6 is where the platform, not the profile, decides what needs a human.

## Governance, privilege and audit

- **Accountable human.** Every Agent Instance has one owning attorney and, for associate-type roles, a named supervising reviewer. Departure or role change triggers owner reassignment through the Agent 365 lifecycle and access-review flow, not a silent orphan.
- **Read permissive, write gated.** Reads run under the user's delegated rights. Anything with external effect (sending, filing, sharing, updating a record of truth) requires a deterministic confirmation gate, and the profile can only add gates.
- **Security trimming and ethical walls.** Knowledge Scopes inherit the caller's rights; a firm-level L2 policy can bar scope combinations (for example two clients with a conflict). The agent never holds broader access than its owner.
- **Privilege.** Agent work product is attorney work product. Run ledgers are tenant-resident, retention follows the customer's Purview policies, and profiles are never used to train shared models.
- **Honesty contract.** Each output labels its epistemic class: record fact, document-grounded analysis, or knowledge answer, with citations to source passages. Associate drafts carry a visible "unreviewed" state until a human acts.
- **Audit.** The run ledger answers who (owner, agent identity, reviewer), what (profile version, tools, sources) and why (gate decisions), and is exportable for a client or regulator inquiry.
- **Cross-client learning is off by default.** One attorney's tuned positions never reach another's agent. Spaarke's two-sided data position can inform archetype updates only as aggregated, opt-in, de-identified patterns.

## Evaluation and the tuning loop

Evaluation is what makes attorney tuning safe. Without it, a well-meant instruction change silently breaks behavior the attorney was relying on.

1. **Seed.** An archetype ships with a curated Golden Set (for example ten CRE lease-review cases, or claim sets with known defects).
2. **Extend.** Each time the owner accepts, rejects or corrects an output, they can promote it into their own Golden Set with one action. The set grows from real work, which is the quality ratchet.
3. **Gate.** Every profile diff runs against the set before activation. The attorney sees a behavior diff (outputs that changed, checks that now fail), not a score alone.
4. **Monitor.** Post-activation, override rates, escalation rates and cost per run are tracked per agent and shown to the owner, with the archetype publisher seeing only aggregates.
5. **Retire.** Stale positions are flagged when the underlying law, form or client instruction changes, with an owner prompt to confirm or revise.

Deterministic checks (defined-term consistency, cross-references, antecedent basis in claims, deadline arithmetic) run as tools and as eval assertions, so the model is graded on judgment and drafting, not on facts the platform should compute.

## Self-tuning

A Coach job turns signals into suggestions, and the attorney accepts, edits or dismisses each one. Nothing changes silently. It runs as a Service Bus job under ADR-004.

**Signals, strongest first:**

1. Corrections to agent output inside Spaarke: edits and rejections in the run ledger, and SharePoint Embedded document versions.
2. Explicit buttons in chat (below).
3. The attorney's wider work through Work IQ, such as sent emails and prior drafts, used for cold start and drift detection. Work IQ requires delegated user authentication and admin consent, so this is opt-in per attorney. Whether a scheduled job can run without the attorney present is \[VALIDATION NEEDED\].

**Suggestion types:** a correction pattern ("you changed this clause in 4 of 5 reviews; make it a position?"), a gap prompt ("no fallback set for assignment"), a drift flag (recent emails accept terms the profile does not), an exemplar offer ("use this memo as your style example?") and a weekly check-in of three questions.

**Buttons on every output**, as an MCP Apps widget in Copilot chat: Add to my knowledge, Make this my position, Use as my example, Never do this again. Each creates a proposal with provenance. Adding a document to a knowledge scope can apply at once with undo; anything that changes behavior goes through the golden-set gate.

**Delivery.** Suggestions appear when the attorney opens the agent in Copilot ("3 suggestions waiting") and as actionable cards in Outlook and Teams, under a nudge budget. Dismissals count as negative signal.

**Safeguards.** Store derived rules, not client content. Scope harvesting per matter (D-A4). The approved profile in Dataverse stays authoritative; Copilot Studio memory is not used.

## Fit with the existing Spaarke architecture

- **Action Engine.** Archetype = Template, Agent Instance = Action. The naming question ("Action" versus "Agent" user-facing) should resolve to "Agent" for this feature, since attorneys think in roles, and keep "Action" as the internal runtime term.
- **Playbooks and Scope System.** The Position Library and Skills reuse Skills, Actions and Playbook nodes. A specialist runs playbooks; it does not replace them. `PlaybookExecutionEngine` remains the execution path.
- **Insights Engine.** The Fact, Observation, Inference taxonomy is the honesty contract above; the agent consumes it rather than re-deriving facts.
- **Module taxonomy.** Roles select among the six output-contract archetypes (deviation analysis, structured extraction, adversarial issue mapping and so on). A CRE partner leans on deviation analysis and change-impact; a patent associate on structured extraction and drafting.
- **ADR-013, 015, 016.** Extend the BFF, keep AI data governance and per-tenant token budgets. Per-agent budgets become a sub-allocation of the tenant budget.
- **Compose workspace and Word.** Drafting output lands in Compose, with the document revision report recording the profile version and reviewer.
- **Patent drafting request.** The law-firm patent drafting ask is the natural first customer of the biotech claim-drafting archetype, and docketing remains deterministic per the IP docketing design.
- **Claude Code.** Remains internal tooling only; none of this adds a runtime dependency on it.

## Market position: what others claim and where we differ

Checked on 22 Nov from vendor and trade sources. Feature claims are marketing until tested.

- **AI-native law firms** (Crosby, Eudia Counsel, Garfield and others): firms that use agents for the first pass, with licensed attorneys reviewing and the firm carrying liability. Crosby sells commercial contract review per document; Eudia Counsel operates under Arizona's alternative business structure program; Garfield is authorised in England and Wales for small-claims debt recovery. They sell outcomes. We sell software that a firm or legal department runs, so they are a signal of buyer expectations, not direct competitors.
- **Harvey:** Agent Builder (March 2026), 500+ pre-built agents (May 2026), multi-step and scheduled agents. It ships in Microsoft 365 Copilot as an agent and in Cowork as a plugin (June 2026), is enabled in both Harvey and the Microsoft 365 admin center, and its Outlook agents draft replies for the attorney to review. This is the same delivery pattern we chose, so a firm may run Harvey and our specialists side by side in one Copilot.
- **Legora:** an agentic operating system (aOS) reached through Word and Outlook add-ins, Intake for Teams and Outlook, and a connection to Copilot. The agent uses a team's skills and playbooks with a lawyer accountable, and Monitors scan regulation continuously.
- **Thomson Reuters CoCounsel:** moved from skills orchestration to agents that plan and execute steps, grounded in Westlaw and Practical Law content.
- **Claude for Legal (Anthropic, May 2026, Apache 2.0):** 12 practice-area plugins, 90+ workflow agents and 20+ connectors. Each plugin runs a tailoring interview; agents are modifiable in plain language; scheduled watcher templates (renewals, dockets) run without a lawyer at the keyboard. It runs on Anthropic's surfaces, not Microsoft Copilot.

**Where we differ.** Specialists owned and tuned per attorney, with a one-way ratchet on authority; tuning by tested diff with versioning and rollback; gates and ethical walls enforced in tools; delivery inside the customer's Copilot and tenant. None of the sources read describes an equivalent of the ratchet or the Golden Set test. That is absence of evidence, not a proven gap.

**Where we are behind.** No licensed legal content library comparable to Harvey's or Thomson Reuters' (so the content strategy and partners need a decision), no liability or insurance offering, and no chained autonomy yet beyond the NDA example above.

**\[PROPOSED\] Borrow and benchmark.** Use Claude for Legal's archetype definitions and tailoring interviews as seed material (confirm the license terms and quality first), and benchmark our tuning experience against Harvey's Agent Builder and Legora's playbooks. Decide how our specialists are named and routed when several legal agents share one Copilot, and whether any of them are partners or tools instead of competitors.

## Phasing and open decisions

Recommended sequence, each phase gated on its eval results:

1. **Phase 0: inventory and spikes.** Codebase inventory of `IAiToolHandler`, the Scope System schema and `PlaybookExecutionEngine`; a Copilot spike covering a declarative agent with an MCP connector, a widget with action buttons, and an eval of whether the agent follows a profile returned by a tool.
2. **Phase 1: one vertical slice.** Role Profile schema, the layer compiler, and one archetype end to end. Recommended: the biotech patent claim-drafting associate with a supervising partner profile, because it has a live customer ask and exercises the supervision protocol.
3. **Phase 2: the tuning loop.** Profile diff review, Golden Set gate, versioning and rollback, then correction harvesting.
4. **Phase 3: identity and governance.** Agent 365 plugin governance, Entra Agent ID for background agents, per-archetype budgets and dashboards.
5. **Phase 4: archetype library and firm policy layer.** Second archetype (CRE partner), L2 firm policy editor, template distribution.

| ID | Open decision | Leaning |
| --- | --- | --- |
| D-A1 | Declarative agent or custom engine agent as each archetype's front door | Declarative agent by default; custom engine agent only on a failed requirement or eval |
| D-A2 | Which identity runs chat and which runs background work | Delegated attorney identity for chat; Entra Agent ID for background agents such as the Coach |
| D-A3 | Profile storage: one versioned JSON document or normalized Dataverse tables | JSON document with indexed header fields |
| D-A4 | Consent and privilege rules for correction harvesting from client documents | Owner-scoped, matter-restricted, opt-in per matter |
| D-A5 | Per-agent pricing and Agent 365 licensing implications | \[VALIDATION NEEDED\] with Microsoft partner contacts |
| D-A6 | Whether firm-authored Copilot Studio agents may be connected as peers via A2A | Defer to Phase 4 |

Platform status notes: Entra Agent ID is described by Microsoft as generally available; Foundry agent memory, Agent 365 integrations and some connected-agent types were in preview in the sources reviewed, so confirm current status before committing dates.

**Sources** (search results, not read in full): [Foundry hosted agents](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/hosted-agents) · [Agent 365 integration with Foundry](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/agent-365-integration) · [Entra Agent ID](https://techcommunity.microsoft.com/blog/microsoft-entra-blog/build-ai-agents-for-production-with-secure-identities-from-day-one/4524606) · [Foundry Agent Service at Ignite 2025](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/foundry-agent-service-at-ignite-2025-simple-to-build-powerful-to-deploy-trusted-/4469788) · [Agent Service and Agent Framework comparison](https://element61.be/en/resource/microsoft-foundry-agent-service-and-agent-framework-comparison) · Spaarke project docs (ADR-013, feature specification, architecture doctrine).

**Added 22 Nov 2026** (plugins, Copilot surface, Work IQ): [What is a plugin](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugins-overview) · [Plugin registry](https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/introducing-the-plugin-registry---one-place-to-discover-and-govern-plugins-for-m/4559682) · [Copilot plugin development](https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development) · [Work IQ Dev Tools](https://microsoft.github.io/wiqd/concepts/plugins/) · [MCP Apps in Copilot](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-mcp-apps) · [Custom engine agents](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/overview-custom-engine-agent) · [Work IQ APIs](https://www.microsoft.com/en-us/microsoft-365/work-iq).

**Added 22 Nov 2026 (Ignite, autonomy and market):** [Ignite Book of News](https://news.microsoft.com/events/ignite/2026/book-of-news/) · [Cowork at Ignite](https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/whats-new-in-copilot-cowork-at-ignite-2026/4572242) · [Agent 365 at Ignite](https://techcommunity.microsoft.com/blog/agent-365-blog/whats-new-in-microsoft-agent-365-ignite-2026/4571753) · [Foundry Agent Service at Ignite](https://devblogs.microsoft.com/foundry/whats-new-in-foundry-agent-service-at-ignite-2026/) · [Agent accounts](https://aidevme.com/?p=21164) · [Copilot Studio triggers](https://learn.microsoft.com/en-gb/power-platform/release-plan/2024wave2/microsoft-copilot-studio/create-automated-copilots-triggered-events) · [Harvey in Copilot and Cowork](https://www.harvey.ai/blog/harvey-copilot-cowork-launch) · [Harvey for Microsoft 365 Copilot](https://help.harvey.ai/articles/harvey-for-microsoft-365-copilot) · [Harvey agents for Outlook](https://www.harvey.ai/blog/harvey-agents-for-outlook-use-cases) · [Legora and Microsoft 365](https://legora.com/blog/bringing-the-power-of-legora-across-microsoft-365) · [CoCounsel rebuilt](https://www.thomsonreuters.com/en-us/posts/innovation/rebuilding-for-the-agent-era-the-next-generation-of-cocounsel-legal/) · [Claude for Legal](https://gc.ai/blog/claude-legal-ai) · [AI-native law firms](https://research.contrary.com/company/crosby).
