"""Clobber check before a master deploy: what is live on a Dataverse environment, compared with git.

Downloads every Spaarke web resource (sprk_*, cc_Spaarke*, cc_Sprk*) from the environment into
--out (that folder is also the restore point: the exact pre-deploy bytes), then classifies each one
by its git blob hash (raw, CRLF->LF, LF->CRLF, BOM-stripped):

  CURRENT       identical to a file in the target ref's tree
  STALE_MASTER  identical to an older version of a file in the target ref's history (safe to overwrite)
  BRANCH_ONLY   found only on another remote branch -> someone deployed unmerged work: STOP
  UNKNOWN       not in git at all: a built artefact (code page, PCF bundle) or a manual edit

CURRENT only proves the live bytes equal *some* committed file. For code pages that is usually the
SpaarkeMaster/WebResources mirror, a snapshot, so a fresh build from master can still differ: build
and compare those before deploying (master-deploy Step 0.5).

Usage:
  python scripts/master-deploy/check-dev-drift.py --out <dir> [--org https://spaarkedev1.crm.dynamics.com] [--ref origin/master]

Exit code: 0 = no BRANCH_ONLY content; 2 = BRANCH_ONLY content found (do not deploy over it); 1 = error.
Run `git fetch origin` first so the ref and the remote branches are current.
"""
import argparse, base64, hashlib, json, os, subprocess, sys, urllib.parse, urllib.request
from collections import defaultdict


def git(repo, *args):
    return subprocess.run(["git", "-C", repo, *args], capture_output=True, text=True,
                          encoding="utf-8", errors="ignore").stdout


def blob(data):
    return hashlib.sha1(b"blob %d\0" % len(data) + data).hexdigest()


def variants(raw):
    out = {raw, raw.replace(b"\r\n", b"\n"), raw.replace(b"\n", b"\r\n").replace(b"\r\r\n", b"\r\n")}
    if raw.startswith(b"\xef\xbb\xbf"):
        out |= {v[3:] for v in list(out)}
    return {blob(v) for v in out}


def history(repo, *refs):
    seen, cur = {}, ""
    for line in git(repo, "log", *refs, "--format=%h %cs", "--raw", "--no-abbrev", "--no-renames",
                    "--", "src", "infrastructure", "infra").splitlines():
        if line and not line.startswith(":"):
            cur = line
        elif line.startswith(":"):
            parts = line.split("\t")
            seen.setdefault(parts[0].split()[3], (cur, parts[1]))
    return seen


def download(org, out_dir):
    token = subprocess.run(["az", "account", "get-access-token", "--resource", org, "--query", "accessToken",
                            "-o", "tsv"], capture_output=True, text=True, shell=os.name == "nt").stdout.strip()
    if not token:
        raise SystemExit("no Dataverse token; run az login")
    flt = "startswith(name,'sprk_') or startswith(name,'cc_Spaarke') or startswith(name,'cc_Sprk')"
    url = org + "/api/data/v9.2/webresourceset?" + urllib.parse.urlencode(
        {"$select": "name,webresourcetype,modifiedon,content", "$filter": flt})
    live_dir = os.path.join(out_dir, "live")
    os.makedirs(live_dir, exist_ok=True)
    index = []
    while url:
        req = urllib.request.Request(url, headers={"Authorization": "Bearer " + token, "Accept": "application/json",
                                                   "Prefer": "odata.maxpagesize=200"})
        page = json.load(urllib.request.urlopen(req, timeout=300))
        for r in page["value"]:
            raw = base64.b64decode(r.get("content") or "")
            fn = r["name"].replace("/", "__")
            with open(os.path.join(live_dir, fn), "wb") as f:
                f.write(raw)
            index.append({"name": r["name"], "type": r["webresourcetype"], "modifiedon": r["modifiedon"],
                          "file": fn, "size": len(raw)})
        url = page.get("@odata.nextLink")
    with open(os.path.join(out_dir, "live_index.json"), "w", encoding="utf-8") as f:
        json.dump(index, f, indent=1)
    return index


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--org", default="https://spaarkedev1.crm.dynamics.com")
    ap.add_argument("--ref", default="origin/master")
    ap.add_argument("--repo", default=".")
    ap.add_argument("--out", required=True, help="restore-point folder (outside the repo)")
    a = ap.parse_args()

    index = download(a.org.rstrip("/"), a.out)
    tree = defaultdict(list)
    for line in git(a.repo, "ls-tree", "-r", a.ref).splitlines():
        meta, path = line.split("\t", 1)
        tree[meta.split()[2]].append(path)
    on_ref = history(a.repo, a.ref)
    off_ref = history(a.repo, "--remotes", "--not", a.ref)

    groups = defaultdict(list)
    for r in index:
        with open(os.path.join(a.out, "live", r["file"]), "rb") as f:
            hashes = variants(f.read())
        cls, info = "UNKNOWN", ""
        for h in hashes:
            if h in tree:
                cls, info = "CURRENT", tree[h][0]
                break
        else:
            hit = next((h for h in hashes if h in on_ref), None)
            if hit:
                cls, info = "STALE_MASTER", "%s %s" % on_ref[hit]
            else:
                hit = next((h for h in hashes if h in off_ref), None)
                if hit:
                    cls, info = "BRANCH_ONLY", "%s %s" % off_ref[hit]
        groups[cls].append((r["modifiedon"][:16], r["name"], info))

    for cls in ("BRANCH_ONLY", "STALE_MASTER", "UNKNOWN", "CURRENT"):
        print("=== %s (%d)" % (cls, len(groups[cls])))
        if cls != "CURRENT":
            for row in sorted(groups[cls], reverse=True):
                print("  ", *row)
    print("restore point: %s (%d web resources)" % (os.path.abspath(a.out), len(index)))
    return 2 if groups["BRANCH_ONLY"] else 0


if __name__ == "__main__":
    sys.exit(main())
