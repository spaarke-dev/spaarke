#!/usr/bin/env python3
"""Render and publish the "Issues & Quality Dashboard" issue.

Used by .github/workflows/issues-dashboard.yml (needs GH_TOKEN and GH_REPO in the env,
the `gh` CLI on PATH). Finds the dashboard issue by label `dashboard` + exact title (never
creates a second one), regenerates its BODY (edit in place, never a comment, so it causes
no notification noise), reopens it if it was closed, and pins it once.

  --dry-run   print the body and what would be done; write nothing
"""
import argparse
import json
import os
import re
import subprocess
import sys
from datetime import datetime, timedelta, timezone

TITLE = "📊 Issues & Quality Dashboard"
LABEL = "dashboard"
ALERT_LABEL = "quality-alert"
NIGHTLIES = [
    "nightly-health.yml",
    "client-tests.yml",
    "mutation-nightly.yml",
    "pcf-build-prod-nightly.yml",
    "report-workflow-health.yml",
]
NOW = datetime.now(timezone.utc)
REPO = os.environ.get("GH_REPO") or os.environ.get("GITHUB_REPOSITORY", "")
SERVER = os.environ.get("GITHUB_SERVER_URL", "https://github.com")
RUN_URL = (
    f"{SERVER}/{REPO}/actions/runs/{os.environ['GITHUB_RUN_ID']}"
    if os.environ.get("GITHUB_RUN_ID")
    else ""
)
LIST_LIMIT = 1000


def gh(*args, check=True):
    p = subprocess.run(["gh", *args], capture_output=True, text=True, encoding="utf8")
    if check and p.returncode != 0:
        raise RuntimeError(f"gh {' '.join(args[:3])} failed: {p.stderr.strip()}")
    return p


def gh_json(*args, default=None):
    p = gh(*args, check=False)
    if p.returncode != 0:
        print(f"::warning::gh {' '.join(args[:3])} failed: {p.stderr.strip()[:200]}")
        return default
    return json.loads(p.stdout or "null")


def ts(s):
    return datetime.fromisoformat(s.replace("Z", "+00:00"))


def day(s):
    return ts(s).strftime("%Y-%m-%d")


def cell(text, limit=90):
    text = " ".join((text or "").split())
    if len(text) > limit:
        text = text[: limit - 1] + "…"
    return text.replace("|", "\\|").replace("<", "&lt;").replace(">", "&gt;")


def link(issue):
    return f"[#{issue['number']}]({issue['url']})"


def who(issue):
    names = [a["login"] for a in issue.get("assignees", [])]
    return ", ".join(f"`{n}`" for n in names) if names else "_unassigned_"


def labels(issue):
    names = [l["name"] for l in issue.get("labels", []) if l["name"] != "quality-alert"]
    return ", ".join(f"`{n}`" for n in names[:4]) or ""


def table(header, rows, empty):
    if not rows:
        return [empty]
    out = ["| " + " | ".join(header) + " |", "|" + "|".join("---" for _ in header) + "|"]
    out += ["| " + " | ".join(r) + " |" for r in rows]
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    fields = "number,title,url,labels,assignees,createdAt,updatedAt,state,body"
    open_issues = gh_json("issue", "list", "--state", "open", "--limit", str(LIST_LIMIT), "--json", fields, default=[])
    open_issues = [i for i in open_issues if i["title"] != TITLE]
    capped = len(open_issues) >= LIST_LIMIT - 1
    plus = "+" if capped else ""

    md = [f"# {TITLE}", ""]
    stamp = NOW.strftime("%Y-%m-%d %H:%M UTC")
    md.append(
        f"_Updated {stamp}" + (f" · [run]({RUN_URL})" if RUN_URL else "")
        + ". Regenerated every 6 hours and on issue events. This issue is edited in place: it never gets comments._"
    )
    md.append("")

    # 1. Open quality alerts
    alerts = [i for i in open_issues if any(l["name"] == ALERT_LABEL for l in i["labels"])]
    md.append(f"## 🔴 Open quality alerts ({len(alerts)})")
    md.append("")
    rows = []
    for i in sorted(alerts, key=lambda x: x["number"]):
        m = re.search(r"first-seen=(\d{4}-\d{2}-\d{2})", i.get("body") or "")
        since = m.group(1) if m else day(i["createdAt"])
        rows.append([link(i), cell(i["title"].replace("🔴 Quality alert: ", "")), since, who(i), day(i["updatedAt"])])
    md += table(["Alert", "Check", "Failing since", "Assignee", "Last updated"], rows,
                "✅ None open: every alerting check passed its last run.")
    md.append("")

    # 2. Nightly checks
    md.append("## Nightly checks")
    md.append("")
    default_branch = "master"
    info = gh_json("repo", "view", "--json", "defaultBranchRef", default=None)
    if info and info.get("defaultBranchRef"):
        default_branch = info["defaultBranchRef"]["name"]
    rows = []
    for wf in NIGHTLIES:
        runs = gh_json("run", "list", "--workflow", wf, "--branch", default_branch, "--limit", "10",
                       "--json", "conclusion,status,createdAt,url,event", default=None)
        if runs is None:
            rows.append([f"`{wf}`", "⚠️ could not query run history (transient API error or workflow not on " + default_branch + " yet)", "", ""])
            continue
        done = [r for r in runs if r["status"] == "completed"]
        if not done:
            rows.append([f"`{wf}`", "⚠️ no completed run on " + default_branch, "", ""])
            continue
        r = done[0]
        c = r["conclusion"]
        icon = {"success": "✅", "failure": "❌", "timed_out": "❌", "startup_failure": "❌"}.get(c, "⚠️")
        note = ""
        if c == "cancelled":
            note = " (cancelled counts as not-passing: usually a job hit its timeout)"
        age = (NOW - ts(r["createdAt"])).days
        stale = f" ⚠️ {age}d old" if age >= 2 else ""
        rows.append([f"`{wf}`", f"{icon} {c}{note}", f"[{day(r['createdAt'])}]({r['url']}){stale}", r["event"]])
    md += table(["Workflow", "Last run", "When", "Trigger"], rows, "_no workflows_")
    md.append("")

    # 3. Opened in the last 7 days
    since7 = (NOW - timedelta(days=7)).strftime("%Y-%m-%d")
    recent = gh_json("issue", "list", "--state", "all", "--search", f"created:>={since7}", "--limit", "200",
                     "--json", fields, default=[]) or []
    recent = [i for i in recent if i["title"] != TITLE]
    md.append(f"## Issues opened in the last 7 days ({len(recent)})")
    md.append("")
    rows = [[link(i), cell(i["title"]), labels(i), who(i), i["state"].lower()]
            for i in sorted(recent, key=lambda x: x["createdAt"], reverse=True)[:60]]
    md += table(["#", "Title", "Labels", "Assignee", "State"], rows, "_None._")
    if len(recent) > 60:
        md.append(f"_…and {len(recent) - 60} more._")
    md.append("")

    # 4. Stale
    cutoff = NOW - timedelta(days=30)
    stale = sorted(
        (i for i in open_issues if ts(i["updatedAt"]) < cutoff and not any(l["name"] == ALERT_LABEL for l in i["labels"])),
        key=lambda x: x["updatedAt"],
    )
    md.append(f"## Open issues with no activity for 30+ days: {len(stale)}{plus}")
    md.append("")
    rows = [[link(i), cell(i["title"]), day(i["updatedAt"]), who(i)] for i in stale[:10]]
    md += table(["#", "Title (10 oldest)", "Last activity", "Assignee"], rows, "_None._")
    md.append("")

    # 5. Totals
    prs = gh_json("pr", "list", "--state", "open", "--limit", str(LIST_LIMIT), "--json", "number", default=None)
    unassigned = sum(1 for i in open_issues if not i["assignees"])
    md.append("## Totals")
    md.append("")
    md.append("| Open issues | Open PRs | Open issues with no assignee |")
    md.append("|---:|---:|---:|")
    md.append(f"| {len(open_issues)}{plus} | {len(prs) if prs is not None else 'n/a'} | {unassigned}{plus} |")
    md.append("")
    md.append("_Generated by `.github/workflows/issues-dashboard.yml`. Alerts are opened and closed by `.github/actions/quality-alert`; "
              "see `docs/procedures/ci-cd-workflow.md`, \"Quality alerts\"._")
    body = "\n".join(md) + "\n"

    if args.dry_run:
        print(body)
        print("[dry-run] would create/update the dashboard issue and pin it")
        return 0

    # Find (never duplicate) the dashboard issue.
    found = gh_json("issue", "list", "--label", LABEL, "--state", "all", "--limit", "50",
                    "--json", "number,title,state,id", default=None)
    if found is None:
        print("::error::could not look up the dashboard issue; not creating a possible duplicate")
        return 1
    mine = sorted((i for i in found if i["title"] == TITLE), key=lambda x: x["number"])
    path = "dashboard-body.md"
    with open(path, "w", encoding="utf8") as f:
        f.write(body)

    if not mine:
        gh("label", "create", LABEL, "--color", "0E8A16", "--description",
           "Auto-maintained dashboard issue (do not comment)", check=False)
        out = gh("issue", "create", "--title", TITLE, "--body-file", path, "--label", LABEL).stdout.strip()
        number = int(out.rstrip("/").split("/")[-1])
        print(f"Created dashboard issue #{number}")
    else:
        number = mine[0]["number"]
        if mine[0]["state"] == "CLOSED":
            gh("issue", "reopen", str(number))
        gh("issue", "edit", str(number), "--body-file", path)
        print(f"Updated dashboard issue #{number}")

    pin(number)
    return 0


def pin(number):
    q = gh("api", "graphql", "-f", "query=query($o:String!,$n:String!,$i:Int!){repository(owner:$o,name:$n){issue(number:$i){id isPinned}}}",
           "-f", f"o={REPO.split('/')[0]}", "-f", f"n={REPO.split('/')[1]}", "-F", f"i={number}", check=False)
    if q.returncode != 0:
        print(f"::warning::could not read the pin state of #{number}: {q.stderr.strip()[:200]}. Pin it manually (Issue page > Pin issue).")
        return
    issue = json.loads(q.stdout)["data"]["repository"]["issue"]
    if issue["isPinned"]:
        print(f"#{number} is already pinned.")
        return
    p = gh("api", "graphql", "-f", "query=mutation($id:ID!){pinIssue(input:{issueId:$id}){issue{number}}}",
           "-f", f"id={issue['id']}", check=False)
    if p.returncode != 0:
        print(f"::warning::GITHUB_TOKEN could not pin issue #{number} ({p.stderr.strip()[:200]}). "
              "Owner: pin it manually (open the issue > Pin issue).")
    else:
        print(f"Pinned #{number}.")


if __name__ == "__main__":
    sys.exit(main())
