"""build_package.py - assemble, gate and zip the individual-base package of the Spaarke Legal AI Accelerator.

Usage: <real python> build_package.py [--out DIR] [--kit DIR]
Normally run through Build-Package.ps1, which runs the NDA build first. Build-time dependencies: PyYAML, python-docx, lxml.

Output (under <out>/package/):
  individual-base/Documents/...                         the tree the user ends up with in OneDrive Documents
  Spaarke Legal Accelerator - Individual base v0.1.zip  its top level is the CONTENTS of Documents/
  test-documents/Spaarke Test Documents/...
  Spaarke test documents v0.1.zip                       its top level is Spaarke Test Documents/
  manifest.json                                         sizes and SHA-256 of every file

Documents/Cowork/ holds Skills/ (the nda build's three skills + legal-hold-review) and Playbooks/ (per document
type: the four-column Word playbook, the readable "(agent knowledge)" copy and the Markdown master; the review method;
Start here). No network or model calls; nothing secret. Exit 0 only when every gate passes.
"""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile
from datetime import datetime, timezone

import yaml

BUILD = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(BUILD)
REPO = os.path.abspath(os.path.join(ROOT, "..", ".."))
ENGINES = os.path.join(ROOT, "core", "engines")
sys.path[:0] = [BUILD, ENGINES]

import build_accelerator as ba  # noqa: E402
import gates  # noqa: E402
import package_docs as docs  # noqa: E402
import playbook_parse  # noqa: E402
import render_word_playbook as rw  # noqa: E402

VERSION = "0.1"
PACKAGE_ZIP = f"Spaarke Legal Accelerator - Individual base v{VERSION}.zip"
TEST_ZIP = f"Spaarke test documents v{VERSION}.zip"
DEFAULT_KIT = os.path.join(REPO, "projects", "spaarke-legal-ai-accelerator-r1", "notes", "poc-e2e")
REVIEW_METHOD_SRC = os.path.join(ROOT, "core", "shared-references", "spaarke-review-method.md")
METHOD_DOCX = "Spaarke Review Method.docx"
NDA_SKILLS = ("nda-review", "nda-library-review", "accelerator-setup")

# the library playbooks of the package: practice (topics catalogue), Type folder under Cowork/Playbooks, master file
# method_inside: the Word copy starts with the review method (non-contract only; gap #36, test G-5). A contract
# playbook never gets it: Word's Review contract takes one file and imports the surrounding text as guidelines.
PLAYBOOKS = [
    {"practice": "nda", "folder": "NDA", "require_side": True, "method_inside": False,
     "source": os.path.join(ROOT, "practices", "nda", "positions", "Spaarke NDA – Mutual v1.2.md")},
    {"practice": "legal-hold", "folder": "Legal Hold Notice", "require_side": False, "method_inside": True,
     "source": os.path.join(ROOT, "practices", "legal-hold", "positions",
                            "Spaarke Legal Hold Notice – Employee Custodians v1.0.md")},
]
START_HERE = {
    "readme": "Spaarke – Read me first.docx",
    "instruction": "Spaarke – Custom instruction.txt",
}
# nda-build inputs: the package is stale when any of them is newer than the nda build's manifest
NDA_INPUT_GLOBS = [("core/engines", ".py"), ("core/shared-references", None), ("core/schemas", None),
                   ("core/skills", None), ("practices/nda/skills", None)]
NDA_INPUT_FILES = ["practices/nda/topics.yaml", "practices/nda/accelerator.yaml", "practices/nda/onboarding.md",
                   "practices/nda/positions/baseline-mutual.md"]


class GateFailure(Exception):
    pass


def log(msg):
    print(msg, flush=True)


def newest_input_mtime():
    files = [os.path.join(ROOT, f) for f in NDA_INPUT_FILES]
    for folder, ext in NDA_INPUT_GLOBS:
        for dp, dn, fn in os.walk(os.path.join(ROOT, folder)):
            dn[:] = [d for d in dn if d not in ("__pycache__", "tests")]
            files += [os.path.join(dp, f) for f in fn if (ext is None or f.endswith(ext)) and not f.endswith(".pyc")]
    return max(os.path.getmtime(f) for f in files)


def nda_skills_dir(out):
    manifest = os.path.join(out, "nda", "manifest.json")
    skills = os.path.join(out, "nda", "t0", "skills")
    if not os.path.isfile(manifest) or not all(os.path.isdir(os.path.join(skills, s)) for s in NDA_SKILLS):
        raise GateFailure("the NDA build output is missing: run Build-Accelerator.ps1 -Practice nda first")
    if newest_input_mtime() > os.path.getmtime(manifest):
        raise GateFailure("the NDA build output is older than its sources: run Build-Accelerator.ps1 -Practice nda again")
    return skills


def catalogue_for(practice, gen_dir):
    cat = ba.generate_topic_files(os.path.join(ROOT, "practices", practice), gen_dir)
    sides = {t["id"]: set(t["sides"]) for t in cat["topics"] if t.get("status") != "retired"}
    return cat, sides


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def render_playbook(spec, work, lk):
    """Validate one library playbook against its catalogue and write its three files under Cowork/Playbooks/<Type>/."""
    cat, sides = catalogue_for(spec["practice"], os.path.join(work, spec["practice"], "generated"))
    text = read(spec["source"])
    try:
        pb = playbook_parse.parse(text, sides, require_side=spec["require_side"])
    except playbook_parse.FormatError as ex:
        raise GateFailure(f"{os.path.basename(spec['source'])}: " + "; ".join(ex.args[0])) from None
    stem = os.path.splitext(os.path.basename(spec["source"]))[0]
    folder = os.path.join(lk, spec["folder"])
    os.makedirs(os.path.join(folder, "Playbook master"), exist_ok=True)
    preface = docs.method_preface(read(REVIEW_METHOD_SRC)) if spec["method_inside"] else None
    four = rw.render_table(pb, cat, os.path.join(folder, f"{stem}.docx"), preface=preface)
    problems = rw.check_table(four, pb, cat, preface=preface)
    readable = rw.render_readable(pb, cat, os.path.join(folder, f"{stem} (agent knowledge).docx"))
    problems += rw.check_readable(readable, pb, cat)
    if problems:
        raise GateFailure(f"{stem}: " + "; ".join(problems))
    shutil.copy2(spec["source"], os.path.join(folder, "Playbook master", f"{stem}.md"))
    log(f"  - {stem}: {len(pb['topics'])} topics, silent {pb['silent_topics']}")
    return stem, cat


def build_legal_hold_skill(work, dest):
    with open(os.path.join(ROOT, "practices", "legal-hold", "accelerator.yaml"), encoding="utf-8") as f:
        acc = yaml.safe_load(f)
    cat, sides = catalogue_for("legal-hold", os.path.join(work, "legal-hold", "generated"))
    return [ba.assemble_skill(s, os.path.join(work, "legal-hold", "generated"), dest) for s in acc["skills"]], set(sides)


def lint_skills(dirs_with_ids):
    problems = []
    for d, ids in dirs_with_ids:
        problems += [f"{os.path.basename(d)}: {r}: {m}" for r, m in gates.lint_skill(d, ids)]
    if problems:
        raise GateFailure("lint:\n    " + "\n    ".join(problems))


def run_gate(root, prefix, **kw):
    problems = gates.lint_package(root, prefix, **kw)
    if problems:
        raise GateFailure("package gate:\n    " + "\n    ".join(f"{r}: {m}" for r, m in problems))


def build_individual_base(out, nda_skills, work):
    pkg = os.path.join(out, "package")
    root = os.path.join(pkg, "individual-base", "Documents")
    if os.path.exists(os.path.join(pkg, "individual-base")):
        shutil.rmtree(os.path.join(pkg, "individual-base"))
    skills_dir = os.path.join(root, "Cowork", "Skills")
    lk = os.path.join(root, *gates.KNOWLEDGE_TREE.split("/"))
    os.makedirs(skills_dir)

    log("[1/5] library playbooks: validate, render (four-column, readable), copy the master")
    stems = {}
    for spec in PLAYBOOKS:
        stems[spec["practice"]], _ = render_playbook(spec, work, lk)

    log("[2/5] skills: nda build output + legal-hold-review")
    nda_dirs = []
    for name in NDA_SKILLS:
        dst = os.path.join(skills_dir, name)
        shutil.copytree(os.path.join(nda_skills, name), dst, ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))
        nda_dirs.append(dst)
    nda_ids = set(catalogue_for("nda", os.path.join(work, "nda", "generated"))[1])
    lh_dirs, lh_ids = build_legal_hold_skill(work, skills_dir)
    lint_skills([(d, nda_ids) for d in nda_dirs] + [(d, lh_ids) for d in lh_dirs])
    log("  - " + ", ".join(os.path.basename(d) for d in nda_dirs + lh_dirs) + ": lint ok")

    log("[3/5] review method and Start here")
    method_md = read(REVIEW_METHOD_SRC)
    os.makedirs(os.path.join(lk, "Review method"))
    docs.render_review_method(method_md, os.path.join(lk, "Review method", METHOD_DOCX))
    sh = os.path.join(lk, "Start here")
    os.makedirs(sh)
    docs.render_readme(os.path.join(sh, START_HERE["readme"]), VERSION)
    ci = os.path.join(sh, START_HERE["instruction"])
    with open(ci, "w", encoding="utf-8", newline="\n") as f:
        f.write(docs.CUSTOM_INSTRUCTION)
    for key, a in docs.AGENTS.items():
        text = read(os.path.join(ROOT, a["instructions"])).rstrip("\n") + "\n"
        if os.path.basename(a["knowledge"]).replace(" (agent knowledge).docx", "") != stems[key]:
            raise GateFailure(f"recipe {key}: knowledge file {a['knowledge']!r} does not match playbook {stems[key]!r}")
        problems = docs.recipe_problems(key, text)
        if problems:
            raise GateFailure(f"recipe {key}: " + "; ".join(problems))
        if not os.path.isfile(os.path.join(ROOT, "practices", key, "skills", a["skill"], "SKILL.md")):
            raise GateFailure(f"recipe {key}: the skill it points to, {a['skill']}, does not exist")
        docs.render_recipe(key, text, os.path.join(sh, a["file"]))

    log("[4/5] package gates")
    run_gate(root, "Documents/", custom_instruction=docs.CUSTOM_INSTRUCTION)
    log(f"  - no duplicate names, no path over {gates.MAX_PACKAGE_PATH} characters, "
        f"custom instruction {len(docs.CUSTOM_INSTRUCTION)}/{gates.MAX_CUSTOM_INSTRUCTION} characters")
    return root, stems


def build_test_documents(out, kit, method_md):
    pkg = os.path.join(out, "package")
    parent = os.path.join(pkg, "test-documents")
    if os.path.exists(parent):
        shutil.rmtree(parent)
    top = os.path.join(parent, "Spaarke Test Documents")
    os.makedirs(os.path.join(top, "Decoy"))
    os.makedirs(os.path.join(top, "References"))
    os.makedirs(os.path.join(top, "Screens"))
    open(os.path.join(top, "Screens", ".keep"), "wb").close()
    lk = os.path.join(kit, "kit", "Legal Knowledge")
    for src, name in ((os.path.join(lk, "Matters", "Brightwater Systems", "Brightwater Mutual NDA.docx"),
                       "Brightwater Mutual NDA.docx"),
                      (os.path.join(lk, "Inbox", "NDA batch", "b01 - Kestrel Mutual Non-Disclosure Agreement.docx"),
                       "b01 - Kestrel Mutual Non-Disclosure Agreement.docx"),
                      # Word round 2, R-1 and R-2: reference documents (a precedent and a policy)
                      (os.path.join(lk, "Precedents", "NDA", "Acme Mutual NDA (model form).docx"),
                       os.path.join("References", "Acme Mutual NDA (model form).docx")),
                      (os.path.join(lk, "Grounding", "Normative", "NDA",
                                    "Acme NDA Approval and Signature Authority Policy v1.0.docx"),
                       os.path.join("References", "Acme NDA Approval and Signature Authority Policy v1.0.docx"))):
        if not os.path.isfile(src):
            raise GateFailure(f"test document source missing: {src}")
        shutil.copy2(src, os.path.join(top, name))
    # the Harbor Point drafts: the kit script's own writers (it is imported, not run, so its answer key and
    # user-steps files in the repo are not rewritten); KIT_OUT keeps anything it would write out of the repo
    kit_src = os.path.join(kit, "kit-src")
    if not os.path.isfile(os.path.join(kit_src, "make_doctype_library.py")):
        raise GateFailure(f"make_doctype_library.py not found in {kit_src}")
    scratch = tempfile.mkdtemp()
    old_env = os.environ.get("KIT_OUT")
    os.environ["KIT_OUT"] = scratch
    sys.path.insert(0, kit_src)
    try:
        import make_doctype_library as mk
        for label, body in (("draft A", mk.DRAFT_A), ("draft B", mk.DRAFT_B)):
            title = f"Harbor Point – Legal Hold Notice ({label})"
            mk.write_notice(os.path.join(top, f"{title}.docx"), title, body)
        # Word round 2, R-3: draft a non-contract from a template or precedent, with the matter facts
        refs = os.path.join(top, "References")
        mk.write_template(os.path.join(refs, "Acme Legal Hold Notice Template v1.0.docx"))
        mk.write_precedent(os.path.join(refs, "Legal Hold Notice – Kestrel Freight (2025-02-28).docx"))
        mk.write_demand_letter(os.path.join(refs, "Harbor Point – Demand letter (2026-10-20).docx"))
    finally:
        sys.path.remove(kit_src)
        if old_env is None:
            os.environ.pop("KIT_OUT", None)
        else:
            os.environ["KIT_OUT"] = old_env
        shutil.rmtree(scratch, ignore_errors=True)
    docs.render_review_method(docs.decoy_method(method_md), os.path.join(top, "Decoy", METHOD_DOCX))
    # each zip is checked on its own: the decoy deliberately shares its name with the real method file
    run_gate(parent, "", allowed_duplicates={METHOD_DOCX}, scan_text=True)
    return parent


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def file_list(root):
    out = {}
    for dp, _, fn in os.walk(root):
        for f in fn:
            full = os.path.join(dp, f)
            out[os.path.relpath(full, root).replace("\\", "/")] = os.path.getsize(full)
    return dict(sorted(out.items()))


def verify_zip(path, expected_top, expected_files=()):
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        bad = z.testzip()
    if bad:
        raise GateFailure(f"{os.path.basename(path)}: corrupt entry {bad}")
    tops = {n.split("/")[0] for n in names}
    if tops != set(expected_top):
        raise GateFailure(f"{os.path.basename(path)}: top level is {sorted(tops)}, expected {sorted(expected_top)}")
    missing = [f for f in expected_files if f not in names]
    if missing:
        raise GateFailure(f"{os.path.basename(path)}: missing {missing}")
    return names


def expected_package_files(stems):
    nda, lh = stems["nda"], stems["legal-hold"]
    k = gates.KNOWLEDGE_TREE
    files = [f"{k}/NDA/{nda}.docx", f"{k}/NDA/{nda} (agent knowledge).docx", f"{k}/NDA/Playbook master/{nda}.md",
             f"{k}/Legal Hold Notice/{lh}.docx", f"{k}/Legal Hold Notice/{lh} (agent knowledge).docx",
             f"{k}/Legal Hold Notice/Playbook master/{lh}.md",
             f"{k}/Review method/{METHOD_DOCX}"]
    files += [f"{k}/Start here/{n}" for n in
              (START_HERE["readme"], START_HERE["instruction"], *(a["file"] for a in docs.AGENTS.values()))]
    files += [f"Cowork/Skills/{s}/SKILL.md" for s in (*NDA_SKILLS, "legal-hold-review")]
    return files


def main(argv=None):
    ap = argparse.ArgumentParser(description="Build the individual-base package and the test-documents zip.")
    ap.add_argument("--out", default=os.path.join(BUILD, "out"))
    ap.add_argument("--kit", default=DEFAULT_KIT, help="the poc-e2e folder holding kit/ and kit-src/ (test documents)")
    a = ap.parse_args(argv)
    work = os.path.join(a.out, "package", "_work")
    try:
        if os.path.exists(work):
            shutil.rmtree(work)
        os.makedirs(work)
        nda_skills = nda_skills_dir(a.out)
        root, stems = build_individual_base(a.out, nda_skills, work)
        pkg = os.path.join(a.out, "package")
        log("[5/5] zips")
        zip_path = ba.zip_dir(root, os.path.join(pkg, PACKAGE_ZIP))
        verify_zip(zip_path, {"Cowork"}, expected_package_files(stems))
        test_parent = build_test_documents(a.out, a.kit, read(REVIEW_METHOD_SRC))
        test_zip = ba.zip_dir(test_parent, os.path.join(pkg, TEST_ZIP))
        verify_zip(test_zip, {"Spaarke Test Documents"}, [
            "Spaarke Test Documents/Harbor Point – Legal Hold Notice (draft A).docx",
            "Spaarke Test Documents/Harbor Point – Legal Hold Notice (draft B).docx",
            "Spaarke Test Documents/Decoy/Spaarke Review Method.docx", "Spaarke Test Documents/Screens/.keep",
            "Spaarke Test Documents/References/Acme Mutual NDA (model form).docx",
            "Spaarke Test Documents/References/Acme NDA Approval and Signature Authority Policy v1.0.docx",
            "Spaarke Test Documents/References/Acme Legal Hold Notice Template v1.0.docx",
            "Spaarke Test Documents/References/Legal Hold Notice – Kestrel Freight (2025-02-28).docx",
            "Spaarke Test Documents/References/Harbor Point – Demand letter (2026-10-20).docx"])
    except GateFailure as ex:
        log(f"\nPACKAGE BUILD FAILED: {ex}")
        return 1
    finally:
        shutil.rmtree(work, ignore_errors=True)
    manifest = {
        "package": "individual-base", "version": VERSION,
        "built_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "git_commit": subprocess.run(["git", "rev-parse", "--short", "HEAD"], capture_output=True, text=True,
                                     cwd=ROOT).stdout.strip() or None,
        "zips": {os.path.basename(z): {"bytes": os.path.getsize(z), "sha256": sha256(z)} for z in (zip_path, test_zip)},
        "documents": file_list(root),
        "test_documents": file_list(os.path.join(test_parent, "Spaarke Test Documents")),
    }
    ba.write(os.path.join(pkg, "manifest.json"), json.dumps(manifest, indent=2, ensure_ascii=False))
    log("\nPACKAGE BUILD OK")
    for z in (zip_path, test_zip):
        log(f"  {os.path.relpath(z, a.out)}  ({os.path.getsize(z):,} bytes)")
    for rel, size in manifest["documents"].items():
        if rel.startswith(gates.KNOWLEDGE_TREE + "/"):
            log(f"    {rel}  ({size:,})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
