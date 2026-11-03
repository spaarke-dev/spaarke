"""build_accelerator.py - assemble, gate and package one practice accelerator (design §5.5).

Usage: <real python> build_accelerator.py --practice nda [--out DIR] [--skip-tests] [--regen-corpus]
Normally run through Build-Accelerator.ps1. Build-time dependencies: PyYAML, python-docx, lxml.

Steps: topics.yaml -> topics.json + review-checklist.md; playbooks validated against the topic
catalogue and rendered as Word playbooks; skills assembled (engines -> scripts/, shared references
-> references/); lint gates; engine and gate tests; local end-to-end run; Cowork plugin zip
(unified manifest, agentSkills[]), Claude-format plugin zip, per-skill zips; manifest.json with
the version and SHA-256 of every output. Exit 0 only when every gate passes.
"""
import argparse
import hashlib
import json
import os
import shutil
import struct
import subprocess
import sys
import uuid
import zipfile
import zlib
from datetime import datetime, timezone

import yaml

BUILD = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(BUILD)
ENGINES = os.path.join(ROOT, "core", "engines")
sys.path.insert(0, ENGINES)
sys.path.insert(0, BUILD)

import gates  # noqa: E402
import playbook_parse  # noqa: E402
import render_word_playbook  # noqa: E402


class GateFailure(Exception):
    pass


def log(msg):
    print(msg, flush=True)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


# ---------- generated references ----------
def generate_topic_files(practice_dir, gen_dir):
    with open(os.path.join(practice_dir, "topics.yaml"), encoding="utf-8") as f:
        cat = yaml.safe_load(f)
    ids = [t["id"] for t in cat["topics"]]
    if len(ids) != len(set(ids)):
        raise GateFailure("topics.yaml has duplicate ids")
    for t in cat["topics"]:
        for k in ("id", "title", "question", "sides", "default_priority"):
            if k not in t:
                raise GateFailure(f"topic {t.get('id')} missing {k}")
    write(os.path.join(gen_dir, "topics.json"), json.dumps(cat, ensure_ascii=False, indent=1))
    lines = [f"# {cat['practice'].upper()} review checklist (topic catalogue v{cat['version']})", "",
             "Evaluate every topic that applies to the review side. Use the topic id in the edit plan "
             "(`topic`) and in the review result.", "",
             "| Topic id | Topic | Decide | Look for | Sides | Default priority |", "|---|---|---|---|---|---|"]
    for t in cat["topics"]:
        if t.get("status") == "retired":
            continue
        lines.append(f"| `{t['id']}` | {t['title']} | {t['question']} | {', '.join(t.get('look_for', []))} | "
                     f"{', '.join(t['sides'])} | {t['default_priority']} |")
    write(os.path.join(gen_dir, "review-checklist.md"), "\n".join(lines) + "\n")
    return cat


# ---------- skills ----------
def assemble_skill(spec, gen_dir, out_dir):
    dst = os.path.join(out_dir, spec["name"])
    if os.path.exists(dst):
        shutil.rmtree(dst)
    shutil.copytree(os.path.join(ROOT, spec["source"]), dst,
                    ignore=shutil.ignore_patterns("__pycache__", "*.pyc", ".*"))
    if spec.get("engines"):
        os.makedirs(os.path.join(dst, "scripts"), exist_ok=True)
        for e in spec["engines"]:
            shutil.copy2(os.path.join(ENGINES, e + ".py"), os.path.join(dst, "scripts", e + ".py"))
    for kind in ("references", "assets"):
        for item in spec.get(kind, []):
            src, name = (item["from"], item["as"]) if isinstance(item, dict) else (item, os.path.basename(item))
            src_path = os.path.join(gen_dir, src[len("generated/"):]) if src.startswith("generated/") \
                else os.path.join(ROOT, src)
            os.makedirs(os.path.join(dst, kind), exist_ok=True)
            shutil.copy2(src_path, os.path.join(dst, kind, name))
    return dst


def zip_dir(src_dir, zip_path, arc_prefix=""):
    if os.path.exists(zip_path):
        os.remove(zip_path)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for dp, dn, fn in os.walk(src_dir):
            dn.sort()
            for f in sorted(fn):
                full = os.path.join(dp, f)
                rel = os.path.relpath(full, src_dir).replace("\\", "/")
                z.write(full, (arc_prefix + rel) if arc_prefix else rel)
    return zip_path


# ---------- icons (placeholder; stdlib PNG writer) ----------
def png(path, w, h, pixel):
    raw = b"".join(b"\x00" + b"".join(struct.pack("BBBB", *pixel(x, y)) for x in range(w)) for y in range(h))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + \
        chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)


def make_icons(folder):
    navy, white, clear = (0x1F, 0x38, 0x64, 255), (255, 255, 255, 255), (0, 0, 0, 0)

    def color(x, y):  # navy tile, white page with three text lines and a red tracked line
        if 56 <= x < 136 and 36 <= y < 156:
            if 70 <= x < 122 and y in range(60, 66):
                return (0xC0, 0x00, 0x00, 255)
            if 70 <= x < 122 and (y in range(80, 86) or y in range(100, 106) or y in range(120, 126)):
                return navy
            return white
        return navy

    def outline(x, y):  # white page outline on transparent
        inside = 8 <= x < 24 and 4 <= y < 28
        edge = inside and (x in (8, 23) or y in (4, 27))
        line = 11 <= x < 21 and y in (11, 16, 21)
        return white if edge or line else clear
    png(os.path.join(folder, "color.png"), 192, 192, color)
    png(os.path.join(folder, "outline.png"), 32, 32, outline)


# ---------- plugin ----------
def build_plugins(acc, skill_dirs, t0, topic_ids):
    # lint again: tests ran in between, and what ships is what must pass
    problems = [f"{os.path.basename(d)}: {r}: {m}" for d in skill_dirs for r, m in gates.lint_skill(d, topic_ids)]
    if problems:
        raise GateFailure("lint before packaging:\n    " + "\n    ".join(problems))
    p = acc["plugin"]
    pkg = p["package"]
    version = acc["version"].split("-")[0]
    plug = os.path.join(t0, "plugin")
    if os.path.exists(plug):
        shutil.rmtree(plug)
    os.makedirs(os.path.join(plug, "skills"))
    for d in skill_dirs:
        shutil.copytree(d, os.path.join(plug, "skills", os.path.basename(d)))
    make_icons(plug)
    manifest = {
        "$schema": "https://developer.microsoft.com/json-schemas/teams/v1.30/MicrosoftTeams.schema.json",
        "manifestVersion": "1.30",
        "version": version,
        "id": str(uuid.uuid5(uuid.NAMESPACE_URL, f"https://spaarke.com/accelerators/{pkg}")),
        "developer": {"name": p["developer"]["name"], "websiteUrl": p["developer"]["website"],
                      "privacyUrl": p["developer"]["privacy"], "termsOfUseUrl": p["developer"]["terms"]},
        "name": {"short": p["name_short"], "full": p["name_full"]},
        "description": {"short": p["description_short"], "full": " ".join(p["description_full"].split())},
        "icons": {"color": "color.png", "outline": "outline.png"},
        "accentColor": p["accent_color"],
        "agentSkills": [{"folder": f"./skills/{os.path.basename(d)}"} for d in skill_dirs],
    }
    write(os.path.join(plug, "manifest.json"), json.dumps(manifest, indent=2))
    problems = gates.lint_plugin(os.path.join(plug, "manifest.json"))
    if problems:
        raise GateFailure("plugin gate: " + "; ".join(f"{r}: {m}" for r, m in problems))
    cowork_zip = zip_dir(plug, os.path.join(t0, f"{pkg}.zip"))

    claude = os.path.join(t0, "claude-plugin", pkg)
    if os.path.exists(os.path.dirname(claude)):
        shutil.rmtree(os.path.dirname(claude))
    os.makedirs(os.path.join(claude, ".claude-plugin"))
    shutil.copytree(os.path.join(plug, "skills"), os.path.join(claude, "skills"))
    write(os.path.join(claude, ".claude-plugin", "plugin.json"), json.dumps({
        "name": pkg, "version": version, "description": p["description_short"],
        "author": {"name": p["developer"]["name"], "url": p["developer"]["website"]},
        "homepage": p["developer"]["website"], "license": "Proprietary"}, indent=2))
    claude_zip = zip_dir(claude, os.path.join(t0, f"{pkg}-claude-format.zip"))
    return manifest, [cowork_zip, claude_zip]


def run_py(args, label):
    log(f"  - {label}")
    env = dict(os.environ, PYTHONDONTWRITEBYTECODE="1")  # never leave __pycache__ in assembled skills
    p = subprocess.run([sys.executable, "-B", *args], capture_output=True, text=True, encoding="utf-8", env=env)
    tail = (p.stdout + p.stderr).strip().splitlines()[-6:]
    if p.returncode != 0:
        raise GateFailure(f"{label} failed:\n    " + "\n    ".join(tail))
    return tail


def main(argv=None):
    ap = argparse.ArgumentParser(description="Assemble, gate and package one practice accelerator.")
    ap.add_argument("--practice", required=True)
    ap.add_argument("--out", default=os.path.join(BUILD, "out"))
    ap.add_argument("--skip-tests", action="store_true")
    ap.add_argument("--regen-corpus", action="store_true")
    a = ap.parse_args(argv)
    practice_dir = os.path.join(ROOT, "practices", a.practice)
    with open(os.path.join(practice_dir, "accelerator.yaml"), encoding="utf-8") as f:
        acc = yaml.safe_load(f)
    out = os.path.join(a.out, a.practice)
    gen, t0 = os.path.join(out, "generated"), os.path.join(out, "t0")
    for d in (gen, t0):
        os.makedirs(d, exist_ok=True)
    results = {"practice": a.practice, "version": acc["version"], "gates": {}}
    try:
        log(f"[1/8] topic catalogue ({a.practice})")
        cat = generate_topic_files(practice_dir, gen)
        topic_sides = {t["id"]: set(t["sides"]) for t in cat["topics"] if t.get("status") != "retired"}
        topic_ids = set(topic_sides)

        log("[2/8] playbooks: validate against the catalogue, render for Word")
        wp_dir = os.path.join(t0, "word-playbooks")
        os.makedirs(wp_dir, exist_ok=True)
        rendered = []
        for pb in acc.get("playbooks", []):
            text = open(os.path.join(ROOT, pb["source"]), encoding="utf-8").read()
            try:
                parsed = playbook_parse.parse(text, topic_sides)
            except playbook_parse.FormatError as ex:
                raise GateFailure(f"{pb['source']}: " + "; ".join(ex.args[0])) from None
            log(f"  - {pb['source']}: {len(parsed['topics'])} topics, silent {parsed['silent_topics']}")
            rendered.append(render_word_playbook.render(parsed, cat, os.path.join(wp_dir, pb["word"])))
        results["gates"]["topic-alignment"] = "ok"

        log("[3/8] eval corpus")
        corpus = os.path.join(practice_dir, "evals", "corpus")
        if a.regen_corpus or not os.path.isdir(corpus) or not os.listdir(corpus):
            run_py([os.path.join(practice_dir, "evals", "build_corpus.py"), corpus], "build_corpus.py")

        log("[4/8] assemble skills")
        skills_out = os.path.join(t0, "skills")
        os.makedirs(skills_out, exist_ok=True)
        skill_dirs = [assemble_skill(s, gen, skills_out) for s in acc["skills"]]

        log("[5/8] lint gates")
        all_problems = []
        for d in skill_dirs:
            probs = gates.lint_skill(d, topic_ids)
            files = sum(len(fn) for _, _, fn in os.walk(d)) - 1
            log(f"  - {os.path.basename(d)}: {files} companion files, {len(probs)} problem(s)")
            all_problems += [f"{os.path.basename(d)}: {r}: {m}" for r, m in probs]
        if all_problems:
            raise GateFailure("lint:\n    " + "\n    ".join(all_problems))
        results["gates"]["lint"] = "ok"

        if not a.skip_tests:
            log("[6/8] tests")
            results["gates"]["engine-tests"] = run_py(
                ["-m", "unittest", "discover", "-s", os.path.join(ENGINES, "tests")], "engine tests")[-1]
            results["gates"]["gate-tests"] = run_py(
                ["-m", "unittest", "discover", "-s", os.path.join(BUILD, "tests")], "gate tests")[-1]
            log("[7/8] local end-to-end")
            e2e = os.path.join(out, "e2e")
            run_py([os.path.join(practice_dir, "evals", "run_local_e2e.py"),
                    os.path.join(skill_dirs[0], "scripts"), e2e], "run_local_e2e.py (shipped scripts)")
            results["gates"]["e2e"] = "ok"
        else:
            log("[6-7/8] tests SKIPPED (--skip-tests)")
            results["gates"]["tests"] = "skipped"

        log("[8/8] package")
        manifest, zips = build_plugins(acc, skill_dirs, t0, topic_ids)
        skill_zips = [zip_dir(d, d + ".zip", arc_prefix=os.path.basename(d) + "/") for d in skill_dirs]
    except GateFailure as ex:
        log(f"\nBUILD FAILED: {ex}")
        return 1

    outputs = zips + skill_zips + rendered
    results.update({
        "built_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "git_commit": subprocess.run(["git", "rev-parse", "--short", "HEAD"], capture_output=True, text=True,
                                     cwd=ROOT).stdout.strip() or None,
        "plugin": {"id": manifest["id"], "version": manifest["version"], "skills": [s["folder"] for s in manifest["agentSkills"]]},
        "topic_catalogue": cat["version"],
        "outputs": {os.path.relpath(o, out).replace("\\", "/"): sha256(o) for o in outputs},
    })
    write(os.path.join(out, "manifest.json"), json.dumps(results, indent=2))
    log("\nBUILD OK")
    for o in outputs:
        log(f"  {os.path.relpath(o, out)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
