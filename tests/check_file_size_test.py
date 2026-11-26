"""Exercise the line-limit gate against an isolated Git repository."""

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

CHECKER = Path(__file__).resolve().parents[1] / "scripts" / "check-file-size.py"


class FileSizeGateTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.git("init", "--quiet")
        (self.root / "scripts").mkdir()
        self.exceptions({})

    def git(self, *args):
        subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)

    def exceptions(self, entries):
        (self.root / "scripts/file-size-exceptions.json").write_text(json.dumps(entries), encoding="utf-8")

    def check(self):
        return subprocess.run([sys.executable, str(CHECKER)], cwd=self.root, capture_output=True, text=True)

    def test_boundary_including_nonterminated_line(self):
        source = self.root / "new file.txt"
        source.write_bytes(b"\r\n" * 399 + b"last")
        self.assertEqual(self.check().returncode, 0)
        source.write_bytes(b"\r\n" * 400 + b"last")
        result = self.check()
        self.assertEqual(result.returncode, 1)
        self.assertIn("new file.txt: 401 lines exceed the limit of 400", result.stderr)

    def test_tracked_files_are_checked_even_if_later_ignored(self):
        (self.root / "tracked.md").write_bytes(b"\n" * 401)
        self.git("add", "tracked.md")
        (self.root / ".gitignore").write_text("*.md\n", encoding="utf-8")
        (self.root / "ignored.md").write_bytes(b"\n" * 500)
        (self.root / "binary").write_bytes(b"\0" + b"\n" * 500)
        result = self.check()
        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.stderr.strip(), "tracked.md: 401 lines exceed the limit of 400.")

    def test_exception_is_bounded_and_removed_once_unneeded(self):
        source = self.root / "catalog.json"
        self.exceptions({source.name: {"maxLines": 420, "reason": "Keep the catalog together."}})
        source.write_bytes(b"\n" * 420)
        self.assertEqual(self.check().returncode, 0)
        source.write_bytes(b"\n" * 421)
        self.assertIn("421 lines exceed the limit of 420", self.check().stderr)
        source.write_bytes(b"\n" * 400)
        self.assertIn("remove the unused file-size exception", self.check().stderr)
        source.unlink()
        self.assertIn("remove the unused file-size exception", self.check().stderr)

    def test_exception_requires_a_reason(self):
        self.exceptions({"large.cs": {"maxLines": 410, "reason": " "}})
        result = self.check()
        self.assertEqual(result.returncode, 1)
        self.assertIn("maxLines integer above 400 and a reason", result.stderr)


if __name__ == "__main__":
    unittest.main()
