#!/usr/bin/env python3
"""Create 12 initial Epic Issues on Project #2 per FR-05.

Reads epic descriptions from notes/drafts/epic-descriptions.md and creates
one GitHub Issue per Epic, adds to Project #2, sets Type=Epic field.

Saves Issue numbers to notes/phase1-epic-issue-numbers.md for Phase 3 backfill.
"""
import json
import re
import subprocess
import sys
from pathlib import Path

PROJECT_ID = "PVT_kwHODW0Pv84BEgWu"
TYPE_FIELD_ID = "PVTSSF_lAHODW0Pv84BEgWuzg2HOQw"
EPIC_OPTION_ID = "47842682"  # post-task-001 Epic option ID

# Title : Body sections (Objectives, Scope, Success criteria, Projected timeframe)
EPICS = [
    ("AI Platform & Chat",
     ("Unify Spaarke's AI surfaces into a single platform — chat routing, capability discovery, playbook orchestration, observability. Eliminate parallel chat stacks across SpaarkeAi, SmartTodo, LegalWorkspace.",
      "In: chat routing redesign, capability router, shared agent framework, conversation history persistence, Foundry-grounded agents. Out: customer-facing chatbot.",
      "Single ChatEndpoints surface used by all surfaces; capability router handles soft-slash + free-text; conversation context persists across compaction; ≥3 production agents grounded against SPE.",
      "H2 2026 ongoing.")),
    ("Insights Engine",
     ("Generate operational insights from Dataverse + SPE data. Daily briefings, due-date awareness, risk flags, summarization at the entity level.",
      "In: daily briefing, due-date worker, summarization pipeline, insights widgets. Out: third-party analytics.",
      "Daily briefing reliably runs across customers; due-date alerts surface in app notifications; widget surfaces summarized insights without per-record click.",
      "H2 2026.")),
    ("Smart Todo",
     ("First-class `sprk_todo` entity with 11-entity regarding (ADR-024), Code Page, parent-form subgrids, Outlook ribbon integration, BFF Office endpoints, and feature-gated MS To Do sync scaffolding.",
      "In: SmartTodo Code Page, parent-form subgrids, Outlook + LinkedTodosBanner, BFF Office endpoints, MS To Do sync (gated). Out: Teams-native task surface in r1.",
      "SmartTodo Code Page handles 11-entity regarding; subgrids work on parent forms; Outlook ribbon creates todos; MS To Do sync gated and tested.",
      "r3 shipped; subsequent enhancements as needed.")),
    ("Document Intelligence",
     ("AI-assisted document classification, summarization, similarity, OCR-aware ingestion. Powers entity-aware document operations across legal/matter surfaces.",
      "In: document classification, summarization, FindSimilar, content extraction pipeline. Out: e-signature/redaction.",
      "Documents classified within minutes of upload; summarization runs on demand; FindSimilar returns useful results across SPE containers.",
      "H2 2026 → H1 2027.")),
    ("BFF & Test Hygiene",
     ("Keep `Sprk.Bff.Api` lean, well-tested, and within publish-size + dependency-vulnerability budgets per CLAUDE.md §10 BFF Hygiene. Strengthen unit test fixtures and integration coverage.",
      "In: publish-size monitoring, CVE remediation, fixture-contract enforcement, test-suite repair projects, ADR-032 Null-Object pattern adoption. Out: BFF rewrite or splitting.",
      "BFF publish-size ≤60 MB compressed; zero HIGH CVEs; test pass rate ≥99%; binding rules in CLAUDE.md §10.B/C/D enforced in PR review.",
      "Ongoing (continuous hygiene).")),
    ("Auth & SSO",
     ("Maintain Spaarke Auth v2 architecture per ADR-028 — OBO + app-only Graph auth, SSO binding, secure token caches. Streamline new-customer auth setup.",
      "In: OBO flow refinements, SSO binding, Key Vault secret rotation, app registration tooling. Out: B2C/B2B identity federation.",
      "All new customers provisioned with auth deployment scripts; zero credentials committed; Auth ADR-028 followed in every BFF-touching project.",
      "Maintenance + targeted improvements.")),
    ("Code Quality",
     ("Cross-cutting code quality — ADR enforcement, code-review judgment layer, doc-drift audits, conventions sweep, AI procedure quality.",
      "In: ADR auditing, code-review judgment layer, doc-drift auditing, AI procedure maintenance (ai-procedure-quality projects), conventions sweep. Out: third-party static analysis.",
      "ADRs ≤200 LOC each + indexed; doc-drift audits at project boundaries; ai-procedure-quality projects maintained at cadence; ANTI-PATTERNS.md current.",
      "Continuous.")),
    ("Procedures & Knowledge",
     ("Maintain Spaarke's AI-coding procedures, knowledge base, and onboarding documentation. Ensure the AI agent has accurate, current operational rules.",
      "In: HOW-TO-INITIATE-NEW-PROJECT.md, AI-CODING-PROCEDURES-GUIDE.md, knowledge/ subdirectory, researcher subagent memory, root CLAUDE.md. Out: customer-facing docs.",
      "Procedures docs accurate; knowledge/ accurately reflects current best practices; researcher subagent memory pruned regularly; root CLAUDE.md ≤250 LOC.",
      "Continuous.")),
    ("CI/CD & Tooling",
     ("Maintain GitHub Actions workflows + scripts + deployment tooling. Tiered CI model (blocking/advisory/info) with escape hatches. Reliable, debuggable, non-flaky.",
      "In: workflow rationalization, nightly health, build/deploy/test scripts, PowerShell utilities, repo cleanup tooling, this portfolio tracker. Out: external CI providers.",
      "CI runtime under target; flaky tests classified + retried; workflows in `.github/workflows/` ≤12 files; tooling discoverable + maintained.",
      "Continuous.")),
    ("Insights/Widgets/Search",
     ("Inline + side-by-side MCP widgets that show insights inside the Spaarke chat surface — search results, document lookups, due-date views, related-record explorers.",
      "In: MCP widget framework, AI search widget, document viewer widget, calendar widget, due-date widget, semantic search widget. Out: third-party widget marketplaces.",
      "≥5 production widgets shipping; widget framework reusable across surfaces; semantic search returns relevant results; performance budgets met.",
      "H2 2026 → H1 2027.")),
    ("Communications",
     ("Outlook + Teams integration surfaces for legal-ops workflows. Email-to-record, ribbon-driven create, side pane experiences, app-notification routing.",
      "In: Outlook ribbon, Office Add-ins, Teams app patterns, app-notification routing per entity. Out: end-user-customizable workflow builders.",
      "Outlook ribbon ships matter/document/todo creates; app notifications work across 11-entity regarding; Office Add-in deployment scripted and reproducible.",
      "H2 2026 → H1 2027.")),
    ("Multi-tenant",
     ("Provision, manage, and operate multi-customer Spaarke environments. Tenant onboarding, environment provisioning, deploy-promote pipeline, customer-specific configs.",
      "In: provision-customer workflow, deploy-promote pipeline, environment registry, customer-specific config files, tenant isolation. Out: cross-tenant data sharing.",
      "New customer provisioning automated end-to-end; environment registry single source of truth; deploy-promote workflow reliable; no tenant data leakage.",
      "H2 2026 → H1 2027.")),
]


def build_body(objectives, scope, success, timeframe):
    return f"""### Objectives / Focus

{objectives}

### Scope

{scope}

### Success Criteria

{success}

### Projected Timeframe

{timeframe}

### Notes / Context

Auto-created by Phase 1 task 005 of `spaarke-devops-project-tracking-r1`. Refine description and link child Projects via `Parent issue` field as portfolio evolves.
"""


def check_existing():
    """Return set of Epic titles already on Project #2."""
    result = subprocess.run(
        ['gh', 'issue', 'list', '--label', 'epic', '--state', 'open', '--limit', '50', '--json', 'number,title'],
        capture_output=True, text=True, check=True)
    existing = json.loads(result.stdout)
    titles = set()
    for issue in existing:
        # Strip "[Epic]: " prefix if present
        t = issue['title'].replace('[Epic]: ', '').strip()
        titles.add(t)
    return titles, existing


def create_epic(title, body):
    """Create Issue + add to Project #2 + set Type=Epic. Return (issue_num, issue_url, item_id)."""
    # 1. Create Issue
    result = subprocess.run(
        ['gh', 'issue', 'create',
         '--title', f'[Epic]: {title}',
         '--body', body,
         '--label', 'epic'],
        capture_output=True, text=True, check=True)
    url = result.stdout.strip().splitlines()[-1]
    issue_num = url.rstrip('/').split('/')[-1]

    # 2. Add to Project #2
    add_result = subprocess.run(
        ['gh', 'project', 'item-add', '2',
         '--owner', 'spaarke-dev',
         '--url', url,
         '--format', 'json'],
        capture_output=True, text=True, check=True)
    item = json.loads(add_result.stdout)
    item_id = item['id']

    # 3. Set Type=Epic
    mutation = f'''mutation {{
      updateProjectV2ItemFieldValue(input: {{
        projectId: "{PROJECT_ID}"
        itemId: "{item_id}"
        fieldId: "{TYPE_FIELD_ID}"
        value: {{ singleSelectOptionId: "{EPIC_OPTION_ID}" }}
      }}) {{ projectV2Item {{ id }} }}
    }}'''
    set_result = subprocess.run(
        ['gh', 'api', 'graphql', '-f', f'query={mutation}'],
        capture_output=True, text=True, check=True)
    parsed = json.loads(set_result.stdout)
    if 'errors' in parsed:
        raise RuntimeError(f'Type=Epic mutation failed: {parsed["errors"]}')

    return issue_num, url, item_id


def main():
    existing_titles, existing_issues = check_existing()
    print(f'Existing Epic Issues: {len(existing_issues)}')
    for issue in existing_issues:
        print(f'  EXISTING #{issue["number"]}: {issue["title"]}')

    results = []
    for title, sections in EPICS:
        if title in existing_titles:
            # Find existing number
            for issue in existing_issues:
                if title in issue['title']:
                    results.append({'title': title, 'number': issue['number'], 'created': False, 'url': f'https://github.com/spaarke-dev/spaarke/issues/{issue["number"]}'})
                    print(f'  SKIP existing: {title}')
                    break
            continue

        body = build_body(*sections)
        try:
            num, url, item_id = create_epic(title, body)
            results.append({'title': title, 'number': num, 'url': url, 'item_id': item_id, 'created': True})
            print(f'  CREATED #{num}: {title}')
        except subprocess.CalledProcessError as e:
            print(f'  FAILED: {title} -- {e.stderr}', file=sys.stderr)
            results.append({'title': title, 'error': e.stderr, 'created': False})

    # Save results
    results_path = Path('projects/spaarke-devops-project-tracking-r1/notes/phase1-epic-issue-numbers.md')
    with open(results_path, 'w', encoding='utf-8') as f:
        f.write('# Phase 1 Epic Issue Numbers (FR-05)\n\n')
        f.write(f'> Captured: 2026-06-23 by task 005 (`create-epics.py`)\n')
        f.write(f'> Consumed by Phase 3 backfill (task 021) — Parent Epic mappings.\n\n')
        f.write('| # | Title | Issue # | URL | Item ID | Status |\n')
        f.write('|---|-------|---------|-----|---------|--------|\n')
        for i, r in enumerate(results, 1):
            num = r.get('number', '—')
            url = r.get('url', '—')
            item = r.get('item_id', '—')
            status = 'CREATED' if r.get('created') else ('PRE-EXISTING' if r.get('number') else 'FAILED')
            f.write(f'| {i} | {r["title"]} | #{num} | {url} | `{item}` | {status} |\n')

    created = sum(1 for r in results if r.get('created'))
    pre_existing = sum(1 for r in results if not r.get('created') and r.get('number'))
    failed = sum(1 for r in results if 'error' in r)
    print()
    print(f'Summary: {created} created, {pre_existing} pre-existing, {failed} failed')
    print(f'Saved to: {results_path}')

    return 0 if failed == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
