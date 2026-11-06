import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
from verify_packages import verify
from package import write_checksum


class PackageVerificationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.commit = "a" * 40
        self.build_id = "g" + self.commit[:12]
        self.note = ("AssetImport build: " + self.build_id + "\nCommit: " + self.commit + "\n").encode()
        self.stems = [game + "_AssetImport_" + self.build_id + "_Packed" for game in ("KK", "KKS")]
        for stem in self.stems:
            self.write_package(stem, {"BUILD.txt": self.note, "plugin.dll": b"fixture"})
        self.write_package("AssetImport_" + self.build_id + "_source", {"AssetImport/BUILD.txt": self.note}, source=True)

    def write_package(self, stem, files, source=False):
        path = self.root / (stem + ".zip")
        with zipfile.ZipFile(path, "w") as archive:
            for name, content in files.items():
                archive.writestr(name, content)
        write_checksum(path)
        if not source:
            manifest = {"source": {"commit": self.commit, "buildId": self.build_id, "dirty": False},
                        "sha256": {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}}
            path.with_suffix(".manifest.json").write_text(json.dumps(manifest))

    def test_complete_set(self):
        self.assertEqual(len(verify(self.root, self.commit)), 8)

    def test_windows_output_has_unix_compatible_checksum_lines(self):
        for path in self.root.glob("*.zip.sha256"):
            self.assertNotIn(b"\r", path.read_bytes())
            self.assertTrue(path.read_bytes().endswith(b"\n"))

    def test_missing_game_and_unexpected_files(self):
        path = self.root / (self.stems[1] + ".zip")
        path.rename(self.root / "wrong.zip")
        with self.assertRaisesRegex(ValueError, "missing or unexpected"):
            verify(self.root, self.commit)

    def test_corrupt_outer_package(self):
        with (self.root / (self.stems[0] + ".zip")).open("ab") as stream:
            stream.write(b"changed")
        with self.assertRaisesRegex(ValueError, "checksum mismatch"):
            verify(self.root, self.commit)

    def test_inner_content_must_match_manifest_even_when_outer_hash_is_updated(self):
        manifest_path = self.root / (self.stems[0] + ".manifest.json")
        original = manifest_path.read_bytes()
        self.write_package(self.stems[0], {"BUILD.txt": self.note, "plugin.dll": b"changed"})
        manifest_path.write_bytes(original)
        with self.assertRaisesRegex(ValueError, "Manifest checksum mismatch"):
            verify(self.root, self.commit)

    def test_mixed_commit_and_dirty_manifest(self):
        manifest_path = self.root / (self.stems[0] + ".manifest.json")
        for field, value in (("commit", "b" * 40), ("dirty", True)):
            with self.subTest(field=field):
                original = manifest_path.read_text()
                manifest = json.loads(original)
                manifest["source"][field] = value
                manifest_path.write_text(json.dumps(manifest))
                with self.assertRaisesRegex(ValueError, "Manifest identity mismatch"):
                    verify(self.root, self.commit)
                manifest_path.write_text(original)

    def test_private_files_are_rejected_even_when_declared_in_manifest(self):
        self.write_package(self.stems[0], {"BUILD.txt": self.note, "TEST-NOTES.txt": b"private"})
        with self.assertRaisesRegex(ValueError, "Private files"):
            verify(self.root, self.commit)

    def test_stale_source_identity(self):
        self.write_package("AssetImport_" + self.build_id + "_source",
                           {"AssetImport/BUILD.txt": self.note.replace(self.commit.encode(), b"b" * 40)}, source=True)
        with self.assertRaisesRegex(ValueError, "Build identity mismatch"):
            verify(self.root, self.commit)


if __name__ == "__main__":
    unittest.main()
