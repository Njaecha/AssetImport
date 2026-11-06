import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("build_identity", ROOT / "scripts/build_identity.py")
identity = importlib.util.module_from_spec(spec)
spec.loader.exec_module(identity)


class IdentityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.old_root = identity.ROOT
        identity.ROOT = self.root
        self.addCleanup(setattr, identity, "ROOT", self.old_root)
        self.git("init", "-q")
        self.git("config", "user.name", "Build test")
        self.git("config", "user.email", "build@example.invalid")
        self.git("config", "commit.gpgsign", "false")
        (self.root / ".gitignore").write_text("*.dll\nprivate/\n")
        self.source = self.root / "source.cs"
        self.source.write_text("committed source\n")
        self.git("add", ".")
        self.git("commit", "-qm", "fixture")
        self.commit = self.git("rev-parse", "HEAD").strip()
        self.dll = self.root / "plugin.dll"
        self.dll.write_bytes(("git." + self.commit).encode() + b"\0")

    def git(self, *args):
        return subprocess.check_output(["git", *args], cwd=self.root, text=True, stderr=subprocess.STDOUT)

    def test_clean_identity_and_committed_source_only(self):
        (self.root / "private").mkdir()
        (self.root / "private/model.pmx").write_text("private fixture")
        result = identity.package_identity(self.dll)
        self.assertEqual(result["buildId"], "g" + self.commit[:12])
        self.assertEqual(dict(identity.source_archive_files())["source.cs"], b"committed source\n")
        self.assertEqual(set(dict(identity.source_archive_files())), {".gitignore", "source.cs"})

    def test_dirty_dll_rejected_even_after_worktree_is_clean(self):
        self.dll.write_bytes(("git." + self.commit + ".dirty").encode() + b"\0")
        with self.assertRaises(SystemExit):
            identity.package_identity(self.dll)

    def test_stale_clean_dll_rejected_after_new_commit(self):
        self.source.write_text("new source\n")
        self.git("add", ".")
        self.git("commit", "-qm", "changed")
        with self.assertRaises(SystemExit):
            identity.package_identity(self.dll)

    def test_modified_and_untracked_sources_rejected(self):
        self.source.write_text("dirty\n")
        with self.assertRaises(SystemExit):
            identity.package_identity(self.dll)
        self.git("restore", "source.cs")
        (self.root / "extra.cs").write_text("untracked\n")
        with self.assertRaises(SystemExit):
            identity.package_identity(self.dll)


if __name__ == "__main__":
    unittest.main()
