# Where Spaarke's AI should run across Microsoft 365: synthesis of the November 2026 surfaces spike

Written 2026-11-27, by the main session, from three research notes in this folder and our own tenant tests:

- `01-office-apps.md`: Copilot inside Word, Excel and PowerPoint (Outlook briefly).
- `02-chat-cowork-agents.md`: Copilot Chat, Cowork, Researcher and Analyst.
- `03-extension-routes.md`: every documented developer route, by surface; distribution, governance and direction.
- Our evidence: `../../poc-e2e/results.md` (WP-0 to WP-5, T-12b, W4 to W6).

**Check of the notes.** I re-read 11 of the most decision-relevant claims against the live pages on 2026-11-27, and all
11 match word for word:
- federated connectors work in "Copilot in Word, Excel, PowerPoint, and Outlook";
- skills in Chat are "only to users with Microsoft Copilot (Premium)";
- MCP certification: "PKCE isn't supported today";
- Excel Office.js skills: not for "production Copilot extension";
- "doesn't establish cross-agent reuse or cross-host support";
- the Excel FAQ: "Skills do not introduce a new code-execution mechanism";
- "Skills used in Word, Excel, and PowerPoint are stored in OneDrive today";
- Cowork "saves the changes to the same shared file";
- document selection is "only supported in Word";
- the add-in agent-action articles were removed by OfficeDev/office-js-docs-pr#5799, merged 2026-06-17.

The notes' other caveats still stand. Three Ignite texts were read as summaries. Message Center items come from a
public mirror. Most pages are dated 16 November 2026 or later and will move again.

---

## 1. The one-paragraph answer

Microsoft is splitting the work. **Microsoft owns the editing of the open document.** Its own Copilot agent makes
the changes in Word, Excel and PowerPoint. Third parties contribute two things:

- **instructions**, as skills;
- **remote tools**, as MCP servers.

The plugin is the box that carries both, and each app turns on only the parts it supports. For Spaarke this means:

- **Our logic and data stay on our server, behind the Spaarke MCP server.** That is the one route documented across
  Word, Excel, PowerPoint, Outlook, Copilot Chat, Cowork and Researcher.
- **Skills are thin, per-host instruction layers** on top of it.
- **Scripts (our Python engines) run only where a sandbox exists**, Cowork and Copilot Chat Premium. That's where we
  make new files: redlined copies, summary pages, dashboards.
- **In the open document, Copilot does the edits.** Our skill steers them. Our Office.js can apply them precisely
  where the app allows it, but that is an accelerator, not a foundation.

---

## 2. What each surface can do for us today

D = documented by Microsoft. O = observed in our tenant. P = documented preview. ? = unknown, with a test that
settles it.

| Surface | Our instructions (skills) | Our server tools (MCP) | Our scripts | Edit the open file | Make new files | Our UI (MCP Apps) |
|---|---|---|---|---|---|---|
| **Word for Windows** | Yes, plugin skills: O, no Microsoft page | Yes: D (federated), O (plugin connector) | Python no (O). Office.js in the skill text runs (O, WP-5) | Copilot's own editing, tracked changes and comments: D. Our Office.js: O | No | ? (UX-1) |
| Word for Mac | ? (WX-1) | D | ? (WX-2) | Copilot's own editing: D | No | ? |
| Word for the web | Lists our plugin, didn't run it (O, confounded; WX-3) | D | ? | Copilot edits: D; tracked changes not named | No | ? |
| **Excel** (all clients) | Personal skills GA (D). Plugin skills: Insider preview (P) | D | Office.js `.js` skill scripts: P (Insider). Copilot also runs its own Python: D | Copilot's own editing: D. Our Office.js: P | No | ? |
| **PowerPoint** (all clients) | Personal and admin-deployed skills GA (D). Plugins rolling out (Message Center) | D | ? (PP-1, PP-2) | Copilot's own editing: D | No | ? |
| **Outlook** | Not found | D | Not found | No edit mode | No | ? |
| **Copilot Chat, Premium** | Yes, GA Nov 2026 (D) | Yes (D) | Yes. Closed sandbox: no network, no pip; python-docx, openpyxl, python-pptx, LibreOffice included (D) | No | Yes (D); where outputs land ? (CC-6) | Yes, inside a declarative agent (D) |
| Copilot Chat, free | No skills (D) | No; licence needed (D) | Code interpreter only (D) | No | Code interpreter (D) | Inside a declarative agent (D) |
| **Cowork** | Yes (D, O) | Yes (D, O) | Yes: Python 3.12 (O). Runtime undocumented: network, pip, time limit ? (CC-3) | Its built-in editor changes OneDrive/SharePoint files in place (D). Tracked changes ? (CC-14). No Office.js runner (O) | Yes, to OneDrive `Cowork` (D, O) | Yes (D) |
| **Researcher** | No | Read-only, federated connectors only (D) | No | No | Report | No |
| Analyst | No | Not listed | Its own Python (D, stale) | No | Report | No |
| Declarative agent, any host | Up to 8 skills, Chat only (D) | As an action (D) | In Chat (D) | No. Reads the Word/PowerPoint document as plain text; only writes images (D, O) | In Chat (D) | Yes (D) |

**Licence floor.** Everything above except the free-Chat row needs Microsoft 365 Copilot (Premium). Cowork also
needs a spending policy and is billed in Copilot Credits. Skills in Copilot Chat cost no credits.

---

## 3. What this means for how we build

### 3.1 Five rules

1. **Logic and data live on our server, behind MCP.**
   - The MCP server is the only route documented across the Office apps.
   - It is also the only way to reach our SharePoint Embedded files: no host can read SPE bytes, and no MCP route
     hands a file to the host. So review of a stored Spaarke document must run on the server, from a document id.
   - This is what Harvey, Legora, iManage, NetDocuments and Litera already do: they are in the Connectors Gallery's
     legal category.
2. **A skill is a thin instruction layer, written per host.**
   - Microsoft says, repeatedly, that a shared format proves nothing about reach.
   - Word, Excel and PowerPoint each load skills differently. Chat and Cowork have different script runtimes.
   - So we write one skill per host family and test each host. The rules the skill applies come from one source.
3. **Scripts only where a sandbox exists, and only for making new files.**
   - Cowork and Chat Premium run our Python. The Office apps don't (Excel runs Office.js only, in preview).
   - Scripts have no network and no identity in Chat, and probably not in Cowork either. So they never call our
     server. The model calls the MCP tools; the script only shapes files.
4. **In the open document, steer Copilot; don't replace it.**
   - Copilot's own editing in Word makes tracked changes and comments (GA on Windows and Mac).
   - Our skill tells it what to change, from the playbook and the MCP tool's findings.
   - Where precision matters, our fixed Office.js can apply an edit list:
     - in Word desktop, observed in WP-5 but undocumented;
     - in Excel, documented but preview only.
   - Every Office.js path needs a fallback that works without it.
5. **One package, many components, tested per host.**
   - The plugin is Microsoft's packaging unit. Each surface activates only what it supports.
   - We ship one plugin per practice (NDA, agreements, legal hold), holding:
     - a Word skill (instructions plus optional Office.js);
     - a Cowork/Chat skill (instructions plus Python);
     - the Spaarke MCP connector;
     - optionally, a declarative agent (it may be needed for store eligibility; see 3.4).

### 3.2 What changes for the accelerator (this project)

| Today | Change | Why |
|---|---|---|
| Cowork-only full edition (Python) | Keep, and **add Copilot Chat Premium as a second host** for the same skills | Chat skills are GA, cost no Cowork credits and ship python-docx. Needs CC-1, CC-2 and CC-6: lxml is not in the published list, and where outputs land is unknown |
| Skills placed as OneDrive folders | Move to a **plugin** with per-host skills | OneDrive skills reach Chat only if shared. Admin-deployed plugins reach both |
| Word edition: not built | Design it as an instructions-only skill + MCP findings + Copilot's own tracked changes, with Office.js as an optional precise applier | WP-5. Microsoft's direction for Excel says the same |
| Redline skill fallback is unguarded | **Forbid editing the source file in place** in every review skill | If our script fails, Cowork's built-in editor could change the customer's original file, possibly without tracked changes (CC-14) |
| Plugin connector sign-in: Cowork's dynamic-registration shortcut | Use `OAuthPluginVault` for any plugin meant for both Chat and Cowork | The shortcut works only in Cowork, and Entra has no dynamic registration endpoint |
| MCP tools without annotations | Set `readOnlyHint` / `destructiveHint` on every tool | Unannotated tools are treated as destructive and prompt every time |

### 3.3 What changes for Spaarke more broadly

- **The Spaarke MCP server is the strategic asset**, more than any skill or agent.
  - Next decision: ship it as a federated connector (admin-made or in the gallery), as a plugin connector, or both.
    Only the federated form has a documented reach across Word, Excel, PowerPoint, Outlook and Researcher (XR-2).
  - Owners: the spaarkeai-mcp-bff-api projects.
- **Our UI (MCP Apps widgets) reaches Chat and Cowork only**, as far as Microsoft documents. Nothing is documented
  for the Office app panes (UX-1, XR-3). In Word, Excel and PowerPoint, plan for text and the document itself as the
  UI.
- **Excel and PowerPoint are open to us now, with differences.**
  - PowerPoint takes admin-deployed skills at GA: matter summary decks, board reporting.
  - Excel's developer route is preview, and Excel is Microsoft's lead app for skills: legal-spend analysis, matter
    lists, e-billing review.
  - Both reach our MCP tools.
- **Declarative agents are a reading surface, not an editing one.** In Word and PowerPoint they read the document as
  plain text and can only insert images. Their role is in Chat, and possibly as the store wrapper.
- **Researcher reads our federated connector, read only.** Research over Spaarke matters and documents in
  Researcher is a cheap win once the connector is federated.
- **Cross-project fact (word-add-in-r1):**
  - Copilot can no longer call Office add-in functions: the feature was cancelled on 2026-06-17, and store rules now
    forbid it.
  - Add-ins themselves are unaffected.
  - Facts only. Add-ins stay out of scope for this project.

### 3.4 Commercial and governance points for the owner

- **Law firms with Information Barriers can't use skills or plugins today**, in either Chat or Cowork. Many firms
  run ethical walls. This is a hard stop for those customers until Microsoft ships support. A Message Center post
  said November; it is not confirmed.
- **EU tenants:**
  - Claude models are outside the EU Data Boundary and off by default in the EU, EFTA and UK.
  - Only GPT models are supported in Cowork for EU Data Boundary tenants.
  - Our skills must work on GPT.
- **Store path:**
  1. Partner Center, "Microsoft 365 and Copilot" program, verified publisher.
  2. The customer's admin then allows the publisher, installs the plugin and scopes it.

  The documented store path for skills is inside a declarative agent. A skills-plus-connector plugin without one may
  not be eligible (XR-6 preflight settles it). Store agents may not use the Dataverse knowledge source. That doesn't
  touch a Dataverse-backed MCP server.
- **MCP certification conflicts with our zero-secret auth direction.** It is preview, capacity-limited, needs a
  client secret in our Key Vault and doesn't support PKCE. This needs an owner decision before any certification
  work (XR-8, read-only).
- **Audit.** Purview's `CopilotInteraction` record names the host app and the plugins used. Whether it records MCP
  tool arguments or skill script runs is not documented. Until AD-2 / XR-7 / CC-11 say otherwise, **our server must
  log every tool call itself**: what a law firm's compliance team will ask for.
- **Cost.** Cowork bills Copilot Credits per step, against one tenant pool shared with Copilot Studio. Chat skills
  and the federated connector are inside the Premium licence. That matters for pricing the base tiers.

---

## 4. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Word stops running skill-supplied Office.js, or tightens its filter | Medium. It's undocumented, and Excel's documented route looks different | Office.js only as an applier. Fallback: Copilot's own tracked changes from the same edit list, or the Cowork edition. Re-run WP-5 on Word updates (WX-10) |
| Each host behaves differently and changes monthly | High. Microsoft says so | Per-host skills, a probe per host, and a dated test log |
| Our skill falls back to in-place editing of the client's original | Medium (inferred) | Explicit "never edit the source in place" rule in every review skill, plus CC-14 |
| IB and EU customers can't use the package | Certain today for IB; model-limited for EU | Say so in sales material. Make the server-side (MCP) path, which needs no skills, the baseline |
| Store eligibility forces a declarative agent wrapper | Unknown | XR-6 preflight before packaging work |

---

## 5. Tests, in priority order

From the 50 proposed in the three notes. Each names the note that holds the full steps. Run review tests with
Spaarke MCP off unless the test is about it (spike tools on the dev connector).

| # | Test | Settles | Note |
|---|---|---|---|
| 1 | **WP-6** = WX-5 (data block filled by Copilot, code fixed) + WX-6 (does a thrown error reach Copilot?) + WX-4 (5/20/60 KB code) | Whether a Word edition can apply a real edit list | 01 |
| 2 | **CC-1 + CC-2 + CC-6**: our plugin in Copilot Chat Premium; the sandbox's real packages (lxml); where outputs land | Whether Chat is a second, credit-free host for the full edition | 02 |
| 3 | **CC-14**: can Cowork's built-in editor make tracked changes and comments, and does it edit in place unasked? | The in-place fallback risk; a second Cowork editing path | 02 |
| 4 | **XR-1**: one plugin across Word, Excel and PowerPoint on Windows, Mac and web (includes WX-1, WX-3, PP-4) | Real reach of "one package" | 03, 01 |
| 5 | **Part A egress + CC-3** (planned) and **CC-4** (MCP result becomes a file; needs the file-probe deploy) | How files reach Cowork scripts | `../../poc-e2e/spe-file-probe/README.md`, 02 |
| 6 | **XR-2**: federated vs plugin connector in Word and Outlook; with and without licence | How to register the Spaarke MCP server | 03 |
| 7 | **AD-2 / XR-7 / CC-11**: Purview records after a skill run and a tool call | What we tell compliance teams; what our server must log | 01, 03, 02 |
| 8 | **XR-6** store preflight (no submit) and **XR-8** certification auth fit (read only) | Store path; secret conflict | 03 |
| 9 | **XP-1, XP-2, PP-1, PP-2**: Excel and PowerPoint skill loading and script execution | Excel/PowerPoint editions | 01 |
| 10 | **UX-1 / XR-3**: does an MCP Apps widget render in an Office pane? | Whether any of our UI reaches the Office apps | 01, 03 |

---

## 6. Owner decisions this raises

1. Is **Copilot Chat Premium** a target host for the accelerator's full edition, alongside Cowork? (Test 2 first.)
2. Draft the **Word edition** design now (instructions + MCP + Copilot's tracked changes, Office.js optional), or
   after WP-6?
3. Register the Spaarke MCP server as a **federated connector**? This is a decision for the MCP projects (test 6
   first).
4. **Store and certification:** pursue, given the client-secret conflict and the declarative-agent wrapper question?
5. Whether to open **Excel and PowerPoint** accelerators (spend and matter analysis; summary decks) as a later
   phase.
6. Where this synthesis should live long-term: it is broader than this project (a `docs/architecture/` page on
   Spaarke's Microsoft 365 surface strategy).
