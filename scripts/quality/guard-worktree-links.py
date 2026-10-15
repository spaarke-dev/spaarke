#!/usr/bin/env python3
"""PreToolUse hook: no link between worktrees, no bare recursive delete of a worktree root.

Owner rule (2026-10-13 incident): agents replaced `<task-worktree>\\...\\node_modules` with an NTFS junction to
ANOTHER worktree's real node_modules to skip `npm install`. A later bulk cleanup (`git worktree remove --force`
plus an `rm -rf` fallback) deleted THROUGH the junction and emptied the other worktree's node_modules and, through
npm's own @spaarke/auth / @spaarke/sdap-client junctions, 70 tracked files. This hook denies the two commands
that make that possible. It applies to the Bash and PowerShell tools (`tool_input.command`).

  A. Creating a link (PowerShell `New-Item -ItemType Junction|SymbolicLink|HardLink` (also assigned, in a block or
     group, with common parameters), `[IO.Directory]::CreateSymbolicLink('L','T')` with literal arguments,
     `mklink /J|/D|/H` (also Git Bash `cmd //c mklink //J`), `ln -s` (also behind `VAR=x`)) whose location is inside one registered worktree and whose target is inside a DIFFERENT one.
     Links inside one worktree, or with either side outside every worktree, are allowed.
  B. A recursive delete (`rm -r`, `Remove-Item -Recurse`, `rmdir|rd|del /s`) or `git worktree remove` whose path
     argument resolves EXACTLY to a registered worktree root (main checkout included) or to a `.claude/worktrees`
     container. Deleting a subfolder inside a worktree is allowed.

The way to remove a worktree is `pwsh scripts/worktree/Remove-WorktreeSafe.ps1 -Path <path>`, which unlinks
junctions first and never recurses through one.

Zero false positives is the requirement: when the command cannot be parsed with confidence (variables, globs,
unknown flags, unresolvable paths) it is ALLOWED. The worktree roots are read straight from the git directory
(`<common>/worktrees/*/gitdir`, a few small files, milliseconds even with hundreds of worktrees) and only once a
candidate command has been recognised; `git worktree list --porcelain` (3 s timeout) is the fallback when the
layout is not the usual one. Fails open: unreadable input or any internal error lets the call through silently;
roots that cannot be determined at all let it through with a one-line notice on stderr. Enforced by: this hook;
controls in scripts/quality/tests/test_guard_worktree_links.py.
"""
import json
import os
import posixpath
import re
import subprocess
import sys

GIT_TIMEOUT_SECONDS = 3
ROOTS_NOTICE = "guard-worktree-links: could not read worktree roots; check skipped"
LINK_TYPES = {"junction", "symboliclink", "hardlink"}
SHELL_WRAPPERS = {"pwsh": "ps", "powershell": "ps", "bash": "bash", "sh": "bash", "zsh": "bash", "cmd": "cmd"}
WRAPPER_FLAGS = {"-c", "-command", "/c", "/k", "-lc", "-ic", "-cl"}
PS_DELETE = {"remove-item", "ri"}
CMD_DELETE = {"rmdir", "rd", "del", "erase"}
NEW_ITEM_PARAMS = ["path", "name", "itemtype", "type", "value", "target", "force", "credential", "whatif", "confirm",
                   "usetransaction", "erroraction", "warningaction", "informationaction", "progressaction",
                   "errorvariable", "warningvariable", "informationvariable", "outvariable", "outbuffer",
                   "pipelinevariable", "verbose", "debug"]
NEW_ITEM_SWITCHES = {"force", "whatif", "confirm", "usetransaction", "verbose", "debug"}
NEW_ITEM_ALIASES = {"ea": "erroraction", "wa": "warningaction", "infa": "informationaction", "ev": "errorvariable",
                    "wv": "warningvariable", "iv": "informationvariable", "ov": "outvariable", "ob": "outbuffer",
                    "pv": "pipelinevariable", "vb": "verbose", "db": "debug", "wi": "whatif", "cf": "confirm"}
LINK_COMMANDS = {"new-item", "ni", "mklink", "ln"}
# Remove-Item parameters: name -> takes a value. Aliases/common parameters included; anything else is "unsure".
DELETE_PARAMS = {"path": True, "literalpath": True, "include": True, "exclude": True, "filter": True,
                 "credential": True, "stream": True, "erroraction": True, "warningaction": True,
                 "informationaction": True, "errorvariable": True, "warningvariable": True,
                 "informationvariable": True, "outvariable": True, "outbuffer": True, "pipelinevariable": True,
                 "recurse": False, "force": False, "whatif": False, "confirm": False, "usetransaction": False,
                 "verbose": False, "debug": False}
DELETE_ALIASES = {"ea": "erroraction", "wa": "warningaction", "infa": "informationaction", "ev": "errorvariable",
                  "wv": "warningvariable", "iv": "informationvariable", "ov": "outvariable", "ob": "outbuffer",
                  "pv": "pipelinevariable", "vb": "verbose", "db": "debug", "lp": "literalpath", "wi": "whatif",
                  "cf": "confirm", "pspath": "literalpath"}


class Unsure(Exception):
    """Raised when part of a command cannot be understood with confidence; the caller allows."""


# --- tokenizer -------------------------------------------------------------------------------------------
HEREDOC = re.compile(r"<<-?\s*(['\"]?)([A-Za-z_][A-Za-z0-9_]*)\1")


PS_HERE_OPEN = re.compile(r"@(['\"])\s*$")


def strip_heredoc_bodies(text, flavor=None):
    """Drop here-document bodies (data, not commands); a body line like `rm -rf C:/wt` in a commit message or a
    script fed to `python -` must not be read as a command. For the PowerShell flavour the same goes for
    `@'...'@` / `@"..."@` here-strings (opener at the end of its line, closer at the start of a line): the body may
    hold an odd number of apostrophes that would otherwise break quote tracking. The opener and closer lines stay,
    so the statement around them keeps its shape; a here-string with no closer is left alone."""
    out, lines, i = [], text.split("\n"), 0
    while i < len(lines):
        line = lines[i]
        out.append(line)
        m = HEREDOC.search(line)
        i += 1
        if m:
            while i < len(lines) and lines[i].strip() != m.group(2):
                i += 1
            i += 1                                   # the terminator
            continue
        h = PS_HERE_OPEN.search(line) if flavor == "ps" else None
        if h:
            closer = h.group(1) + "@"
            j = i
            while j < len(lines) and not lines[j].startswith(closer):
                j += 1
            if j < len(lines):
                i = j                                # the closer line is kept and processed as a normal line
    return "\n".join(out)


def tokenize(text, flavor=None):
    """Split a shell command line into statements of tokens. Quotes group; backslashes are NOT escapes (Windows
    paths); unquoted ; && || | & and newlines separate statements; a `#` at the start of a word is a comment."""
    text = strip_heredoc_bodies(text, flavor)
    statements, cur, buf, have, quote = [], [], [], False, None
    i, n = 0, len(text)

    def flush():
        nonlocal buf, have
        if have:
            cur.append("".join(buf))
        buf, have = [], False

    def end_statement():
        nonlocal cur
        flush()
        if cur:
            statements.append(cur)
        cur = []

    while i < n:
        c = text[i]
        if quote:
            if c == quote:
                if quote == "'" and text[i + 1:i + 2] == "'":   # PowerShell '' inside single quotes
                    buf.append("'")
                    i += 2
                    continue
                quote = None
            else:
                buf.append(c)
            i += 1
            continue
        if c == "#" and not have:
            j = text.find(chr(10), i)
            i = n if j < 0 else j
            continue
        if c in "'\"":
            quote, have = c, True
            i += 1
            continue
        if c in " \t\r":
            flush()
        elif c == "\n" or c == ";":
            end_statement()
        elif c in "&|":
            end_statement()
            if text[i + 1:i + 2] == c:
                i += 1
        else:
            buf.append(c)
            have = True
        i += 1
    end_statement()
    return statements


def command_base(token):
    base = re.split(r"[\\/]", token)[-1].lower()
    for ext in (".exe", ".cmd", ".bat", ".ps1"):
        if base.endswith(ext):
            base = base[:-len(ext)]
    return base


# --- path handling ---------------------------------------------------------------------------------------
def _long_path(p):
    """Nearest existing ancestor expanded (8.3 names on Windows, symlinks elsewhere) + the rest, untouched."""
    p = os.path.abspath(p)
    if os.name == "nt" and "~" not in p:
        return p                                 # no 8.3 short name in it: GetLongPathNameW would change nothing
    rest = []
    probe = p
    while probe and not os.path.exists(probe):
        parent, tail = os.path.split(probe)
        if parent == probe:
            break
        rest.append(tail)
        probe = parent
    try:
        if os.name == "nt":
            import ctypes
            buf = ctypes.create_unicode_buffer(32768)
            got = ctypes.windll.kernel32.GetLongPathNameW(probe, buf, len(buf))
            base = buf.value if 0 < got < len(buf) else probe
        else:
            base = os.path.realpath(probe)
    except Exception:
        base = probe
    return os.path.join(base, *reversed(rest)) if rest else base


class Paths:
    """Canonical keys (lower-case, forward slashes, no trailing slash) for paths in any of the C:\\x, C:/x and
    /c/x spellings."""

    def __init__(self, drive_style):
        self.drive_style = drive_style

    def key(self, raw, cwd):
        if raw is None or raw == "":
            raise Unsure()
        if re.search(r"[*?$%`<>|\"]|^~|^\(|\)$", raw):
            raise Unsure()
        p = raw.replace("\\", "/")
        if p.startswith("//?/"):
            p = p[4:]
        m = re.match(r"^/(?:cygdrive/)?([A-Za-z])(?:/(.*))?$", p) if self.drive_style else None
        if m:
            p = m.group(1) + ":/" + (m.group(2) or "")
        if re.match(r"^[A-Za-z]:$", p):
            raise Unsure()                       # drive-relative
        if not (re.match(r"^[A-Za-z]:/", p) or p.startswith("/")):
            if not cwd:
                raise Unsure()
            p = self.key(cwd, None) + "/" + p
        p = posixpath.normpath(p) if not p.startswith("//") else p
        if self.drive_style:
            p = _long_path(p.replace("/", "\\")).replace("\\", "/") if os.name == "nt" else p
        elif p.startswith("/"):
            p = _long_path(p)
        return p.rstrip("/").lower() if len(p) > 1 else p.lower()


class RootsUnavailable(Exception):
    """The worktree roots could not be determined at all (direct read and `git worktree list` both failed)."""


def _read_text(path):
    with open(path, encoding="utf-8", errors="replace") as fh:
        return fh.read().strip()


def _find_dot_git(start):
    """Nearest `.git` (file or folder) at or above `start`, or None."""
    cur = os.path.abspath(start)
    while True:
        cand = os.path.join(cur, ".git")
        if os.path.exists(cand):
            return cand
        parent = os.path.dirname(cur)
        if parent == cur:
            return None
        cur = parent


def read_worktree_roots(project_dir):
    """Registered worktree roots read straight from the git directory, or None when the layout is not the usual one
    (bare repository, submodule, unreadable or missing files). A `.git` folder is the common dir; a `.git` file in
    a linked worktree says `gitdir: <common>/worktrees/<name>`. The main root is the parent of the common `.git`;
    each linked root is the `gitdir` file in `<common>/worktrees/<name>` (a path to `<root>/.git`). Locked and
    prunable entries are roots too; an entry with no readable `gitdir` is skipped."""
    try:
        dot_git = _find_dot_git(project_dir)
        if dot_git is None:
            return None
        if os.path.isdir(dot_git):
            common = dot_git
        else:
            line = _read_text(dot_git).splitlines()[0]
            if not line.startswith("gitdir:"):
                return None
            git_dir = line[len("gitdir:"):].strip()
            if not os.path.isabs(git_dir):
                git_dir = os.path.join(os.path.dirname(dot_git), git_dir)
            git_dir = os.path.normpath(git_dir)
            common_file = os.path.join(git_dir, "commondir")
            if os.path.isfile(common_file):
                common = os.path.join(git_dir, _read_text(common_file))
            elif os.path.basename(os.path.dirname(git_dir)) == "worktrees":
                common = os.path.dirname(os.path.dirname(git_dir))
            else:
                return None
        common = os.path.normpath(common)
        if os.path.basename(common).lower() != ".git" or not os.path.isdir(common):
            return None                                   # bare repository or odd layout: let git describe it
        roots = [os.path.dirname(common)]
        wt_dir = os.path.join(common, "worktrees")
        for name in (sorted(os.listdir(wt_dir)) if os.path.isdir(wt_dir) else []):
            entry = os.path.join(wt_dir, name)
            try:
                target = _read_text(os.path.join(entry, "gitdir")).splitlines()[0].strip()
            except (OSError, IndexError):
                continue
            if not os.path.isabs(target):
                target = os.path.normpath(os.path.join(entry, target))
            if target.replace("\\", "/").lower().endswith("/.git"):
                target = target[:-len("/.git")]
            roots.append(target)
        return roots
    except Exception:
        return None


def git_worktree_roots(project_dir):
    """Fallback: registered worktree roots as they come from `git worktree list` (main first); None on failure."""
    try:
        out = subprocess.run(["git", "-C", project_dir, "worktree", "list", "--porcelain"], capture_output=True,
                             text=True, timeout=GIT_TIMEOUT_SECONDS)
    except Exception:
        return None
    if out.returncode != 0:
        return None
    return [line[len("worktree "):].strip() for line in out.stdout.splitlines() if line.startswith("worktree ")]


def worktree_roots(project_dir):
    roots = read_worktree_roots(project_dir)
    if not roots:
        roots = git_worktree_roots(project_dir)
    if not roots:
        raise RootsUnavailable()
    return roots


class Context:
    def __init__(self, project_dir):
        self.project_dir = project_dir
        self._roots = None
        self._paths = None

    def load(self):
        if self._roots is None:
            raw = worktree_roots(self.project_dir)
            drive = any(re.match(r"^[A-Za-z]:", r) for r in raw)
            self._paths = Paths(drive)
            self._roots = [self._paths.key(r, None) for r in raw]
            containers = {r + "/.claude/worktrees" for r in self._roots}
            try:
                containers.add(self._paths.key(self.project_dir, None) + "/.claude/worktrees")
            except Unsure:
                pass
            self._containers = containers
        return self

    @property
    def roots(self):
        return self.load()._roots

    @property
    def containers(self):
        return self.load()._containers

    def key(self, raw, cwd):
        return self.load()._paths.key(raw, cwd)

    def owner(self, key):
        best = None
        for r in self.roots:
            if key == r or key.startswith(r + "/"):
                if best is None or len(r) > len(best):
                    best = r
        return best


# --- A: links between worktrees ---------------------------------------------------------------------------
def resolve_new_item_param(name):
    if name in NEW_ITEM_ALIASES:
        return NEW_ITEM_ALIASES[name]
    cands = [p for p in NEW_ITEM_PARAMS if p.startswith(name)]
    if name in NEW_ITEM_PARAMS:
        return name
    if len(cands) == 1:
        return cands[0]
    raise Unsure()


def parse_new_item(args):
    """(link, target) from New-Item arguments, or None when this is not a link creation."""
    values, positional, i = {}, [], 0
    while i < len(args):
        t = args[i]
        if t.startswith("-") and len(t) > 1 and not t[1].isdigit():
            body, inline = t[1:], None
            if ":" in body:
                body, inline = body.split(":", 1)
            name = resolve_new_item_param(body.lower())
            if name in NEW_ITEM_SWITCHES:
                i += 1
                continue
            if inline is None:
                i += 1
                if i >= len(args):
                    raise Unsure()
                inline = args[i]
            values["itemtype" if name == "type" else name] = inline
        else:
            positional.append(t)
        i += 1
    kind = values.get("itemtype", "").strip().lower()
    if kind not in LINK_TYPES:
        return None
    path = values.get("path") or (positional.pop(0) if positional else None)
    target = values.get("value") or values.get("target") or (positional.pop(0) if positional else None)
    if path is None and "name" in values:
        path = "."
    if path is None or target is None or positional:
        raise Unsure()
    if "name" in values:
        path = path.rstrip("\\/") + "/" + values["name"]
    return path, target, "cwd"


def parse_mklink(args):
    flags = [a for a in args if re.fullmatch(r"/[A-Za-z]", a)]
    rest = [a for a in args if not re.fullmatch(r"/[A-Za-z]", a)]
    if not any(f.lower() in ("/j", "/d", "/h") for f in flags) or len(rest) != 2:
        return None
    return rest[0], rest[1], "cwd"


def parse_ln(args):
    symbolic, relative, rest, i = False, False, [], 0
    while i < len(args):
        a = args[i]
        if a == "--":
            rest += args[i + 1:]
            break
        if a == "--symbolic":
            symbolic = True
        elif a in ("--relative",):
            relative = True
        elif a.startswith("--target-directory") or a.startswith("--suffix"):
            raise Unsure()
        elif a.startswith("--"):
            pass
        elif a.startswith("-") and len(a) > 1 and a[1:].isalpha():
            if "s" in a[1:]:
                symbolic = True
            if "r" in a[1:]:
                relative = True
            if "t" in a[1:] or "S" in a[1:]:
                raise Unsure()
        else:
            rest.append(a)
        i += 1
    if not symbolic:
        return None
    base = "cwd" if relative else "link"
    if len(rest) == 1:
        return posixpath.basename(rest[0].replace("\\", "/").rstrip("/")), rest[0], base
    if len(rest) != 2:
        raise Unsure()
    return rest[1], rest[0], base


def check_link(ctx, link, target, base, cwd):
    link_key = ctx.key(link, cwd)
    if base == "link":                           # ln -s: a relative target is relative to the link's folder
        tkey = ctx.key(target, posixpath.dirname(link_key)) if not re.match(r"^([A-Za-z]:|[/\\])", target) \
            else ctx.key(target, cwd)
    else:
        tkey = ctx.key(target, cwd)
    a, b = ctx.owner(link_key), ctx.owner(tkey)
    if a and b and a != b:
        return ("A", link, target)
    return None


# --- B: recursive delete of a worktree root ------------------------------------------------------------------
def resolve_delete_param(body):
    """Canonical Remove-Item parameter name for `body` (prefix/alias allowed); Unsure if unknown or ambiguous."""
    low = body.lower()
    if low in DELETE_ALIASES:
        return DELETE_ALIASES[low]
    if low in DELETE_PARAMS:
        return low
    cands = [p for p in DELETE_PARAMS if p.startswith(low)]
    if low and len(cands) == 1:
        return cands[0]
    raise Unsure()


def delete_paths(base, flavor, args):
    """Path arguments of a recursive delete, or None when the command is not a recursive delete."""
    recursive, paths, i = False, [], 0
    ps_style = flavor == "ps" or base in PS_DELETE
    while i < len(args):
        a = args[i]
        if base == "rm" and not ps_style:
            if a == "--":
                paths += args[i + 1:]
                break
            if a == "--recursive" or re.fullmatch(r"-[A-Za-z]*[rR][A-Za-z]*", a):
                recursive = True
            elif not (a.startswith("-") and len(a) > 1):
                paths.append(a)
        elif a.startswith("-") and len(a) > 1 and not a[1].isdigit():
            body, inline = (a[1:].split(":", 1) + [None])[:2]
            name = resolve_delete_param(body)
            if name == "recurse":
                recursive = True
            elif DELETE_PARAMS[name]:
                if inline is None:
                    i += 1
                    inline = args[i] if i < len(args) else None
                if name in ("path", "literalpath") and inline is not None:
                    paths.append(inline)
        elif base in CMD_DELETE and re.fullmatch(r"/[A-Za-z]", a):
            if a.lower() == "/s":
                recursive = True
        else:
            paths.append(a)
        i += 1
    if not recursive:
        return None
    if ps_style:
        paths = [p for tok in paths for p in tok.split(",")]
    return paths


def git_remove_paths(args):
    """Path arguments of `git [opts] worktree remove [opts] <path>`, with -C applied; None otherwise."""
    i, chdir = 0, None
    while i < len(args) and args[i].startswith("-"):
        if args[i] == "-C" and i + 1 < len(args):
            chdir = args[i + 1]
            i += 2
        elif args[i] == "-c" and i + 1 < len(args):
            i += 2
        else:
            i += 1
    if args[i:i + 2] != ["worktree", "remove"]:
        return None
    rest = [a for a in args[i + 2:] if not a.startswith("-") or a == "-"]
    return rest, chdir


def check_delete(ctx, paths, cwd, what):
    for p in paths:
        try:
            k = ctx.key(p, cwd)
        except Unsure:
            continue
        if k in ctx.roots or k in ctx.containers:
            return ("B", p, what)
    return None


# --- statement shapes the walker must see through -------------------------------------------------------------
ENV_ASSIGN = re.compile(r"^[A-Za-z_]\w*=")
PS_ASSIGN = re.compile(r"^\$\w+(?::\w+)?=(.*)$")
PS_ASSIGN_NAME = re.compile(r"^\$\w+(?::\w+)?$")
OPENERS = re.compile(r"^(?:\[\w+\])?(?:[$@]?[({])+")
BASH_LEAD_WORDS = {"then", "do", "else"}
DOUBLE_SLASH_FLAG = re.compile(r"(?<![\w/\\:.])//([A-Za-z])(?=\s|$)")
DOTNET_SYMLINK = re.compile(
    r"\[\s*(?:System\.)?IO\.(?:Directory|File)\s*\]\s*::\s*CreateSymbolicLink\s*\(\s*"
    r"(?:'([^'$`]*)'|\"([^\"$`]*)\")\s*,\s*(?:'([^'$`]*)'|\"([^\"$`]*)\")\s*\)", re.I)


def strip_assignment(tokens, flavor):
    """Drop a leading `VAR=value` (bash env prefix) / `$var =` (PowerShell assignment) so the real command is first."""
    if flavor == "bash":
        i = 0
        while i < len(tokens) and ENV_ASSIGN.match(tokens[i]):
            i += 1
        tokens = tokens[i:]
        if tokens and tokens[0] in BASH_LEAD_WORDS:
            tokens = tokens[1:]
    elif flavor == "ps" and tokens:
        m = PS_ASSIGN.match(tokens[0])
        if m:
            tokens = ([m.group(1)] if m.group(1) else []) + tokens[1:]
        elif PS_ASSIGN_NAME.match(tokens[0]) and len(tokens) > 1 and tokens[1] == "=":
            tokens = tokens[2:]
    return tokens


def embedded_link_command(tokens, flavor):
    """For a statement that does not start with a link command, the tokens from a link command (or a `cmd /c`
    wrapper around one) that sits inside a block or group (`{ New-Item ... }`, `& { $x = ... }`, `(New-Item ...)`,
    `$(...)`, `[void](...)`), closers trimmed; None otherwise. A bare word later in the statement
    (`echo New-Item ...`) is data, not a command."""
    for i, t in enumerate(tokens):
        m = OPENERS.match(t)
        prev_open = i > 0 and re.search(r"[({]$", tokens[i - 1]) is not None
        if not (m or prev_open):
            continue
        cand = t[m.end():] if m else t
        rest = ([cand] if cand else []) + tokens[i + 1:]
        rest = strip_assignment(rest, flavor)
        if not rest or command_base(rest[0]) not in LINK_COMMANDS | set(SHELL_WRAPPERS):
            continue
        args = []
        for a in rest[1:]:
            if re.fullmatch(r"[})]+", a):
                break
            args.append(a)
        if args:
            args[-1] = re.sub(r"[})]+$", "", args[-1])
        return [rest[0]] + args
    return None


def _is_data(text, pos):
    """True when offset `pos` is inside a quoted string or a `#` comment, judged with the tokenizer's rules: `'`
    and `"` open and close a quote (the other quote character is plain text inside one), `''` inside single quotes
    is an escaped apostrophe, and a `#` at the start of a word outside any quote runs to the end of the line.
    Here-string bodies are already gone by the time this runs."""
    quote, i, word_start = None, 0, True
    while i < pos:
        c = text[i]
        if quote:
            if c == quote:
                if quote == "'" and text[i + 1:i + 2] == "'":
                    i += 2
                    continue
                quote = None
        elif c in "'\"":
            quote, word_start = c, False
        elif c == "#" and word_start:
            j = text.find("\n", i)
            if j < 0 or j >= pos:
                return True
            i, word_start = j, True
            continue
        else:
            word_start = c in " \t\r\n;|&({"
        i += 1
    return quote is not None


def dotnet_symlink_finding(ctx, text, cwd):
    """CreateSymbolicLink('L','T') with two literal-string arguments, as an invocation: a match inside a quoted
    string, commit message, here-string or comment is data. A `-Command "..."` wrapper is analysed on its own as a
    command by the statement walker (the quotes there are the wrapper's, not data)."""
    for m in DOTNET_SYMLINK.finditer(text):
        if _is_data(text, m.start()):
            continue
        link = m.group(1) if m.group(1) is not None else m.group(2)
        target = m.group(3) if m.group(3) is not None else m.group(4)
        try:
            found = check_link(ctx, link, target, "cwd", cwd)
        except Unsure:
            continue
        if found:
            return found
    return None


# --- statement walker ------------------------------------------------------------------------------------
def analyse(command, cwd, ctx, flavor, depth=0):
    """First finding ('A'|'B', ...) in the command line, or None."""
    if depth > 3:
        return None
    if flavor == "ps":
        try:
            found = dotnet_symlink_finding(ctx, strip_heredoc_bodies(command, flavor), cwd)
        except RootsUnavailable:
            raise
        except Exception:
            found = None
        if found:
            return found
    for tokens in tokenize(command, flavor):
        tokens = strip_assignment(tokens, flavor)
        if not tokens:
            continue
        base = command_base(tokens[0])
        if base not in LINK_COMMANDS and base not in SHELL_WRAPPERS:
            embedded = embedded_link_command(tokens, flavor)
            if embedded:
                tokens = embedded
                base = command_base(tokens[0])
        args = tokens[1:]
        try:
            if base in SHELL_WRAPPERS:
                inner_flavor = SHELL_WRAPPERS[base]
                if base == "cmd" and flavor == "bash":      # Git Bash: `cmd //c mklink //J L T` (// stops path mangling)
                    args = [re.sub(r"^//([A-Za-z])$", r"/\1", a) for a in args]
                idx = next((i for i, a in enumerate(args) if a.lower() in WRAPPER_FLAGS), None)
                if idx is not None:
                    inner = args[idx + 1:]
                    text = inner[0] if len(inner) == 1 else " ".join(f'"{a}"' if " " in a else a for a in inner)
                    if base == "cmd" and flavor == "bash":
                        text = DOUBLE_SLASH_FLAG.sub(r"/\1", text)
                    found = analyse(text, cwd, ctx, inner_flavor, depth + 1)
                    if found:
                        return found
                continue
            if base in ("cd", "chdir", "set-location", "sl", "pushd") and args:
                tgt = [a for a in args if not a.startswith("-") and a.lower() != "/d"]
                if tgt:
                    try:
                        ctx.key(tgt[0], cwd)                      # must be resolvable
                        absolute = re.match(r"^([A-Za-z]:|[/\\])", tgt[0])
                        cwd = tgt[0] if absolute else (cwd + "/" + tgt[0] if cwd else None)
                    except Unsure:
                        cwd = None
                continue
            parsed = None
            if base in ("new-item", "ni"):
                parsed = parse_new_item(args)
            elif base == "mklink":
                parsed = parse_mklink(args)
            elif base == "ln":
                parsed = parse_ln(args)
            if parsed:
                found = check_link(ctx, parsed[0], parsed[1], parsed[2], cwd)
                if found:
                    return found
                continue
            if base == "git":
                res = git_remove_paths(args)
                if res:
                    paths, chdir = res
                    wd = cwd
                    if chdir:
                        try:
                            wd = ctx.key(chdir, cwd)
                        except Unsure:
                            continue
                    found = check_delete(ctx, paths, wd, "git worktree remove")
                    if found:
                        return found
                continue
            if base == "rm" or base in PS_DELETE or base in CMD_DELETE:
                paths = delete_paths(base, flavor, args)
                if paths:
                    found = check_delete(ctx, paths, cwd, "a recursive delete")
                    if found:
                        return found
        except Unsure:
            continue
    return None


def reason_for(finding):
    kind, a, b = finding
    if kind == "A":
        return (f"Blocked: this makes '{a}' a link into a DIFFERENT git worktree ('{b}'). On 2026-10-13 a worktree "
                "node_modules junction pointing at another worktree's node_modules was deleted through during "
                "cleanup and emptied that worktree's tracked files. Do not link between worktrees: run "
                "`npm install --legacy-peer-deps --no-audit --no-fund` in this worktree, or build inside the "
                "worktree that owns the node_modules.")
    return (f"Blocked: {b} aimed at '{a}', which is a git worktree root (or the .claude/worktrees folder). A junction "
            "or symlink inside it can be deleted THROUGH (2026-10-13 incident: another worktree's node_modules and "
            "70 tracked files were emptied). Use `pwsh scripts/worktree/Remove-WorktreeSafe.ps1 -Path <path>` "
            "(add `-Force` for a dirty worktree you mean to discard), which unlinks junctions first and never "
            "recurses through one.")


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    try:
        if event.get("tool_name") not in ("Bash", "PowerShell"):
            return 0
        command = (event.get("tool_input") or {}).get("command")
        if not isinstance(command, str) or not command.strip():
            return 0
        cwd = event.get("cwd") or None
        project_dir = os.environ.get("CLAUDE_PROJECT_DIR") or cwd or os.getcwd()
        flavor = "ps" if event.get("tool_name") == "PowerShell" else "bash"
        ctx = Context(project_dir)
        finding = analyse(command, cwd, ctx, flavor)
    except RootsUnavailable:
        print(ROOTS_NOTICE, file=sys.stderr)
        return 0
    except Exception:
        return 0
    if not finding:
        return 0
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                             "permissionDecision": "deny",
                                             "permissionDecisionReason": reason_for(finding)}}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
