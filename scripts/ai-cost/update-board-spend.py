"""Refresh the AI Spend (est.) / AI Calls / AI Spend As Of fields on the Spaarke
Core portfolio board (Project #2) for one or more projects, from local Claude
Code transcripts (see get-project-cost.py for the pricing/estimation method).

This is intentionally a MANUALLY-TRIGGERED script, invoked via the
`/project-spend-update` skill - not wired into task-execute or any other
automatic hook. See .claude/skills/project-spend-update/SKILL.md for the
rationale (a task-completion-tied refresh under-covers Workflow-tool-driven
sessions that don't route through task-execute Step 9.6).

Usage:
    python scripts/ai-cost/update-board-spend.py                 # all Type=Project board items
    python scripts/ai-cost/update-board-spend.py <slug> [<slug> ...]  # just these

Requires: gh CLI authenticated with project read/write scope.
"""
import json, re, subprocess, sys, os, datetime, importlib.util

# get-project-cost.py has a hyphen in its filename (repo convention), which is not
# a valid Python module name for a plain `import` - load it by path instead.
_here = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location(
    "get_project_cost", os.path.join(_here, "get-project-cost.py"))
_gpc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_gpc)
find_folder, cost_for_folder = _gpc.find_folder, _gpc.cost_for_folder

OWNER = "spaarke-dev"
PROJECT_NUMBER = "2"
PROJECT_ID = "PVT_kwHODW0Pv84BEgWu"
F_SPEND = "PVTF_lAHODW0Pv84BEgWuzhkWwag"   # "AI Spend (est.)" - NUMBER
F_ASOF = "PVTF_lAHODW0Pv84BEgWuzhkWwcU"    # "AI Spend As Of" - DATE
F_CALLS = "PVTF_lAHODW0Pv84BEgWuzhkWwdQ"   # "AI Calls" - NUMBER
# Re-derive these IDs if the board is ever rebuilt:
#   gh project field-list 2 --owner spaarke-dev --format json

def board_projects():
    """Return [(item_id, slug), ...] for every Type=Project item on the board."""
    r = subprocess.run(
        ["gh", "project", "item-list", PROJECT_NUMBER, "--owner", OWNER,
         "--format", "json", "-L", "500"],
        capture_output=True, text=True, encoding="utf-8", errors="replace", check=True,
    )
    items = json.loads(r.stdout)["items"]
    out = []
    for i in items:
        if i.get("type") != "Project":
            continue
        m = re.search(r"\[Project\]:\s*(.+)", i["content"]["title"])
        if m:
            out.append((i["id"], m.group(1).strip()))
    return out

def set_field(item_id, field_id, flag, value):
    r = subprocess.run(
        ["gh", "project", "item-edit", "--id", item_id, "--project-id", PROJECT_ID,
         "--field-id", field_id, flag, str(value)],
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    return r.returncode == 0, r.stderr.strip()

def update_one(slug, item_id, today):
    folder = find_folder(slug)
    if not folder:
        return "no-local-history", None
    r = cost_for_folder(folder)
    if r["calls"] == 0:
        return "no-usage-found", None
    ok1, e1 = set_field(item_id, F_SPEND, "--number", r["cost"])
    ok2, e2 = set_field(item_id, F_ASOF, "--date", today)
    ok3, e3 = set_field(item_id, F_CALLS, "--number", r["calls"])
    if ok1 and ok2 and ok3:
        return "updated", r
    return "edit-failed: " + " | ".join(e for e in (e1, e2, e3) if e), r

def main():
    args = sys.argv[1:]
    today = datetime.date.today().isoformat()
    targets = args if args else None

    if targets is None:
        pairs = board_projects()
    else:
        all_pairs = {slug: item_id for item_id, slug in board_projects()}
        pairs = []
        for slug in targets:
            item_id = all_pairs.get(slug)
            if not item_id:
                print(f"SKIP {slug}: not found as a Type=Project board item "
                      f"(run /devops-project-register first)")
                continue
            pairs.append((item_id, slug))

    updated = skipped = failed = 0
    for item_id, slug in pairs:
        status, r = update_one(slug, item_id, today)
        if status == "updated":
            updated += 1
            print(f"OK       {slug:55s} ${r['cost']:>10,.2f}  {r['calls']:>7,d} calls")
        elif status in ("no-local-history", "no-usage-found"):
            skipped += 1
            print(f"SKIP     {slug:55s} ({status})")
        else:
            failed += 1
            print(f"FAIL     {slug:55s} {status}")

    print(f"\n{updated} updated, {skipped} skipped (no local data), {failed} failed "
          f"- out of {len(pairs)} targeted")

if __name__ == "__main__":
    main()
