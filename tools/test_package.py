import tempfile
from pathlib import Path
import unittest

import package


class package_tests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.folder = self.root / "Assets/Core"
        self.folder.mkdir(parents=True)
        self.folder.with_suffix(".meta").write_text("fileFormatVersion: 2\nguid: " + "a" * 32 + "\nfolderAsset: yes\n")
        self.script = self.folder / "Receiver.cs"
        self.script.write_text("public class Receiver {}\n")
        Path(str(self.script) + ".meta").write_text("fileFormatVersion: 2\nguid: " + "b" * 32 + "\n")

    def test_round_trip_preserves_file_folder_and_guid(self):
        assets = package.collect_assets(self.root, ["Assets/Core"])
        path = self.root / "test.unitypackage"
        package.write_package(path, assets)
        self.assertEqual(assets, package.read_package(path))
        self.assertEqual(assets["Assets/Core/Receiver.cs"]["guid"], "b" * 32)
        self.assertIsNone(assets["Assets/Core"]["asset"])

    def test_identical_source_produces_identical_archive(self):
        assets = package.collect_assets(self.root, ["Assets/Core"])
        first, second = self.root / "first", self.root / "second"
        package.write_package(first, assets)
        package.write_package(second, assets)
        self.assertEqual(first.read_bytes(), second.read_bytes())

    def test_missing_meta_fails(self):
        Path(str(self.script) + ".meta").unlink()
        with self.assertRaises(FileNotFoundError):
            package.collect_assets(self.root, ["Assets/Core"])

    def test_duplicate_guid_fails(self):
        Path(str(self.script) + ".meta").write_text("guid: " + "a" * 32 + "\n")
        with self.assertRaisesRegex(ValueError, "Duplicate GUID"):
            package.collect_assets(self.root, ["Assets/Core"])

    def test_test_scripts_are_rejected(self):
        (self.folder / "Tests").mkdir()
        with self.assertRaisesRegex(ValueError, "Development-only"):
            package.collect_assets(self.root, ["Assets/Core"])

    def test_lfs_pointer_fails(self):
        self.script.write_text("version https://git-lfs.github.com/spec/v1\noid sha256:abc\n")
        with self.assertRaisesRegex(ValueError, "LFS pointer"):
            package.collect_assets(self.root, ["Assets/Core"])

    def test_missing_path_fails(self):
        with self.assertRaisesRegex(ValueError, "Invalid package path"):
            package.collect_assets(self.root, ["Assets/Missing"])


if __name__ == "__main__":
    unittest.main()
