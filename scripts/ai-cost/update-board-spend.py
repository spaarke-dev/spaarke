"""Refresh the AI Spend (est.) / AI Calls / AI Spend As Of fields on the Spaarke
Core portfolio board (Project #2) for one or more projects, from local Claude
Code transcripts (see get-project-cost.py for the pricing/estimation method).

This is intentionally a MANUALLY-TRIGGERED script, invoked via the
`/project-spend-update` skill - not wired into task-execute or any other
automatic hook. See .claude/skills/project-spend-update/SKILL.md for the
rationale (a task-completion-tied refresh under-covers Workflow-tool-driven
sessions that don't route through task-execute Step 9.6).

The board figures never go down: transcripts are deleted over time, so a fresh total that is lower
than the value already on the board is not written (`KEEP <slug> ...`), and AI Spend As Of is only
touched when something is written. When the spend archive exists (spend-report.py --archive), the
figure per project is max(transcript all-time total, archive all-time total).

AI Cost (allocated) is the invoiced actual cost given to each project: each billing cycle's invoices
(the hand-kept invoice list, see spend-report.py --actuals) split by the projects' share of that cycle's
archived estimate. It is written as computed, even when lower: both inputs are durable, and a project's
share of the open cycle shrinks when other projects' usage grows. Without an invoice list it is not written.

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
slug_candidates = _gpc.slug_candidates

# spend-report.py owns the durable archive (see README "Durable archive") and the folder -> project name rule.
_spec2 = importlib.util.spec_from_file_location(
    "spend_report", os.path.join(_here, "spend-report.py"))
_sr = importlib.util.module_from_spec(_spec2)
_spec2.loader.exec_module(_sr)

OWNER = "spaarke-dev"
PROJECT_NUMBER = "2"
PROJECT_ID = "PVT_kwHODW0Pv84BEgWu"
F_SPEND = "PVTF_lAHODW0Pv84BEgWuzhkWwag"   # "AI Spend (est.)" - NUMBER
F_ASOF = "PVTF_lAHODW0Pv84BEgWuzhkWwcU"    # "AI Spend As Of" - DATE
F_CALLS = "PVTF_lAHODW0Pv84BEgWuzhkWwdQ"   # "AI Calls" - NUMBER
F_ALLOC = "PVTF_lAHODW0Pv84BEgWuzhncDeA"   # "AI Cost (allocated)" - NUMBER
# Re-derive these IDs if the board is ever rebuilt:
#   gh project field-list 2 --owner spaarke-dev --format json

def board_number(item, field_name):
    """Current numeric value of a custom field on a `gh project item-list` entry, or None if unset.

    gh lower-cases the first letter of the field name in the JSON ("AI Spend (est.)" arrives as
    "aI Spend (est.)"), so match case-insensitively."""
    want = field_name.lower()
    for k, v in item.items():
        if k.lower() == want:
            return v if isinstance(v, (int, float)) and not isinstance(v, bool) else None
    return None


def board_projects():
    """Return [(item_id, slug, current_spend, current_calls), ...] for every Type=Project item.

    current_* is the figure already on the board (None if never set)."""
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
            out.append((i["id"], m.group(1).strip(),
                        board_number(i, "AI Spend (est.)"), board_number(i, "AI Calls")))
    return out

def set_field(item_id, field_id, flag, value):
    r = subprocess.run(
        ["gh", "project", "item-edit", "--id", item_id, "--project-id", PROJECT_ID,
         "--field-id", field_id, flag, str(value)],
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    return r.returncode == 0, r.stderr.strip()

def decide_write(fresh_cost, fresh_calls, board_cost, board_calls):
    """Monotonic rule for the board: (write_spend, write_calls).

    A figure is written unless it is LOWER than the value already on the board (None = never set, so
    write). Transcripts are deleted over time, so a lower fresh total means data was lost, not that
    the spend went down. Equal values are written, which refreshes AI Spend As Of."""
    write_spend = board_cost is None or fresh_cost >= board_cost
    write_calls = board_calls is None or fresh_calls >= board_calls
    return write_spend, write_calls


def best_totals(transcript, archive):
    """Per project all-time {cost, calls}: the max of the transcript total and the archive total, per
    figure. Either may be None (not available); returns None if both are."""
    have = [t for t in (transcript, archive) if t]
    if not have:
        return None
    return {"cost": max(t["cost"] for t in have), "calls": max(t["calls"] for t in have)}


def archive_totals(path=None):
    """{lower-case project name: {cost, calls}} summed over every archived day ({} if no archive)."""
    path = path or _sr.DEFAULT_ARCHIVE
    try:
        rows = _sr.read_archive(path)
    except _sr.ArchiveError as ex:
        print(f"WARN     archive ignored: {ex}")
        return {}
    out = {}
    for r in rows:
        t = out.setdefault(r["project"].lower(), {"cost": 0.0, "calls": 0})
        t["cost"] += sum(r[k] for k in _sr.COST_FIELDS)
        t["calls"] += r["calls"]
    return {k: {"cost": round(v["cost"], 2), "calls": v["calls"]} for k, v in out.items()}


def allocated_totals(invoices_path=None, archive_path=None):
    """({lower-case project name: allocated actual $}, date of the last invoice); ({}, None) without invoices."""
    try:
        invoices = _sr.read_invoices(invoices_path or _sr.DEFAULT_INVOICES)
        arows = _sr.read_archive(archive_path or _sr.DEFAULT_ARCHIVE)
    except _sr.ArchiveError as ex:
        print(f"WARN     allocation skipped: {ex}")
        return {}, None
    if not invoices:
        return {}, None
    rep = _sr.actuals_report(invoices, arows)
    return ({p.lower(): t["allocated"] for p, t in rep["projects"].items() if t["allocated"] > 0},
            invoices[-1]["date"])


def archive_lookup(archive, slug, folder):
    """The archive total for a board slug. Candidates in order: the name derived from the transcript
    folder (what spend-report.py archived it under), then the slug, then the slug minus `spaarke-`."""
    names = ([_sr.project_name(folder)] if folder else []) + slug_candidates(slug)
    for n in names:
        a = (archive or {}).get(n.lower())
        if a:
            return a
    return None


def update_one(slug, item_id, today, board_spend=None, board_calls=None, archive=None, known_slugs=None):
    """Returns (status, totals, kept_fields). `totals` is the figure computed this run (before the
    never-go-down rule); `kept_fields` lists the fields left unchanged because the board was higher."""
    folder = find_folder(slug, known_slugs)
    r = cost_for_folder(folder) if folder else None
    if r is not None and r["calls"] == 0:
        r = None
    # The archive keys projects by the same name spend-report.py derives from the transcript folder.
    a = archive_lookup(archive, slug, folder)
    r = best_totals(r, a)
    if r is None:
        return ("no-usage-found" if folder else "no-local-history"), None, []
    ws, wc = decide_write(r["cost"], r["calls"], board_spend, board_calls)
    kept = [n for n, w in (("spend", ws), ("calls", wc)) if not w]
    if not (ws or wc):
        return "kept", r, kept
    errs, wrote = [], 0
    for want, field_id, flag, value in ((ws, F_SPEND, "--number", r["cost"]),
                                        (wc, F_CALLS, "--number", r["calls"])):
        if want:
            ok, e = set_field(item_id, field_id, flag, value)
            wrote += ok
            if not ok:
                errs.append(e)
    if wrote:                       # As Of last, and only once a figure is actually on the board
        ok, e = set_field(item_id, F_ASOF, "--date", today)
        if not ok:
            errs.append(e)
    if errs:
        return "edit-failed: " + " | ".join(e for e in errs if e), r, kept
    return "updated", r, kept


def main():
    args = sys.argv[1:]
    today = datetime.date.today().isoformat()
    targets = args if args else None

    board = board_projects()
    if targets is None:
        pairs = board
    else:
        all_pairs = {slug: (item_id, spend, calls) for item_id, slug, spend, calls in board}
        pairs = []
        for slug in targets:
            found = all_pairs.get(slug)
            if not found:
                print(f"SKIP {slug}: not found as a Type=Project board item "
                      f"(run /devops-project-register first)")
                continue
            pairs.append((found[0], slug, found[1], found[2]))

    archive = archive_totals()
    known = [p[1] for p in board]      # every board slug, so a fuzzy folder match cannot cross releases
    updated = kept_n = skipped = failed = 0
    for item_id, slug, cur_spend, cur_calls in pairs:
        status, r, kept = update_one(slug, item_id, today, cur_spend, cur_calls, archive, known)
        if status == "updated":
            updated += 1
            print(f"OK       {slug:55s} ${r['cost']:>10,.2f}  {r['calls']:>7,d} calls")
            if kept:
                print(f"KEEP {slug} (fresh < board; transcripts likely pruned) [kept: {', '.join(kept)}]")
        elif status == "kept":
            kept_n += 1
            print(f"KEEP {slug} (fresh < board; transcripts likely pruned)")
        elif status in ("no-local-history", "no-usage-found"):
            skipped += 1
            print(f"SKIP     {slug:55s} ({status})")
        else:
            failed += 1
            print(f"FAIL     {slug:55s} {status}")

    print(f"\n{updated} updated, {kept_n} kept (board already higher), {skipped} skipped (no local data), "
          f"{failed} failed - out of {len(pairs)} targeted")

    alloc, through = allocated_totals()
    if not alloc:
        print("AI Cost (allocated): not written (no invoice list, or no invoices fall in an archived billing cycle)")
        return
    a_ok, a_failed = 0, 0
    for item_id, slug, _, _ in pairs:
        v = archive_lookup(alloc, slug, find_folder(slug, known))
        if not v:
            continue
        ok, e = set_field(item_id, F_ALLOC, "--number", v)
        if ok:
            a_ok += 1
            print(f"ALLOC    {slug:55s} ${v:>10,.2f}")
        else:
            a_failed += 1
            print(f"FAIL     {slug:55s} allocated: {e}")
    print(f"AI Cost (allocated): {a_ok} written, {a_failed} failed (invoices through {through})")

if __name__ == "__main__":
    main()
