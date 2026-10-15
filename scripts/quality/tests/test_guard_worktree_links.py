"""Must-fire / must-not-fire controls for scripts/quality/guard-worktree-links.py.

Builds a real temp repo with two worktrees so `git worktree list` is real.
Run: python -m unittest discover -s scripts/quality/tests
"""
import json
import os
import re
import subprocess
import sys
import tempfile
import time
import unittest

HOOK = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "guard-worktree-links.py")


def git(cwd, *args):
    subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@example.com", *args], cwd=cwd, check=True,
                   capture_output=True, text=True)


def msys(path):
    """C:\\x\\y -> /c/x/y (Git Bash spelling); unchanged on other platforms."""
    m = re.match(r"^([A-Za-z]):[\\/](.*)$", path)
    return "/%s/%s" % (m.group(1).lower(), m.group(2).replace("\\", "/")) if m else path


def run_full(event, project_dir, raw=None, env_extra=None):
    env = dict(os.environ, CLAUDE_PROJECT_DIR=project_dir)
    env.update(env_extra or {})
    payload = raw if raw is not None else json.dumps(event)
    return subprocess.run([sys.executable, HOOK], input=payload, capture_output=True, text=True, env=env, timeout=20)


def run(event, project_dir, raw=None, env_extra=None):
    out = run_full(event, project_dir, raw, env_extra)
    return out.returncode, out.stdout.strip()


def denied(stdout):
    return bool(stdout) and json.loads(stdout)["hookSpecificOutput"]["permissionDecision"] == "deny"


class GuardWorktreeLinks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        root = os.path.realpath(cls.tmp.name)
        cls.main = os.path.join(root, "repo")
        cls.wta = os.path.join(root, "wtA")
        cls.wtb = os.path.join(root, "wtB")
        cls.outside = os.path.join(root, "elsewhere")
        os.makedirs(cls.main)
        os.makedirs(cls.outside)
        git(cls.main, "init", "-q", "-b", "main")
        with open(os.path.join(cls.main, "f.txt"), "w") as fh:
            fh.write("x")
        git(cls.main, "add", "-A")
        git(cls.main, "commit", "-q", "-m", "init")
        git(cls.main, "worktree", "add", "-q", "-b", "a", cls.wta)
        git(cls.main, "worktree", "add", "-q", "-b", "b", cls.wtb)

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def check(self, tool, command, expect_deny, cwd=None):
        event = {"tool_name": tool, "tool_input": {"command": command}, "cwd": cwd or self.wta}
        code, out = run(event, self.main)
        self.assertEqual(code, 0, command)
        self.assertEqual(denied(out), expect_deny, f"{command!r} -> {out!r}")
        if expect_deny:
            self.assertTrue(json.loads(out)["hookSpecificOutput"]["permissionDecisionReason"])
        else:
            self.assertEqual(out, "", command)

    def ps(self, command, expect_deny, cwd=None):
        self.check("PowerShell", command, expect_deny, cwd)

    def sh(self, command, expect_deny, cwd=None):
        self.check("Bash", command, expect_deny, cwd)

    # --- A: links into another worktree: must fire -----------------------------------------------------------
    def test_new_item_junction_arg_orders_and_quotes(self):
        a, b = self.wta, self.wtb
        link = os.path.join(a, "node_modules")
        tgt = os.path.join(b, "node_modules")
        for cmd in (
            f"New-Item -ItemType Junction -Path '{link}' -Target '{tgt}'",
            f"New-Item -Target {tgt} -Path {link} -ItemType Junction",
            f'New-Item -Path "{link}" -ItemType Junction -Value "{tgt}"',
            f"New-Item -ItemType SymbolicLink -Path {link} -Value {tgt}",
            f"New-Item -ItemType HardLink -Path {link} -Value {tgt}",
            f"New-Item -ItemType Junction -Path {a} -Name node_modules -Target {tgt}",
            f"New-Item {link} -ItemType Junction -Value {tgt}",
            f"ni -Type Junction -Path {link} -Target {tgt} -Force",
            f"cd {a}; New-Item -ItemType Junction -Path node_modules -Target {tgt}",
        ):
            with self.subTest(cmd=cmd):
                self.ps(cmd, True)

    def test_new_item_junction_forward_slash_and_via_bash_wrapper(self):
        link = os.path.join(self.wta, "node_modules").replace("\\", "/")
        tgt = os.path.join(self.wtb, "node_modules").replace("\\", "/")
        self.ps(f"New-Item -ItemType Junction -Path {link} -Target {tgt}", True)
        self.sh(f'powershell -NoProfile -Command "New-Item -ItemType Junction -Path {link} -Target {tgt}"', True)

    def test_mklink(self):
        link = os.path.join(self.wta, "node_modules")
        tgt = os.path.join(self.wtb, "node_modules")
        self.sh(f'cmd /c mklink /J "{link}" "{tgt}"', True)
        self.sh(f'cmd /c "mklink /D {link} {tgt}"', True)
        self.ps(f"cmd /c mklink /J {link} {tgt}", True)
        self.sh(f"mklink /H {link} {tgt}", True)

    def test_ln_s(self):
        link = os.path.join(self.wta, "nm")
        tgt = os.path.join(self.wtb, "node_modules")
        self.sh(f"ln -s {tgt} {link}", True)
        self.sh(f"ln -sf {msys(tgt)} {msys(link)}", True)
        self.sh(f"ln -s ../wtB/node_modules nm", True, cwd=self.wta)

    # --- A: must not fire ------------------------------------------------------------------------------------
    def test_link_inside_one_worktree_allowed(self):
        a = self.wta
        self.ps(f"New-Item -ItemType Junction -Path {a}\\x -Target {a}\\y", False)
        self.sh(f"ln -s {a}/y {a}/x", False)
        self.sh(f"cmd /c mklink /J {a}\\x {a}\\y", False)

    def test_link_to_folder_outside_all_worktrees_allowed(self):
        a, out = self.wta, self.outside
        self.ps(f"New-Item -ItemType Junction -Path {a}\\x -Target {out}", False)
        self.sh(f"ln -s {out} {a}/x", False)
        self.ps(f"New-Item -ItemType Junction -Path {out}\\x -Target {self.wtb}", False)
        self.sh(f"mklink /J {out}\\x {a}", False)

    def test_new_item_that_is_not_a_link_allowed(self):
        self.ps(f"New-Item -ItemType Directory -Path {self.wta}\\x", False)
        self.ps(f"New-Item -ItemType File -Path {self.wta}\\x.txt -Value {self.wtb}\\y", False)
        self.sh(f"ln {self.wtb}/f {self.wta}/f", False)       # hard link without -s is outside the rule

    # --- A: review round 1 (F3): spellings that used to slip through --------------------------------------------
    def _ends(self):
        a, b = self.wta, self.wtb
        return (os.path.join(a, "node_modules"), os.path.join(b, "node_modules"),
                os.path.join(a, "y"), os.path.join(a, "x"))

    def test_new_item_with_common_parameters(self):
        link, tgt, same_t, same_l = self._ends()
        for extra in ("-ErrorAction Stop", "-ea Stop", "-ErrorAction:Stop", "-WarningAction SilentlyContinue",
                      "-wa Ignore", "-Verbose", "-vb", "-Debug", "-ProgressAction SilentlyContinue",
                      "-InformationAction Ignore", "-infa Ignore", "-OutVariable ov", "-ov ov",
                      "-ErrorVariable ev", "-ev ev", "-WhatIf", "-Confirm:$false", "-Force", "-Force:$true",
                      "-WhatIf:$false"):
            with self.subTest(extra=extra):
                self.ps(f"New-Item -Path {link} -ItemType Junction -Value {tgt} {extra}", True)
                self.ps(f"New-Item {extra} -ItemType Junction -Path {link} -Target {tgt}", True)
                self.ps(f"New-Item -Path {same_l} -ItemType Junction -Value {same_t} {extra}", False)

    def test_cmd_c_with_double_slash_flags_in_git_bash(self):
        link, tgt, same_t, same_l = self._ends()
        self.sh(f"cmd //c mklink //J {link} {tgt}", True)
        self.sh(f'cmd //c mklink //J "{link}" "{tgt}"', True)
        self.sh(f"cmd //C mklink //D {msys(link)} {msys(tgt)}", True)
        self.sh(f'cmd.exe //c "mklink //J {link} {tgt}"', True)
        self.sh(f"cmd //c mklink //J {same_l} {same_t}", False)
        self.sh(f"cmd //c mklink //J {self.outside} {tgt}", False)

    def test_leading_environment_assignment_in_bash(self):
        link, tgt, same_t, same_l = self._ends()
        self.sh(f"MSYS_NO_PATHCONV=1 cmd /c mklink /J {link} {tgt}", True)
        self.sh(f"MSYS=winsymlinks:nativestrict ln -s {tgt} {link}", True)
        self.sh(f"A=1 B=two MSYS=winsymlinks:nativestrict ln -s {tgt} {link}", True)
        self.sh(f"MSYS_NO_PATHCONV=1 cmd //c mklink //J {link} {tgt}", True)
        self.sh(f"MSYS_NO_PATHCONV=1 rm -rf {self.wtb}", True)
        self.sh(f"MSYS_NO_PATHCONV=1 cmd /c mklink /J {same_l} {same_t}", False)
        self.sh(f"MSYS=winsymlinks:nativestrict ln -s {same_t} {same_l}", False)
        self.sh("FOO=bar", False)

    def test_powershell_assignment_of_new_item(self):
        link, tgt, same_t, same_l = self._ends()
        for cmd in (f"$null = New-Item -ItemType Junction -Path {link} -Target {tgt}",
                    f"$x = New-Item -ItemType Junction -Path {link} -Target {tgt}",
                    f"$x=New-Item -ItemType Junction -Path {link} -Target {tgt}",
                    f"$null=New-Item -ItemType Junction -Path {link} -Target {tgt}",
                    f"$x = (New-Item -ItemType Junction -Path {link} -Target {tgt})",
                    f"[void](New-Item -ItemType Junction -Path {link} -Target {tgt})",
                    f"$null = New-Item -ItemType Junction -Path {link} -Target {tgt} | Out-Null"):
            with self.subTest(cmd=cmd):
                self.ps(cmd, True)
        self.ps(f"$null = New-Item -ItemType Junction -Path {same_l} -Target {same_t}", False)
        self.ps(f"$x = New-Item -ItemType Junction -Path {same_l} -Target {same_t}", False)

    def test_new_item_and_mklink_inside_a_block(self):
        link, tgt, same_t, same_l = self._ends()
        deny = (
            f"if (-not (Test-Path {link})) {{ New-Item -ItemType Junction -Path {link} -Target {tgt} }}",
            f"if (-not (Test-Path {link})) {{ New-Item -ItemType Junction -Path {link} -Target {tgt}}}",
            f"& {{ New-Item -ItemType Junction -Path {link} -Target {tgt} }}",
            f"& {{ $null = New-Item -ItemType Junction -Path {link} -Target {tgt} }}",
            f"foreach ($i in 1..2) {{ ni -ItemType Junction -Path {link} -Target {tgt} -Force }}",
            f"try {{ (New-Item -ItemType Junction -Path {link} -Target {tgt}) }} catch {{ }}",
            f"$r = $(New-Item -ItemType Junction -Path {link} -Target {tgt})",
            f"if ($true) {{ cmd /c mklink /J {link} {tgt} }}",
            f"& {{ mklink /J {link} {tgt} }}",
        )
        for cmd in deny:
            with self.subTest(cmd=cmd):
                self.ps(cmd, True)
        for cmd in (
            f"if (-not (Test-Path {same_l})) {{ New-Item -ItemType Junction -Path {same_l} -Target {same_t} }}",
            f"& {{ New-Item -ItemType Junction -Path {same_l} -Target {same_t} }}",
            f"if ($true) {{ Write-Host New-Item -ItemType Junction -Path {link} -Target {tgt} }}",
            f"Write-Host (New-Item -ItemType Directory -Path {link})",
            f"if ($true) {{ New-Item -ItemType Junction -Path {self.outside} -Target {tgt} }}",
        ):
            with self.subTest(cmd=cmd):
                self.ps(cmd, False)
        self.sh(f"if [ ! -e {link} ]; then ln -s {tgt} {link}; fi", True)
        self.sh(f"{{ ln -s {tgt} {link}; }}", True)
        self.sh(f"if [ ! -e {same_l} ]; then ln -s {same_t} {same_l}; fi", False)

    def test_create_symbolic_link_dotnet_call(self):
        link, tgt, same_t, same_l = self._ends()
        for cls in ("[IO.Directory]", "[System.IO.Directory]", "[System.IO.File]", "[io.directory]"):
            with self.subTest(cls=cls):
                self.ps(f"{cls}::CreateSymbolicLink('{link}', '{tgt}')", True)
                self.ps(f'{cls}::CreateSymbolicLink("{link}","{tgt}")', True)
                self.ps(f"$null = {cls}::CreateSymbolicLink('{link}','{tgt}')", True)
                self.ps(f"{cls}::CreateSymbolicLink('{same_l}', '{same_t}')", False)
                self.ps(f"{cls}::CreateSymbolicLink('{self.outside}', '{tgt}')", False)
        self.ps(f"[IO.Directory]::CreateSymbolicLink($l, '{tgt}')", False)         # non-literal: unsure, allowed
        self.ps(f'[IO.Directory]::CreateSymbolicLink("{link}/$n", "{tgt}")', False)
        self.sh(f"powershell -NoProfile -Command \"[IO.Directory]::CreateSymbolicLink('{link}','{tgt}')\"", True)
        self.ps(f"[IO.Directory]::Exists('{link}')", False)
        self.ps("[IO.Directory]::CreateSymbolicLink('relative-link', 'relative-target')", False)

    def test_create_symbolic_link_text_inside_quotes_is_data(self):
        """Re-check F1: the call text inside a string, commit message or here-string is not an invocation."""
        link, tgt, _, _ = self._ends()
        call = f"[IO.Directory]::CreateSymbolicLink('{link}','{tgt}')"
        nl = chr(10)
        for cmd in (
            f'Write-Host "{call}"',
            f'git commit -m "docs: {call}"',
            nl.join(['git commit -m @"', f"docs: {call}", '"@']),
            nl.join(["$h = @'", call, "'@"]),
            f'Add-Content -Path docs/x.md -Value "{call}"',
            "Write-Host '" + call.replace("'", "''") + "'",
            f"# {call}",
            nl.join(["# it's data", f"Write-Host ok  # {call}"]),
        ):
            with self.subTest(cmd=cmd):
                self.ps(cmd, False)
        for cmd in (                                   # real invocations still fire, whatever came before
            call,
            f"$null = {call}",
            f"& {{ {call} }}",
            f"if ($true) {{ {call} }}",
            f"Write-Host 'hi'; {call}",
            "Write-Host \"it's\"; " + call,
            nl.join(["# it's a comment", call]),
            nl.join(["$h = @'", "it's", "'@", call]),
        ):
            with self.subTest(cmd=cmd):
                self.ps(cmd, True)
        self.sh(f'powershell -NoProfile -Command "{call}"', True)
        self.ps(f'pwsh -Command "{call}"', True)
        self.ps(f'pwsh -NoProfile -c "{call}"', True)
        self.sh(f'echo "{call}"', False)

    def test_powershell_here_string_with_odd_apostrophes(self):
        """Re-check K3: an unbalanced apostrophe in a here-string body must not break quote tracking."""
        link, tgt, _, _ = self._ends()
        nl = chr(10)
        new_item = f"New-Item -ItemType Junction -Path {link} -Target {tgt}"
        body = [nl.join(["$body = @'", "it's", new_item, "'@", "gh pr create --body $body"]),
                nl.join(['$body = @"', "it's", new_item, '"@', "gh pr create --body $body"]),
                nl.join(["git commit -m @'", "don't", f"rm -rf {self.wtb}", "'@"])]
        for cmd in body:
            with self.subTest(cmd=cmd):
                self.ps(cmd, False)
        for cmd in (nl.join(["$body = @'", "it's", "'@", new_item]),
                    nl.join(["$body = @'", "it's", "'@", "gh pr create --body $body", new_item]),
                    nl.join(["$body = @'", "x", "'@", f"Remove-Item -Recurse -Force {self.wtb}"])):
            with self.subTest(cmd=cmd):
                self.ps(cmd, True)

    def test_operators_constant_removed(self):
        with open(HOOK, encoding="utf-8") as fh:
            self.assertNotIn("OPERATORS", fh.read())

    # --- B: recursive delete / git worktree remove of a root: must fire --------------------------------------
    def test_rm_rf_root_in_all_spellings(self):
        b = self.wtb
        for cmd in (
            f"rm -rf {b}",
            f"rm -rf {b.replace(chr(92), '/')}",
            f"rm -rf {msys(b)}",
            f"rm -rf {b}/",
            f'rm -rf "{b}"',
            f"rm -fr {b}",
            f"rm -R {b}",
            f"rm --recursive --force {b}",
            f"rm -r -f -- {b}",
            f"cd /tmp && rm -rf {b}",
            f"rm -rf {b}/../wtB",
            f"rm -rf {self.main}",
        ):
            with self.subTest(cmd=cmd):
                self.sh(cmd, True)
        self.sh("rm -rf ../wtB", True, cwd=self.wta)

    def test_remove_item_recurse(self):
        b = self.wtb
        for cmd in (
            f"Remove-Item -Recurse -Force {b}",
            f"Remove-Item -Force -Recurse -LiteralPath '{b}'",
            f"Remove-Item -Path {b} -r",
            f"ri {b} -Recurse",
            f"rm -Recurse -Force {b}",
            f"Remove-Item {self.wta}, {b} -Recurse -ErrorAction SilentlyContinue",
            f"Remove-Item -Recurse -Force -ea SilentlyContinue {b}",
            f"rmdir /s /q {b}",
        ):
            with self.subTest(cmd=cmd):
                self.ps(cmd, True)
        self.sh(f"cmd /c rd /s /q {b}", True)
        self.sh(f'cmd /c "rmdir /s /q {b}"', True)
        self.sh(f'pwsh -c "Remove-Item -Recurse -Force {b}"', True)

    def test_git_worktree_remove(self):
        b = self.wtb
        self.sh(f"git worktree remove --force {b}", True)
        self.sh(f"git worktree remove {b}", True)
        self.sh(f"git -C {self.main} worktree remove -f -f {msys(b)}", True)
        self.ps(f"git worktree remove --force {b}", True)
        self.sh("git worktree remove ../wtB", True, cwd=self.wta)

    def test_claude_worktrees_container(self):
        c = os.path.join(self.main, ".claude", "worktrees")
        self.sh(f"rm -rf {c}", True)
        self.sh(f"rm -rf {msys(c)}", True)
        self.ps(f"Remove-Item -Recurse -Force {c}", True)

    # --- B: must not fire ------------------------------------------------------------------------------------
    def test_delete_inside_a_worktree_allowed(self):
        b = self.wtb
        self.sh(f"rm -rf {b}/publish", False)
        self.sh(f"rm -rf {msys(b)}/publish", False)
        self.sh(f"rm -rf {b}/node_modules", False)
        self.ps(f"Remove-Item -Recurse -Force {b}\\publish", False)
        self.ps(f"Remove-Item {b}/file.txt", False)
        self.ps(f"Remove-Item -Force {b}", False)                # not recursive
        self.sh(f"rm {b}/file.txt", False)
        self.sh(f"rm -f {b}", False)                              # not recursive
        self.sh(f"rm -rf {self.outside}", False)
        self.sh(f"rm -rf {os.path.join(self.main, '.claude', 'worktrees', 'other')}", False)
        self.sh("rm -rf publish", False, cwd=self.wtb)
        self.sh(f"git worktree remove {b}/sub", False)

    def test_safe_script_and_read_only_commands_allowed(self):
        b = self.wtb
        self.sh(f"pwsh scripts/worktree/Remove-WorktreeSafe.ps1 -Path {b}", False)
        self.sh(f"pwsh -File scripts/worktree/Remove-WorktreeSafe.ps1 -Path {b} -Force", False)
        self.ps(f"pwsh scripts/worktree/Remove-WorktreeSafe.ps1 -Path {b} -WhatIf", False)
        self.sh("git worktree list", False)
        self.sh("git worktree list --porcelain", False)
        self.sh(f"git -C {b} status --porcelain", False)
        self.sh(f"ls {b}", False)
        self.sh(f"echo rm -rf {b}", False)

    def test_unrelated_and_unsure_commands_allowed(self):
        for cmd in ("npm install --legacy-peer-deps --no-audit --no-fund", "dotnet build", "echo hi", "ln", "rm",
                    "rm -rf $TARGET", "rm -rf ~/x", "rm -rf *", "New-Item -ItemType Junction",
                    "New-Item -ItemType Junction -Path $p -Target $t",
                    "Remove-Item -Recurse -Force $wt", "git worktree remove", "mklink /J a",
                    "ln -s a b c", "rm -rf /c/definitely/not/a/worktree"):
            with self.subTest(cmd=cmd):
                self.sh(cmd, False)
                self.ps(cmd, False)

    def test_data_that_mentions_a_command_is_not_a_command(self):
        b = self.wtb
        nl = chr(10)
        self.sh(nl.join(["git commit -F - <<'EOF'", "fix: never", f"rm -rf {b}", "EOF"]), False)
        self.sh(f'git commit -m "do not rm -rf {b}"', False)
        self.sh(nl.join([f"# rm -rf {b}", "ls"]), False)
        self.sh(nl.join(["python - <<EOF", f"rm -rf {b}", "EOF", "ls"]), False)
        self.sh(nl.join(["cat <<'EOF'", "x", "EOF", f"rm -rf {b}"]), True)   # the command AFTER the body still counts

    def test_non_shell_tool_ignored(self):
        event = {"tool_name": "Edit", "tool_input": {"command": f"rm -rf {self.wtb}"}}
        code, out = run(event, self.main)
        self.assertEqual((code, out), (0, ""))
        event = {"tool_name": "Write", "tool_input": {"file_path": "x", "content": f"rm -rf {self.wtb}"}}
        self.assertEqual(run(event, self.main), (0, ""))

    def test_malformed_input_fails_open(self):
        for raw in ("", "not json", "{", "[]", "null", '{"tool_name": "Bash"}', '{"tool_name": "Bash", "tool_input": 5}',
                    '{"tool_name": "Bash", "tool_input": {"command": 7}}'):
            with self.subTest(raw=raw):
                self.assertEqual(run(None, self.main, raw=raw), (0, ""))

    def test_not_a_git_repo_fails_open(self):
        with tempfile.TemporaryDirectory() as plain:
            event = {"tool_name": "Bash", "tool_input": {"command": f"rm -rf {self.wtb}"}, "cwd": plain}
            self.assertEqual(run(event, plain), (0, ""))

    # --- worktree roots are read from .git directly, not from `git worktree list` (re-check K2) -------------------
    NOTICE = "guard-worktree-links: could not read worktree roots; check skipped"

    def _no_git_env(self):
        empty = tempfile.mkdtemp(dir=self.tmp.name)           # a PATH with no git in it
        return {"PATH": empty}

    def test_roots_read_directly_without_git_on_path(self):
        env = self._no_git_env()
        for project in (self.main, self.wta):                    # from the main checkout and from a linked worktree
            for tool, cmd, deny in (("Bash", f"rm -rf {self.wtb}", True),
                                    ("Bash", f"rm -rf {self.main}", True),
                                    ("Bash", f"rm -rf {self.wtb}/publish", False),
                                    ("PowerShell", f"New-Item -ItemType Junction -Path {self.wta}/nm -Target {self.wtb}/node_modules",
                                     True),
                                    ("PowerShell", f"New-Item -ItemType Junction -Path {self.wta}/nm -Target {self.outside}",
                                     False)):
                with self.subTest(project=project, cmd=cmd):
                    event = {"tool_name": tool, "tool_input": {"command": cmd}, "cwd": self.wta}
                    out = run_full(event, project, env_extra=env)
                    self.assertEqual(out.returncode, 0)
                    self.assertEqual(denied(out.stdout.strip()), deny, out.stdout + out.stderr)
                    self.assertNotIn(self.NOTICE, out.stderr)

    def test_unreadable_common_dir_prints_notice_and_fails_open(self):
        env = self._no_git_env()
        with tempfile.TemporaryDirectory() as broken:
            with open(os.path.join(broken, ".git"), "w") as fh:
                fh.write("gitdir: " + os.path.join(broken, "nowhere", "worktrees", "x") + chr(10))
            event = {"tool_name": "Bash", "tool_input": {"command": f"rm -rf {self.wtb}"}, "cwd": broken}
            for label, extra in (("no git on PATH", env), ("git on PATH, which also fails here", {})):
                with self.subTest(label):
                    out = run_full(event, broken, env_extra=extra)
                    self.assertEqual((out.returncode, out.stdout.strip()), (0, ""))
                    self.assertEqual(out.stderr.strip(), self.NOTICE)
        with tempfile.TemporaryDirectory() as plain:             # not a repository at all
            out = run_full({**event, "cwd": plain}, plain)
            self.assertEqual((out.returncode, out.stdout.strip(), out.stderr.strip()), (0, "", self.NOTICE))

    def test_notice_only_when_a_candidate_command_needs_the_roots(self):
        with tempfile.TemporaryDirectory() as plain:
            out = run_full({"tool_name": "Bash", "tool_input": {"command": "ls -la"}, "cwd": plain}, plain)
            self.assertEqual((out.returncode, out.stdout.strip(), out.stderr.strip()), (0, "", ""))

    def test_locked_worktree_is_still_a_root(self):
        git(self.main, "worktree", "lock", self.wtb)
        try:
            self.sh(f"rm -rf {self.wtb}", True)
        finally:
            git(self.main, "worktree", "unlock", self.wtb)

    def test_latency_under_five_seconds(self):
        event = {"tool_name": "Bash", "tool_input": {"command": f"rm -rf {self.wtb}"}, "cwd": self.wta}
        start = time.time()
        run(event, self.main)
        self.assertLess(time.time() - start, 5)


if __name__ == "__main__":
    unittest.main()
