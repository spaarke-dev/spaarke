#!/usr/bin/env python3
"""Backfill 13 active worktrees as Project Issues on GitHub Project #2.

Per FR-15 + F4 (Phase 3 active backfill). For each active worktree:
- Create a [Project]: {slug} Issue on the repo
- Add to Project #2
- Set: Type=Project, Project Type, Project Status=In Progress
- Set: Worktree Path, Project Folder
- Reference Parent Epic in body (Parent issue field set manually OR via separate sub-issue link)

Idempotent: skips if Issue with matching title already exists.

Reads enumeration from notes/backfill-enumeration-2026-06-23.json.
Writes results to notes/phase3-backfill-results-2026-06-23.md.
"""
import json
import subprocess
import sys
import time
from pathlib import Path

PROJECT_ID = "PVT_kwHODW0Pv84BEgWu"

# Field IDs (from notes/phase1-field-ids.md)
TYPE_FIELD_ID = "PVTSSF_lAHODW0Pv84BEgWuzg2HOQw"
PROJECT_TYPE_FIELD_ID = "PVTSSF_lAHODW0Pv84BEgWuzhWPlKQ"
WORKTREE_PATH_FIELD_ID = "PVTF_lAHODW0Pv84BEgWuzhWPlKU"
PROJECT_FOLDER_FIELD_ID = "PVTF_lAHODW0Pv84BEgWuzhWPlLQ"
TASK_COUNT_FIELD_ID = "PVTF_lAHODW0Pv84BEgWuzhWPlLU"
TASKS_COMPLETED_FIELD_ID = "PVTF_lAHODW0Pv84BEgWuzhWPlLY"
PROJECT_STATUS_FIELD_ID = "PVTSSF_lAHODW0Pv84BEgWuzhWPlLc"

# Option IDs
TYPE_PROJECT_OPTION_ID = "2708f496"

PROJECT_TYPE_OPTIONS = {
    "Module": "07bf70fd",
    "UI": "5074ee2d",
    "Infrastructure": "e7e019fa",
    "Cleanup": "7c088a12",
    "Data": "a4ae9f10",
    "Process": "58a487bb",
    "AI": "339f64ef",
    "Mixed": "49524af1",
}

PROJECT_STATUS_OPTIONS = {
    "Planned": "7cb5fba6",
    "In Progress": "70a2088a",
    "On Hold": "ce5d8e15",
    "Completed": "73bfd3a7",
    "Cancelled": "a07c6443",
}


def get_existing_project_issues():
    """Return dict slug → issue # for existing [Project]: Issues."""
    result = subprocess.run(['gh', 'issue', 'list', '--label', 'project', '--state', 'open', '--limit', '50', '--json', 'number,title'], capture_output=True, text=True, check=True)
    existing = {}
    for issue in json.loads(result.stdout):
        # Extract slug from "[Project]: {slug}"
        title = issue['title']
        if title.startswith('[Project]: '):
            slug = title.replace('[Project]: ', '').strip()
            existing[slug] = issue['number']
    return existing


def compose_body(p):
    """Compose Issue body from project metadata."""
    pr_line = f"- **Closing PR**: #{p['pr']} ({p['pr_url']})" if p['pr'] else "- **Open PR**: none yet"
    folder_line = p.get('folder_path', f"projects/{p['slug']}/")
    return f"""<!-- DO NOT EDIT — synced from README.md by /devops-project-sync -->

### Project Folder Slug
{p['slug']}

### Worktree Slug
{p['slug']}

### Proposed Project Type
{p['project_type']}

### Parent Epic Reference
#{p['epic']}

### Project Summary
Active Spaarke project at worktree `c:/code_files/spaarke-wt-{p['slug']}`. Branch `{p['branch']}`. Backfilled by Phase 3 task 021 of `spaarke-devops-project-tracking-r1` on 2026-06-23.

{pr_line}
- **Local folder**: `{folder_line}`
- **Worktree branch**: `{p['branch']}`
- **Days since last commit**: {p['days_since_commit']}

### Projected Start Date
(unknown — backfilled from existing worktree)

### Projected Target Date
(unknown — backfilled from existing worktree)

### Notes / Context
Registered by Phase 3 backfill. Refine description, set start/target dates, and link as sub-issue of Parent Epic via GitHub UI (or run `/devops-project-sync` after manual updates).
"""


def create_issue(p):
    """Create [Project]: Issue, return (issue_num, issue_url)."""
    body = compose_body(p)
    result = subprocess.run([
        'gh', 'issue', 'create',
        '--title', f"[Project]: {p['slug']}",
        '--body', body,
        '--label', 'project',
    ], capture_output=True, text=True, check=True)
    url = result.stdout.strip().splitlines()[-1]
    issue_num = url.rstrip('/').split('/')[-1]
    return int(issue_num), url


def add_to_project(issue_url):
    """Add Issue to Project #2, return item_id."""
    result = subprocess.run([
        'gh', 'project', 'item-add', '2',
        '--owner', 'spaarke-dev',
        '--url', issue_url,
        '--format', 'json'
    ], capture_output=True, text=True, check=True)
    return json.loads(result.stdout)['id']


def set_field_single_select(item_id, field_id, option_id):
    mutation = f'''mutation {{
      updateProjectV2ItemFieldValue(input: {{
        projectId: "{PROJECT_ID}"
        itemId: "{item_id}"
        fieldId: "{field_id}"
        value: {{ singleSelectOptionId: "{option_id}" }}
      }}) {{ projectV2Item {{ id }} }}
    }}'''
    subprocess.run(['gh', 'api', 'graphql', '-f', f'query={mutation}'], capture_output=True, text=True, check=True)


def set_field_text(item_id, field_id, text):
    mutation = f'''mutation($pid: ID!, $iid: ID!, $fid: ID!, $val: String!) {{
      updateProjectV2ItemFieldValue(input: {{
        projectId: $pid
        itemId: $iid
        fieldId: $fid
        value: {{ text: $val }}
      }}) {{ projectV2Item {{ id }} }}
    }}'''
    subprocess.run(['gh', 'api', 'graphql', '-f', f'query={mutation}',
                    '-f', f'pid={PROJECT_ID}', '-f', f'iid={item_id}', '-f', f'fid={field_id}', '-f', f'val={text}'],
                   capture_output=True, text=True, check=True)


def set_field_number(item_id, field_id, value):
    mutation = f'''mutation {{
      updateProjectV2ItemFieldValue(input: {{
        projectId: "{PROJECT_ID}"
        itemId: "{item_id}"
        fieldId: "{field_id}"
        value: {{ number: {value} }}
      }}) {{ projectV2Item {{ id }} }}
    }}'''
    subprocess.run(['gh', 'api', 'graphql', '-f', f'query={mutation}'], capture_output=True, text=True, check=True)


def main():
    with open('projects/spaarke-devops-project-tracking-r1/notes/backfill-enumeration-2026-06-23.json', 'r', encoding='utf-8') as f:
        enumeration = json.load(f)

    active = [p for p in enumeration if p['is_active']]
    print(f'Active projects to backfill: {len(active)}')

    existing = get_existing_project_issues()
    print(f'Existing Project Issues: {len(existing)}')

    results = []
    for i, p in enumerate(active, 1):
        slug = p['slug']
        print(f'  [{i}/{len(active)}] {slug} (Epic #{p["epic"]}, {p["project_type"]})...', end='', flush=True)

        if slug in existing:
            print(f' SKIP (already #{existing[slug]})')
            results.append({**p, 'issue_number': existing[slug], 'status': 'pre-existing'})
            continue

        try:
            # 1. Create Issue
            issue_num, issue_url = create_issue(p)

            # 2. Add to Project #2
            item_id = add_to_project(issue_url)

            # 3. Set fields
            set_field_single_select(item_id, TYPE_FIELD_ID, TYPE_PROJECT_OPTION_ID)
            set_field_single_select(item_id, PROJECT_TYPE_FIELD_ID, PROJECT_TYPE_OPTIONS[p['project_type']])
            set_field_single_select(item_id, PROJECT_STATUS_FIELD_ID, PROJECT_STATUS_OPTIONS['In Progress'])

            worktree_path = f"c:/code_files/spaarke-wt-{slug}"
            folder_path = p.get('folder_path') or f"projects/{slug}/"
            set_field_text(item_id, WORKTREE_PATH_FIELD_ID, worktree_path)
            set_field_text(item_id, PROJECT_FOLDER_FIELD_ID, folder_path)

            print(f' CREATED #{issue_num}')
            results.append({**p, 'issue_number': issue_num, 'issue_url': issue_url, 'item_id': item_id, 'status': 'created'})

            # Rate-limit hygiene per NFR-05
            if i % 5 == 0:
                time.sleep(2)

        except subprocess.CalledProcessError as e:
            print(f' FAILED: {e.stderr[:100]}')
            results.append({**p, 'status': 'failed', 'error': e.stderr[:200]})
        except Exception as e:
            print(f' FAILED: {e}')
            results.append({**p, 'status': 'failed', 'error': str(e)})

    # Save results
    out_path = Path('projects/spaarke-devops-project-tracking-r1/notes/phase3-backfill-results-2026-06-23.md')
    with open(out_path, 'w', encoding='utf-8') as f:
        f.write('# Phase 3 Backfill Results — 2026-06-23\n\n')
        f.write('| # | Slug | Issue # | Status | Epic | Type | Branch |\n')
        f.write('|---|------|---------|--------|------|------|--------|\n')
        for i, r in enumerate(results, 1):
            num = r.get('issue_number', '-')
            status = r.get('status', '-')
            f.write(f'| {i} | `{r["slug"]}` | #{num} | {status} | #{r["epic"]} | {r["project_type"]} | `{r["branch"]}` |\n')

    created = sum(1 for r in results if r.get('status') == 'created')
    pre_existing = sum(1 for r in results if r.get('status') == 'pre-existing')
    failed = sum(1 for r in results if r.get('status') == 'failed')
    print()
    print(f'Summary: {created} created, {pre_existing} pre-existing, {failed} failed')
    print(f'Results: {out_path}')

    return 0 if failed == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
