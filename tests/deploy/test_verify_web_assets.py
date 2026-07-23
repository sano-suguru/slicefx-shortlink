from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VERIFIER = REPOSITORY_ROOT / "scripts" / "verify_web_assets.py"


def create_publish_tree(root: Path) -> Path:
    publish = root / "wwwroot"
    framework = publish / "_framework"
    framework.mkdir(parents=True)

    (publish / "index.html").write_text("<!doctype html><title>test</title>")
    (framework / "blazor.webassembly.js").write_text("console.log('blazor');")
    (framework / "dotnet.js").write_text("console.log('dotnet');")
    (framework / "dotnet.runtime.test.js").write_text("console.log('runtime');")
    (framework / "dotnet.native.test.wasm").write_bytes(b"\x00asmnative")
    (framework / "System.Private.CoreLib.test.wasm").write_bytes(b"\x00asmcorelib")

    return publish


def run_verifier(publish: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(VERIFIER), "--directory", str(publish)],
        cwd=REPOSITORY_ROOT,
        capture_output=True,
        text=True,
        check=False,
    )


class LocalArtifactVerificationTests(unittest.TestCase):
    def test_accepts_complete_local_publish(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            publish = create_publish_tree(Path(temporary_directory))

            result = run_verifier(publish)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Verified 5 framework files", result.stdout)

    def test_rejects_html_returned_as_wasm(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            publish = create_publish_tree(Path(temporary_directory))
            corelib = publish / "_framework" / "System.Private.CoreLib.test.wasm"
            corelib.write_text("<!doctype html><title>fallback</title>")

            result = run_verifier(publish)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("does not contain WebAssembly bytes", result.stderr)

    def test_rejects_missing_runtime_asset(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            publish = create_publish_tree(Path(temporary_directory))
            runtime = publish / "_framework" / "dotnet.runtime.test.js"
            runtime.unlink()

            result = run_verifier(publish)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("dotnet.runtime.*.js", result.stderr)


if __name__ == "__main__":
    unittest.main()
