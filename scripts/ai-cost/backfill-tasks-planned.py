"""Set the `Tasks Planned` number field on the Spaarke Core portfolio board (Project #2) from git history.

Tasks Planned = how many tasks a project started with, so `Task Count / Tasks Planned` shows scope growth.
For each board project the planned figure is read from the FIRST committed version of
`projects/<slug>/tasks/TASK-INDEX.md` (`git log --diff-filter=A`, oldest):

    planned = max(task rows in that version, *.poml files in tasks/ at that commit)

A task row is a table row whose first cell is a task number (`| 001 |`, `| [001](x.poml) |`, `| **001** |`),
counted once per distinct number. Task Count on the board counts `tasks/*.poml` (see the devops-project-sync
skill); the max() covers an index that was committed before its POMLs, or in a format with no numbered rows.
It is a heuristic: a project first merged in one squash commit reports its size at that merge.

Nothing is overwritten when the board already holds a value, unless --force. Dry run by default.

Usage:
    python scripts/ai-cost/backfill-tasks-planned.py                  # dry run, all Type=Project items
    python scripts/ai-cost/backfill-tasks-planned.py --apply          # write
    python scripts/ai-cost/backfill-tasks-planned.py --apply --force  # also replace existing values
    python scripts/ai-cost/backfill-tasks-planned.py <slug> [<slug> ...]

Requires: gh CLI authenticated with project read/write scope. The field is created on --apply if the board lacks it
(`gh project field-create 2 --owner spaarke-dev --name "Tasks Planned" --data-type NUMBER`).
"""
import argparse
import importlib.util
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
# update-board-spend.py owns the board constants and the gh item-edit wrapper.
_spec = importlib.util.spec_from_file_location("update_board_spend", os.path.join(HERE, "update-board-spend.py"))
ubs = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ubs)

FIELD_NAME = "Tasks Planned"
INDEX_GLOB = "projects/*/tasks/TASK-INDEX.md"
TASK_ROW = re.compile(r"^\|\s*(?:\[)?\**(\d{1,4}[a-z]?)\**(?:\])?(?:\([^)]*\))?\s*\|")
SHA = re.compile(r"[0-9a-f]{40}")


def count_task_rows(text):
    """Distinct task numbers that start a table row of a TASK-INDEX.md."""
    ids = set()
    for line in text.splitlines():
        m = TASK_ROW.match(line)
        if m:
            num = re.match(r"(\d+)(.*)", m.group(1).lower())
            ids.add((int(num.group(1)), num.group(2)))          # 001 and 1 are the same task
    return len(ids)


def git(repo, *args):
    r = subprocess.run(["git", "-C", repo] + list(args), capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    if r.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)}: {r.stderr.strip()}")
    return r.stdout


def first_versions(repo, ref=None):
    """{TASK-INDEX path: oldest commit that added it}. `ref` limits the history to one ref; the default is every
    ref, so a project whose branch has not merged yet is counted too."""
    out = git(repo, "log", ref or "--all", "--diff-filter=A", "--format=%H", "--name-only", "--", INDEX_GLOB)
    first, cur = {}, None
    for line in out.splitlines():
        line = line.strip()
        if not line:
            continue
        if SHA.fullmatch(line):
            cur = line
        else:
            first[line] = cur           # git log is newest first, so the last one seen is the oldest
    return first


def planned_for(repo, path, commit):
    """(planned, rows, poml) for one project's first TASK-INDEX version."""
    rows = count_task_rows(git(repo, "show", f"{commit}:{path}"))
    tree = git(repo, "ls-tree", "--name-only", commit, os.path.dirname(path).replace("\\", "/") + "/").split()
    poml = sum(1 for n in tree if n.endswith(".poml"))
    return max(rows, poml), rows, poml


def find_index_path(paths, slug):
    """The TASK-INDEX path for a board slug: the folder of that name (case-insensitive), else with the board's
    `x-` (retired project) and/or `spaarke-` prefix removed."""
    by_folder = {p.split("/")[1].lower(): p for p in paths}
    s = slug.lower()
    nox = s.removeprefix("x-")
    for cand in (s, nox, s.removeprefix("spaarke-"), nox.removeprefix("spaarke-")):
        if cand in by_folder:
            return by_folder[cand]
    return None


def should_write(current, force):
    """Never overwrite a non-empty value unless --force."""
    return current is None or force


def growth(current, planned):
    return current / planned if planned and current is not None else None


def field_id(create):
    r = subprocess.run(["gh", "project", "field-list", ubs.PROJECT_NUMBER, "--owner", ubs.OWNER,
                        "--format", "json", "--limit", "100"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace", check=True)
    for f in json.loads(r.stdout)["fields"]:
        if f["name"] == FIELD_NAME:
            return f["id"]
    if not create:
        return None
    r = subprocess.run(["gh", "project", "field-create", ubs.PROJECT_NUMBER, "--owner", ubs.OWNER,
                        "--name", FIELD_NAME, "--data-type", "NUMBER", "--format", "json"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace", check=True)
    return json.loads(r.stdout)["id"]


def board_items():
    """[(item_id, slug, task_count, tasks_planned)] for every Type=Project item (None = unset)."""
    r = subprocess.run(["gh", "project", "item-list", ubs.PROJECT_NUMBER, "--owner", ubs.OWNER,
                        "--format", "json", "-L", "500"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace", check=True)
    out = []
    for i in json.loads(r.stdout)["items"]:
        if i.get("type") != "Project":
            continue
        m = re.search(r"\[Project\]:\s*(.+)", i["content"]["title"])
        if m:
            out.append((i["id"], m.group(1).strip(), ubs.board_number(i, "Task Count"), ubs.board_number(i, FIELD_NAME)))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("slugs", nargs="*", help="only these board projects (default: all)")
    ap.add_argument("--apply", action="store_true", help="write to the board (default: dry run)")
    ap.add_argument("--force", action="store_true", help="also replace a value that is already set")
    ap.add_argument("--repo", default=os.path.dirname(os.path.dirname(HERE)), help="git repo holding projects/ (default: this repo)")
    ap.add_argument("--ref", help="read only this ref's history (default: all refs)")
    a = ap.parse_args()

    firsts = first_versions(a.repo, a.ref)
    fid = field_id(create=a.apply)
    if fid is None:
        print(f'NOTE the board has no "{FIELD_NAME}" field yet; a dry run shows what would be written, --apply creates it')
    items = board_items()
    if a.slugs:
        want = {s.lower() for s in a.slugs}
        items = [i for i in items if i[1].lower() in want]

    stats = {"set": 0, "kept": 0, "skipped": 0, "failed": 0}
    growths = []
    for item_id, slug, task_count, current in items:
        path = find_index_path(firsts, slug)
        if path is None:
            stats["skipped"] += 1
            print(f"SKIP {slug}: no projects/<slug>/tasks/TASK-INDEX.md in the history")
            continue
        planned, rows, poml = planned_for(a.repo, path, firsts[path])
        if planned == 0:
            stats["skipped"] += 1
            print(f"SKIP {slug}: first TASK-INDEX version has no task rows and no POMLs")
            continue
        value = planned if current is None or a.force else int(current)
        g = growth(task_count, value)
        if g is not None:
            growths.append((g, slug, value, task_count))
        if not should_write(current, a.force):
            stats["kept"] += 1
            print(f"KEEP {slug}: already {int(current)} (would be {planned})")
            continue
        label = f"{slug}: {planned} (index rows {rows}, POMLs {poml}) at {firsts[path][:9]}"
        if not a.apply:
            stats["set"] += 1
            print(f"WOULD SET {label}")
            continue
        ok, err = ubs.set_field(item_id, fid, "--number", planned)
        if ok:
            stats["set"] += 1
            print(f"SET {label}")
        else:
            stats["failed"] += 1
            print(f"FAIL {slug}: {err}")

    verb = "set" if a.apply else "would be set"
    print(f"\n{stats['set']} {verb}, {stats['kept']} kept (already set), {stats['skipped']} skipped (no TASK-INDEX), "
          f"{stats['failed']} failed - out of {len(items)} board projects")
    growths.sort(reverse=True)
    print("\nLargest growth, Task Count / Tasks Planned:")
    for g, slug, planned, now in growths[:10]:
        print(f"  {g:5.1f}x  {slug}  ({planned} planned -> {int(now)} now)")
    if stats["failed"]:
        sys.exit(2)


if __name__ == "__main__":
    main()
