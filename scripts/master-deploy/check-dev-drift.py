"""Clobber check before a master deploy: what is live on a Dataverse environment, compared with git.

Downloads every Spaarke web resource (sprk_*, cc_Spaarke*, cc_Sprk*) from the environment into
--out (that folder is also the restore point: the exact pre-deploy bytes), then classifies each one
by its git blob hash (raw, CRLF->LF, LF->CRLF, BOM-stripped):

  CURRENT       identical to a file in the target ref's tree (the SpaarkeMaster solution export excluded)
  STALE_MASTER  identical to an older version of a file in the target ref's history (safe to overwrite)
  MIRROR_ONLY   matches only a file (current or older) under src/dataverse/solutions/SpaarkeMaster/, the
                exported solution snapshot, and no maintained source: the snapshot can be stale, so this
                says nothing about master's source. Compare with a fresh build / the source file. Not a stop.
  BRANCH_ONLY   found only on a branch that is not in the ref (any local branch, pushed or not, or any remote
                branch) -> someone deployed unmerged work: STOP
  UNKNOWN       matches no blob in the ref's history or on any local/remote branch: a built artefact (code
                page, PCF bundle), a manual edit, or work from a branch not present in this clone (fetch
                first; the history is limited to src/, infrastructure/ and infra/)

CURRENT only proves the live bytes equal *some* committed source file. Code pages are built, so a fresh
build from master can still differ from the committed copy: build and compare those before deploying
(master-deploy Step 0.5).

Usage:
  python scripts/master-deploy/check-dev-drift.py --out <dir> [--org https://spaarkedev1.crm.dynamics.com] [--ref origin/master]

Exit code: 0 = no BRANCH_ONLY content; 2 = BRANCH_ONLY content found (do not deploy over it); 1 = error.
MIRROR_ONLY does not change the exit code.
Run `git fetch origin` first so the ref and the remote branches are current.
"""
import argparse, base64, hashlib, json, os, subprocess, sys, urllib.parse, urllib.request
from collections import defaultdict, namedtuple

# The exported solution copy of the web resources: a snapshot of dev, not a maintained source.
MIRROR_PREFIX = "src/dataverse/solutions/SpaarkeMaster/"

# blob hash -> where it was found. tree: {hash: [paths]}; the rest: {hash: (commit-and-date, path)}.
Index = namedtuple("Index", "tree mirror_tree on_ref on_ref_mirror off_ref off_ref_mirror")


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
    """Every blob a commit in `refs` wrote under src/infrastructure/infra -> (commit, path).

    Returns (sources, mirror): blobs written under MIRROR_PREFIX are kept apart so a mirror copy can
    never make a live file look like a maintained source.

    Two passes. Ordinary commits first (so a blob is labelled with the commit that introduced it), then
    merge commits diffed against their FIRST parent: that lists every blob the merge result holds that
    the first parent did not, which includes anything written by hand while resolving a conflict (such a
    blob is in no ordinary commit). A plain `git log --raw` shows nothing for a merge. `-m` would add a
    diff against each further parent too, but those only list the first parent's own blobs, which the
    first parent's history already supplies; first-parent is the complete, quieter choice.
    """
    sources, mirror = {}, {}
    for mode in (("--no-merges",), ("--merges", "--diff-merges=first-parent")):
        cur = ""
        for line in git(repo, "log", *refs, *mode, "--format=%h %cs", "--raw", "--no-abbrev", "--no-renames",
                        "--", "src", "infrastructure", "infra").splitlines():
            if line and not line.startswith(":"):
                cur = line
            elif line.startswith(":"):
                parts = line.split("\t")
                target = mirror if parts[1].startswith(MIRROR_PREFIX) else sources
                target.setdefault(parts[0].split()[3], (cur, parts[1]))
    return sources, mirror


def build_index(repo, ref):
    tree, mirror_tree = defaultdict(list), defaultdict(list)
    for line in git(repo, "ls-tree", "-r", ref).splitlines():
        meta, path = line.split("\t", 1)
        (mirror_tree if path.startswith(MIRROR_PREFIX) else tree)[meta.split()[2]].append(path)
    on_ref, on_ref_mirror = history(repo, ref)
    # --branches: local branches too, so a build deployed from this machine before it was pushed is still seen
    off_ref, off_ref_mirror = history(repo, "--branches", "--remotes", "--not", ref)
    return Index(tree, mirror_tree, on_ref, on_ref_mirror, off_ref, off_ref_mirror)


def classify(raw, idx):
    """(class, detail) for the live bytes `raw`. Pure: depends only on the bytes and the index."""
    hashes = variants(raw)
    for h in hashes:
        if h in idx.tree:
            return "CURRENT", idx.tree[h][0]
    for h in hashes:
        if h in idx.on_ref:
            return "STALE_MASTER", "%s %s" % idx.on_ref[h]
    for h in hashes:
        if h in idx.mirror_tree:
            return "MIRROR_ONLY", idx.mirror_tree[h][0]
        if h in idx.on_ref_mirror:
            return "MIRROR_ONLY", "%s %s" % idx.on_ref_mirror[h]
    for h in hashes:
        for found in (idx.off_ref, idx.off_ref_mirror):
            if h in found:
                return "BRANCH_ONLY", "%s %s" % found[h]
    return "UNKNOWN", ""


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
    idx = build_index(a.repo, a.ref)

    groups = defaultdict(list)
    for r in index:
        with open(os.path.join(a.out, "live", r["file"]), "rb") as f:
            cls, info = classify(f.read(), idx)
        groups[cls].append((r["modifiedon"][:16], r["name"], info))

    for cls in ("BRANCH_ONLY", "STALE_MASTER", "MIRROR_ONLY", "UNKNOWN", "CURRENT"):
        print("=== %s (%d)" % (cls, len(groups[cls])))
        if cls != "CURRENT":
            for row in sorted(groups[cls], reverse=True):
                print("  ", *row)
    print("restore point: %s (%d web resources)" % (os.path.abspath(a.out), len(index)))
    return 2 if groups["BRANCH_ONLY"] else 0


if __name__ == "__main__":
    sys.exit(main())
