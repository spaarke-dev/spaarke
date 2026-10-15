"""Must-fire / must-not-fire controls for the classifier in scripts/master-deploy/check-dev-drift.py.

Builds a throwaway git repo and classifies synthetic "live" bytes against it; nothing touches Dataverse
(`download()` is replaced in the one CLI test). Run: python -m unittest discover -s scripts/master-deploy/tests
"""
import contextlib
import importlib.util
import io
import os
import subprocess
import sys
import tempfile
import unittest

SCRIPT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "check-dev-drift.py")
MIRROR = "src/dataverse/solutions/SpaarkeMaster/WebResources/"


def load():
    spec = importlib.util.spec_from_file_location("check_dev_drift", SCRIPT)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


drift = load()


class Repo:
    def __init__(self, path):
        self.path = path
        self.git("init", "-q", "-b", "master")
        for k, v in (("user.name", "t"), ("user.email", "t@t"), ("core.autocrlf", "false"), ("commit.gpgsign", "false")):
            self.git("config", k, v)

    def git(self, *args):
        r = subprocess.run(["git", "-C", self.path, *args], capture_output=True, text=True)
        if r.returncode:
            raise AssertionError("git %s failed: %s" % (" ".join(args), r.stderr))
        return r.stdout.strip()

    def write(self, rel, text):
        full = os.path.join(self.path, *rel.split("/"))
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "wb") as f:
            f.write(text.encode())

    def commit(self, msg, **files):
        for rel, text in files.items():
            self.write(rel.replace("__", "/"), text)
        self.git("add", "-A")
        self.git("commit", "-q", "-m", msg)


def build_fixture(root):
    """master: stale/current source, a mirror with a stale and a current version, and an older mirror-only file.
    Not in master: an unpushed local branch, a remote-tracking branch, and a branch holding a conflict resolution."""
    r = Repo(root)
    r.commit("base", **{"src__a.js": "a-v1\n", "src__m.js": "base\n", "src__same.js": "same\n",
                        MIRROR.replace("/", "__") + "mirror_cur.js": "mirror-v1\n",
                        MIRROR.replace("/", "__") + "same.js": "same\n"})
    r.git("branch", "topic")
    r.commit("master edits", **{"src__a.js": "a-v2\n", "src__m.js": "master-side\n",
                                MIRROR.replace("/", "__") + "mirror_cur.js": "mirror-v2\n"})
    # topic edits the same file differently -> conflict
    r.git("checkout", "-q", "topic")
    r.commit("topic edit", **{"src__m.js": "topic-side\n"})
    # integ = master + topic, conflict resolved by hand to a blob no ordinary commit holds
    r.git("checkout", "-q", "master")
    r.git("checkout", "-q", "-b", "integ")
    merge = subprocess.run(["git", "-C", root, "merge", "--no-ff", "topic"], capture_output=True, text=True)
    assert merge.returncode != 0, "expected a merge conflict"
    r.write("src/m.js", "resolved-by-hand\n")
    r.git("add", "-A")
    r.git("commit", "-q", "-m", "merge topic")
    # unpushed local branch with a blob that exists nowhere else
    r.git("checkout", "-q", "master")
    r.git("checkout", "-q", "-b", "local-only")
    r.commit("local build", **{"src__local.js": "local-only\n"})
    # remote-tracking branch
    r.git("checkout", "-q", "master")
    r.git("checkout", "-q", "-b", "feat")
    r.commit("remote work", **{"src__remote.js": "remote-only\n"})
    r.git("update-ref", "refs/remotes/origin/feat", "feat")
    r.git("checkout", "-q", "master")
    r.git("branch", "-D", "feat")
    # mirror-only: a file that only ever existed under the mirror (current + an older version)
    r.commit("mirror only v1", **{MIRROR.replace("/", "__") + "only.js": "only-v1\n"})
    r.commit("mirror only v2", **{MIRROR.replace("/", "__") + "only.js": "only-v2\n"})
    return r


class ClassifyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.repo = build_fixture(cls.tmp.name)
        cls.idx = drift.build_index(cls.tmp.name, "master")

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def cls(self, text):
        return drift.classify(text.encode(), self.idx)[0]

    # -- CURRENT / STALE_MASTER / UNKNOWN (unchanged behaviour) ---------------------------------
    def test_current_equals_a_source_file(self):
        self.assertEqual(self.cls("a-v2\n"), "CURRENT")

    def test_current_ignores_crlf_and_bom(self):
        self.assertEqual(drift.classify(b"\xef\xbb\xbfa-v2\r\n", self.idx)[0], "CURRENT")

    def test_stale_master_is_an_older_source_version(self):
        self.assertEqual(self.cls("a-v1\n"), "STALE_MASTER")

    def test_unknown_matches_nothing(self):
        self.assertEqual(self.cls("never committed\n"), "UNKNOWN")

    # -- fix 3: the SpaarkeMaster mirror is not a source ----------------------------------------
    def test_blob_equal_only_to_a_mirror_file_is_mirror_only(self):
        cls, detail = drift.classify(b"only-v2\n", self.idx)
        self.assertEqual(cls, "MIRROR_ONLY")
        self.assertEqual(detail, MIRROR + "only.js")          # names the path

    def test_blob_equal_only_to_an_older_mirror_version_is_mirror_only(self):
        self.assertEqual(self.cls("only-v1\n"), "MIRROR_ONLY")

    def test_blob_equal_to_a_source_file_and_a_mirror_file_is_current(self):
        self.assertEqual(self.cls("same\n"), "CURRENT")        # also at src/same.js, so not mirror-only

    def test_current_and_older_mirror_versions_are_mirror_only(self):
        # mirror_cur.js has no source file at all; its v1 was overwritten by v2 in the mirror
        self.assertEqual(self.cls("mirror-v1\n"), "MIRROR_ONLY")
        self.assertEqual(self.cls("mirror-v2\n"), "MIRROR_ONLY")

    def test_mirror_files_are_not_in_the_current_set(self):
        self.assertNotIn(MIRROR + "only.js", [p for paths in self.idx.tree.values() for p in paths])

    # -- fix 2: unpushed local branches ---------------------------------------------------------
    def test_blob_only_on_an_unpushed_local_branch_is_branch_only(self):
        cls, detail = drift.classify(b"local-only\n", self.idx)
        self.assertEqual(cls, "BRANCH_ONLY")
        self.assertIn("src/local.js", detail)

    def test_blob_only_on_a_remote_branch_is_branch_only(self):
        self.assertEqual(self.cls("remote-only\n"), "BRANCH_ONLY")

    def test_local_branch_blob_is_not_unknown(self):
        self.assertNotEqual(self.cls("local-only\n"), "UNKNOWN")

    # -- fix 4: merge-commit diffs --------------------------------------------------------------
    def test_conflict_resolution_blob_is_seen(self):
        cls, detail = drift.classify(b"resolved-by-hand\n", self.idx)
        self.assertEqual(cls, "BRANCH_ONLY")
        self.assertIn("src/m.js", detail)

    def test_ordinary_blob_keeps_its_own_commit_label_not_the_merge(self):
        # "topic-side" is in an ordinary topic commit; the merge on `integ` must not steal the label
        _, detail = drift.classify(b"topic-side\n", self.idx)
        self.assertEqual(self.repo.git("rev-parse", "topic"), detail.split()[0])

    # -- end to end through main() with download() replaced -------------------------------------
    def run_main(self, live):
        out_dir = tempfile.mkdtemp()

        def fake_download(org, out):
            os.makedirs(os.path.join(out, "live"))
            index = []
            for name, data in live.items():
                with open(os.path.join(out, "live", name), "wb") as f:
                    f.write(data)
                index.append({"name": name, "type": 3, "modifiedon": "2026-10-12T00:00:00Z", "file": name, "size": len(data)})
            return index

        original, argv = drift.download, sys.argv
        drift.download = fake_download
        sys.argv = ["check-dev-drift.py", "--repo", self.tmp.name, "--ref", "master", "--out", os.path.join(out_dir, "o")]
        buf = io.StringIO()
        try:
            with contextlib.redirect_stdout(buf):
                code = drift.main()
        finally:
            drift.download, sys.argv = original, argv
        return code, buf.getvalue()

    def test_main_mirror_only_prints_after_stale_master_and_does_not_stop(self):
        code, out = self.run_main({"sprk_old.js": b"a-v1\n", "sprk_mirror.js": b"only-v2\n", "sprk_ok.js": b"a-v2\n"})
        self.assertEqual(code, 0)
        self.assertLess(out.index("=== STALE_MASTER"), out.index("=== MIRROR_ONLY"))
        self.assertIn("sprk_mirror.js " + MIRROR + "only.js", out)

    def test_main_exits_2_for_an_unpushed_local_branch_build(self):
        code, out = self.run_main({"sprk_local.js": b"local-only\n"})
        self.assertEqual(code, 2)
        self.assertIn("=== BRANCH_ONLY (1)", out)


if __name__ == "__main__":
    unittest.main()
