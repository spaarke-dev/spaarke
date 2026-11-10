"""gates.py - build-time lint for assembled skill folders and plugins (design §5.5, gate 3).

Each rule has a must-fire and a must-not-fire test in build/tests/test_gates.py.
Build-time only (uses PyYAML); never shipped in a skill.
"""
import ast
import json
import os
import re

import yaml

SPEC_KEYS = {"name", "description", "license", "compatibility", "metadata", "allowed-tools"}
NAME_RE = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
MAX_COMPANIONS, MAX_FILE, MAX_TOTAL = 20, 5 * 1024 * 1024, 10 * 1024 * 1024
MAX_PLUGIN_SKILLS = 20
# Legal for Cowork / Word legal plugin skill names we must not collide with (design §5.7-1)
RESERVED_NAMES = {"review-contract", "edit-contract", "draft-new-contract", "summarize-redlines", "create-playbook",
                  "extract-obligations-and-deadlines", "legal-reasoning", "triage-nda", "legal-research"}
TRADEMARKS = re.compile(r"microsoft|copilot|claude|anthropic|ledes|openai|gpt", re.I)
# home-directory and drive paths; a drive path needs two segments so regexes like "topic:\s*" don't match
HOST_PATHS = re.compile(r"~/\.claude|\.claude/|(?<![A-Za-z0-9])[A-Za-z]:\\{1,2}[\w .-]+\\{1,2}[\w .-]+|/Users/|/home/")
# every practice prefix we use or plan (design §8.3), so another practice's ids are checked too
PRACTICE_PREFIXES = {"nda", "intake", "invoice", "cre", "employment", "patent"}
FILE_EXT = r"docx|docm|dotx|md|json|ya?ml|pdf|html?|txt|py|zip|xlsx?|csv|pptx|png|jpe?g|eml|msg|rtf"
REF_RE = re.compile(r"(?<![\w/-])((?:scripts|references|assets)/[A-Za-z0-9_.\-/]+[A-Za-z0-9_])")
CMD_RE = re.compile(r"python\s+scripts/([A-Za-z0-9_]+\.py)([^\n`]*)")
TEXT_EXT = {".md", ".py", ".json", ".html", ".txt", ".yaml", ".yml"}


def topic_re(topic_ids):
    prefixes = PRACTICE_PREFIXES | {t.split(".")[0] for t in topic_ids}
    return re.compile(r"\b(?:%s)\.(?!(?:%s)\b)[a-z0-9]+(?:-[a-z0-9]+)*\b" % ("|".join(sorted(prefixes)), FILE_EXT))


def read_frontmatter(skill_md):
    text = _read(skill_md)
    m = re.match(r"^---\r?\n(.*?)\r?\n---\r?\n(.*)$", text, re.S)
    if not m:
        return None, text
    return yaml.safe_load(m.group(1)) or {}, m.group(2)


def _read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def _files(root):
    for dp, dn, fn in os.walk(root):
        for d in dn:
            yield os.path.join(dp, d), True
        for f in fn:
            yield os.path.join(dp, f), False


def lint_skill(folder, topic_ids):
    """Return a list of (rule, message) for one assembled skill folder."""
    out = []
    add = lambda rule, msg: out.append((rule, msg))  # noqa: E731
    skill_md = os.path.join(folder, "SKILL.md")
    if not os.path.isfile(skill_md):
        return [("skill-md", "SKILL.md missing")]
    fm, body = read_frontmatter(skill_md)
    if fm is None:
        return [("frontmatter", "SKILL.md has no YAML frontmatter")]

    extra = set(fm) - SPEC_KEYS
    if extra:
        add("frontmatter-keys", f"keys outside the Agent Skills spec: {sorted(extra)}")
    name = str(fm.get("name", ""))
    if not name or not NAME_RE.match(name) or len(name) > 64:
        add("name-format", f"name {name!r} must be lowercase letters, digits and hyphens, <= 64 chars")
    if name != os.path.basename(os.path.normpath(folder)):
        add("name-folder", f"name {name!r} differs from folder {os.path.basename(folder)!r}")
    if name in RESERVED_NAMES:
        add("name-collision", f"name {name!r} collides with a Microsoft legal skill")
    if TRADEMARKS.search(name):
        add("trademark", f"name {name!r} contains an upstream trademark")
    # Cowork's OneDrive folder loader shows block scalars (">-", "|") and quotes literally (L22).
    raw = re.search(r"^description:[ \t]*(.*)$", _read(skill_md), re.M)
    if raw and (not raw.group(1).strip() or raw.group(1).lstrip()[0] in ">|'\""):
        add("description-plain", "description must be one plain line (no >- / | block, no quotes)")
    desc = str(fm.get("description", ""))
    if not desc or len(desc) > 1024:
        add("description-length", f"description is {len(desc)} chars (1..1024)")
    if not re.search(r"\bUse (for|when)\b", desc):
        add("description-triggers", "description has no 'Use for ...' trigger phrases")
    if "Do NOT use for" not in desc:
        add("description-routing", "description has no 'Do NOT use for ...' line")
    if "compatibility" in fm and len(str(fm["compatibility"])) > 500:
        add("compatibility-length", "compatibility exceeds 500 chars")
    md = fm.get("metadata", {})
    if md is not None and (not isinstance(md, dict) or any(isinstance(v, (dict, list)) for v in md.values())):
        add("metadata-flat", "metadata must be a map of flat string values")
    if not fm.get("license"):
        add("license", "license missing (Spaarke content: Proprietary)")

    topic_pat = topic_re(topic_ids)
    md_texts = {"SKILL.md": body}
    companions, total = 0, 0
    for path, is_dir in _files(folder):
        base = os.path.basename(path)
        if base.startswith(".") or base == "__pycache__" or base.endswith(".pyc"):
            add("hidden-files", f"hidden or generated file {os.path.relpath(path, folder)}")
        if is_dir:
            continue
        size = os.path.getsize(path)
        total += size
        if os.path.abspath(path) != os.path.abspath(skill_md):
            companions += 1
        if size > MAX_FILE:
            add("file-size", f"{os.path.relpath(path, folder)} is {size} bytes (> 5 MB)")
        ext = os.path.splitext(path)[1].lower()
        if ext in TEXT_EXT:
            text = _read(path)
            if HOST_PATHS.search(text):
                add("host-paths", f"{os.path.relpath(path, folder)} contains a host-specific path "
                                  f"({HOST_PATHS.search(text).group(0)!r})")
            if ext == ".py":
                continue  # code identifiers like "nda.something" are not topic references
            if ext == ".md" and os.path.abspath(path) != os.path.abspath(skill_md):
                md_texts[os.path.relpath(path, folder)] = text
            for m in topic_pat.finditer(text):
                if m.group(0) not in topic_ids:
                    add("topic-ids", f"{os.path.relpath(path, folder)} uses unknown topic id {m.group(0)}")
    if companions > MAX_COMPANIONS:
        add("file-count", f"{companions} companion files (> {MAX_COMPANIONS})")
    if total > MAX_TOTAL:
        add("total-size", f"skill is {total} bytes (> 10 MB)")

    if not os.path.isfile(os.path.join(folder, "references", "guardrails.md")):
        add("guardrails", "references/guardrails.md missing")
    if "guardrails.md" not in body:
        add("guardrails", "SKILL.md does not point to references/guardrails.md")
    if "not legal advice" not in (body + desc).lower():
        add("disclaimer", "SKILL.md has no 'not legal advice' line")

    for src, text in md_texts.items():
        for ref in sorted(set(REF_RE.findall(text))):
            if "<" in ref or "*" in ref:
                continue
            if not os.path.exists(os.path.join(folder, ref)):
                add("missing-reference", f"{src} refers to {ref}, which is not in the skill")

    scripts = os.path.join(folder, "scripts")
    for script, args in CMD_RE.findall(body):  # SKILL.md commands must match the scripts' real CLIs
        path = os.path.join(scripts, script)
        if not os.path.isfile(path):
            continue  # reported by missing-reference
        src = _read(path)
        for flag in re.findall(r"(?<![\w-])(--[a-z][a-z0-9-]*)", args):
            if f'"{flag}"' not in src:
                add("cli-drift", f"SKILL.md runs scripts/{script} with {flag}, which the script does not accept")
    if os.path.isdir(scripts):
        local = {f[:-3] for f in os.listdir(scripts) if f.endswith(".py")}
        for f in os.listdir(scripts):
            if not f.endswith(".py"):
                continue
            tree = ast.parse(_read(os.path.join(scripts, f)))
            for node in ast.walk(tree):
                mods = []
                if isinstance(node, ast.Import):
                    mods = [a.name.split(".")[0] for a in node.names]
                elif isinstance(node, ast.ImportFrom) and node.module:
                    mods = [node.module.split(".")[0]]
                for mod in mods:
                    if mod in ALLOWED_IMPORTS or mod in local:
                        continue
                    add("script-imports", f"scripts/{f} imports {mod!r}: not stdlib, lxml or a sibling script")
    return out


ALLOWED_IMPORTS = {
    "argparse", "ast", "collections", "copy", "csv", "datetime", "decimal", "importlib", "io", "json", "math",
    "os", "platform", "posixpath", "re", "shutil", "string", "sys", "tempfile", "textwrap", "typing",
    "unicodedata", "zipfile", "lxml",
}


def lint_plugin(manifest_path):
    out = []
    m = json.loads(_read(manifest_path))
    root = os.path.dirname(manifest_path)
    skills = m.get("agentSkills", [])
    if not skills or len(skills) > MAX_PLUGIN_SKILLS:
        out.append(("plugin-skill-count", f"{len(skills)} skills (1..{MAX_PLUGIN_SKILLS})"))
    for s in skills:
        if not os.path.isfile(os.path.join(root, s.get("folder", ""), "SKILL.md")):
            out.append(("plugin-skill-folder", f"{s.get('folder')} has no SKILL.md"))
    for k in ("id", "version", "developer", "name", "description", "icons"):
        if k not in m:
            out.append(("plugin-manifest", f"manifest missing {k!r}"))
    for k in ("color", "outline"):
        if not os.path.isfile(os.path.join(root, m.get("icons", {}).get(k, "-"))):
            out.append(("plugin-icons", f"icon {k} missing"))
    if len(m.get("name", {}).get("short", "")) > 30:
        out.append(("plugin-manifest", "name.short exceeds 30 chars"))
    if len(m.get("description", {}).get("short", "")) > 80:
        out.append(("plugin-manifest", "description.short exceeds 80 chars"))
    if len(m.get("description", {}).get("full", "")) > 4000:
        out.append(("plugin-manifest", "description.full exceeds 4000 chars"))
    return out


# ---------------------------------------------------------------- package gates (build_package.py)
MAX_PACKAGE_PATH = 200       # characters, counted from the zip root's parent ("Documents/..."), OneDrive adds more
MAX_CUSTOM_INSTRUCTION = 1500
KNOWLEDGE_TREE = "Cowork/Playbooks"   # playbooks, review method, Start here
SKILLS_TREE = "Cowork/Skills"
FORBIDDEN_IN_PACKAGE = re.compile(r"answer[- ]?key|corteva|\brpx\b|helios", re.I)


def lint_package(root, prefix, custom_instruction=None, allowed_duplicates=(), scan_text=False):
    """Gate for one package folder (one zip, checked on its own). Returns a list of (rule, message).

    path-length      no file path (prefix + path inside the package) exceeds 200 characters;
    unique-names     no two files under Cowork/Playbooks/ share a file name, and none of them shares a name with a
                     file under Cowork/Skills/ (Word finds files by name, anywhere in OneDrive). Skill folders repeat
                     SKILL.md, guardrails.md and so on among themselves by design. allowed_duplicates = names a
                     package deliberately repeats (a decoy). A package with neither tree is checked on all of
                     its files;
    custom-instruction  the custom instruction text is at most 1,500 characters;
    forbidden        no file name (and, with scan_text, no .docx text) mentions an answer key, Corteva, RPX or Helios;
    unreadable       (scan_text only) every .docx opens as a zip.
    """
    out = []
    files = []
    for dp, _, fn in os.walk(root):
        for f in fn:
            files.append(os.path.relpath(os.path.join(dp, f), root).replace("\\", "/"))
    for rel in sorted(files):
        if len(prefix + rel) > MAX_PACKAGE_PATH:
            out.append(("path-length", f"{len(prefix + rel)} characters (> {MAX_PACKAGE_PATH}): {prefix + rel}"))
        if FORBIDDEN_IN_PACKAGE.search(rel):
            out.append(("forbidden", f"file name {rel!r} mentions material that must not ship"))
    allowed = set(allowed_duplicates)
    knowledge = [f for f in files if f.startswith(KNOWLEDGE_TREE + "/")]
    pool = knowledge if knowledge or any(f.startswith(SKILLS_TREE + "/") for f in files) else files
    names = {}
    for rel in pool:
        names.setdefault(os.path.basename(rel), []).append(rel)
    for name, rels in sorted(names.items()):
        if len(rels) > 1 and name not in allowed:
            out.append(("unique-names", f"{name!r} appears {len(rels)} times: {', '.join(rels)}"))
    skill_names = {os.path.basename(f) for f in files if f.startswith(SKILLS_TREE + "/")}
    for name in sorted({os.path.basename(f) for f in knowledge} & skill_names - allowed):
        out.append(("unique-names", f"{name!r} is in Cowork/Playbooks and also inside Cowork/Skills"))
    if custom_instruction is not None and len(custom_instruction) > MAX_CUSTOM_INSTRUCTION:
        out.append(("custom-instruction", f"{len(custom_instruction)} characters (> {MAX_CUSTOM_INSTRUCTION})"))
    if scan_text:
        import zipfile
        for rel in files:
            if rel.lower().endswith(".docx"):
                try:
                    with zipfile.ZipFile(os.path.join(root, rel)) as z:
                        text = " ".join(re.sub(r"<[^>]+>", "", z.read(n).decode("utf-8", "replace"))
                                        for n in z.namelist() if n.startswith("word/") and n.endswith(".xml"))
                except zipfile.BadZipFile:
                    out.append(("unreadable", f"{rel} is not a readable .docx"))
                    continue
                m = FORBIDDEN_IN_PACKAGE.search(text)
                if m:
                    out.append(("forbidden", f"{rel} mentions {m.group(0)!r}"))
    return out
