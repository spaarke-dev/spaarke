#!/usr/bin/env python3
"""Print a grouped Epic -> Projects rollup of Project #2 to the terminal.

Usage:
    python scripts/portfolio-status.py
    python scripts/portfolio-status.py --epic 421     # one Epic only
    python scripts/portfolio-status.py --status "In Progress"  # filter Projects by status
    python scripts/portfolio-status.py --verbose      # show worktree path / folder / task counts

Reads live state from Project #2 via GraphQL (~3 seconds).
This is the lightweight CLI equivalent of /devops-portfolio-status terminal mode.
"""
import argparse
import json
import subprocess
import sys
from collections import defaultdict


PROJECT_ID = "PVT_kwHODW0Pv84BEgWu"


def get_merge_state_for_branches(branches):
    """For each branch, return (latest_merged_pr_num, merge_date) or None.

    Single batched gh pr list call — efficient.
    """
    out = subprocess.run(
        ['gh', 'pr', 'list', '--state', 'all', '--limit', '300',
         '--json', 'number,title,state,headRefName,mergedAt,url'],
        capture_output=True, check=True
    ).stdout.decode('utf-8')
    all_prs = json.loads(out)
    branch_to_merged = {}
    branch_to_open = {}
    for pr in all_prs:
        b = pr['headRefName']
        if pr['state'] == 'MERGED':
            existing = branch_to_merged.get(b)
            if not existing or pr['mergedAt'] > existing['mergedAt']:
                branch_to_merged[b] = pr
        elif pr['state'] == 'OPEN':
            branch_to_open[b] = pr
    return branch_to_merged, branch_to_open


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--epic', type=int, help='Show one Epic only')
    ap.add_argument('--status', help='Filter Projects by Project Status field value')
    ap.add_argument('--verbose', action='store_true', help='Show field values per Project')
    ap.add_argument('--show-merges', action='store_true',
                    help='Show latest merged PR + merge date + archive-candidate flag per Project')
    args = ap.parse_args()

    print('Loading portfolio from Project #2...', file=sys.stderr)

    # Query items with all field values via GraphQL
    query = '''
    query {
      node(id: "''' + PROJECT_ID + '''") {
        ... on ProjectV2 {
          items(first: 100) {
            nodes {
              id
              content {
                ... on Issue { number title }
                ... on DraftIssue { title }
              }
              fieldValues(first: 30) {
                nodes {
                  __typename
                  ... on ProjectV2ItemFieldSingleSelectValue {
                    field { ... on ProjectV2SingleSelectField { name } }
                    name
                  }
                  ... on ProjectV2ItemFieldTextValue {
                    field { ... on ProjectV2Field { name } }
                    text
                  }
                  ... on ProjectV2ItemFieldNumberValue {
                    field { ... on ProjectV2Field { name } }
                    number
                  }
                }
              }
            }
          }
        }
      }
    }
    '''
    result = subprocess.run(['gh', 'api', 'graphql', '-f', f'query={query}'], capture_output=True, check=True)
    nodes = json.loads(result.stdout.decode('utf-8'))['data']['node']['items']['nodes']

    # Parse items
    epics = {}
    projects = []
    for n in nodes:
        c = n.get('content') or {}
        num = c.get('number')
        title = c.get('title', '')
        if not num:
            continue

        # Extract field values into a dict
        fv = {}
        for f in n.get('fieldValues', {}).get('nodes', []):
            field_name = (f.get('field') or {}).get('name')
            if not field_name:
                continue
            for key in ('name', 'text', 'number'):
                if key in f and f[key] is not None:
                    fv[field_name] = f[key]
                    break

        if title.startswith('[Epic]:'):
            epics[num] = {'title': title.replace('[Epic]:', '').strip(), 'fv': fv}
        elif title.startswith('[Project]:'):
            projects.append({
                'number': num,
                'title':  title.replace('[Project]:', '').strip(),
                'type':   fv.get('Type', '-'),
                'status': fv.get('Project Status', '-'),
                'ptype':  fv.get('Project Type', '-'),
                'worktree': fv.get('Worktree Path', ''),
                'folder':   fv.get('Project Folder', ''),
                'task_count': fv.get('Task Count'),
                'tasks_completed': fv.get('Tasks Completed'),
            })

    # Get Epic -> Project sub-issue links
    epic_to_projects = defaultdict(list)
    project_to_epic = {}
    for epic_num in epics:
        try:
            subs = json.loads(subprocess.run(
                ['gh', 'api', f'repos/spaarke-dev/spaarke/issues/{epic_num}/sub_issues'],
                capture_output=True, check=True
            ).stdout.decode('utf-8'))
            for sub in subs:
                if sub['title'].startswith('[Project]:'):
                    epic_to_projects[epic_num].append(sub['number'])
                    project_to_epic[sub['number']] = epic_num
        except Exception as e:
            # Surface errors so silent failures don't masquerade as "no children"
            sys.stderr.write(f'  warn: Epic #{epic_num} sub-issues query failed: {e}\n')

    # Filter
    if args.status:
        projects = [p for p in projects if p['status'].lower() == args.status.lower()]
    project_nums = {p['number']: p for p in projects}

    # --show-merges: for each project, look up merge state by reading its worktree's current branch
    branch_to_merged_pr = {}
    branch_to_open_pr = {}
    if args.show_merges:
        # Resolve each project's branch from worktree path
        for p in projects:
            wt_path = p.get('worktree', '')
            if wt_path:
                try:
                    branch = subprocess.run(['git', '-C', wt_path, 'branch', '--show-current'],
                                            capture_output=True, check=True
                                            ).stdout.decode('utf-8').strip()
                    p['branch'] = branch
                except Exception:
                    p['branch'] = None
            else:
                p['branch'] = None
        # Batch query for all PRs (single API call, more efficient than per-branch)
        branch_to_merged_pr, branch_to_open_pr = get_merge_state_for_branches([])
        # Attach merge info to each project
        for p in projects:
            b = p.get('branch')
            if b and b in branch_to_merged_pr:
                pr = branch_to_merged_pr[b]
                p['merged_pr'] = pr['number']
                p['merged_date'] = pr['mergedAt'][:10]
            else:
                p['merged_pr'] = None
                p['merged_date'] = None
            if b and b in branch_to_open_pr:
                p['open_pr'] = branch_to_open_pr[b]['number']
            else:
                p['open_pr'] = None
            # Archive candidate: merged + not yet archived (Project Status != Completed/Cancelled)
            p['archive_candidate'] = (
                p['merged_pr'] is not None
                and p['status'] not in ('Completed', 'Cancelled')
            )

    epic_nums_to_show = [args.epic] if args.epic else sorted(epics.keys())

    print()
    print('=' * 78)
    print(f'  Spaarke Portfolio  -  {len(epics)} Epics  /  {len(projects)} Projects')
    print('=' * 78)

    for epic_num in epic_nums_to_show:
        if epic_num not in epics:
            print(f'No Epic #{epic_num} found')
            continue
        epic = epics[epic_num]
        child_nums = epic_to_projects.get(epic_num, [])
        children = [project_nums[n] for n in child_nums if n in project_nums]

        # Per-status rollup
        counts = defaultdict(int)
        for c in children:
            counts[c['status']] += 1

        rollup_str = '  '.join(f'{k}={v}' for k, v in sorted(counts.items())) or '(no Projects)'
        print()
        print(f"  #{epic_num}  {epic['title']}")
        print(f"     [{len(children)} projects]  {rollup_str}")

        for c in sorted(children, key=lambda x: x['number']):
            tasks_str = ''
            if c['task_count'] is not None:
                done = c['tasks_completed'] or 0
                tasks_str = f"  ({done}/{int(c['task_count'])} tasks)"
            merge_str = ''
            if args.show_merges:
                if c.get('archive_candidate'):
                    merge_str = f"  [ARCHIVE CANDIDATE — merged #{c['merged_pr']} on {c['merged_date']}]"
                elif c.get('merged_pr'):
                    merge_str = f"  [merged #{c['merged_pr']} on {c['merged_date']}]"
                elif c.get('open_pr'):
                    merge_str = f"  [open PR #{c['open_pr']}]"
                else:
                    merge_str = f"  [no PR]"
            print(f"       - #{c['number']}  {c['title'][:55]:<55}  [{c['status']}] ({c['ptype']}){tasks_str}{merge_str}")
            if args.verbose:
                if c['worktree']:
                    print(f"             worktree: {c['worktree']}")
                if c['folder']:
                    print(f"             folder:   {c['folder']}")

    # Orphans
    orphans = [p for p in projects if p['number'] not in project_to_epic]
    if orphans:
        print()
        print('  UNPARENTED Projects (no Epic):')
        for c in orphans:
            print(f"       - #{c['number']}  {c['title'][:55]}  [{c['status']}]")

    print()


if __name__ == '__main__':
    main()
