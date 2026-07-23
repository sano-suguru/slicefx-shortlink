from __future__ import annotations

import subprocess
import sys
import tempfile
import threading
import unittest
from contextlib import contextmanager
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from shutil import copytree
from typing import Iterator


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


def run_verifier(
    publish: Path, *additional_arguments: str
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [
            sys.executable,
            str(VERIFIER),
            "--directory",
            str(publish),
            *additional_arguments,
        ],
        cwd=REPOSITORY_ROOT,
        capture_output=True,
        text=True,
        check=False,
    )


class QuietRequestHandler(SimpleHTTPRequestHandler):
    def log_message(self, format: str, *args: object) -> None:
        pass


@contextmanager
def serve_directory(directory: Path) -> Iterator[str]:
    handler = partial(QuietRequestHandler, directory=str(directory))
    server = ThreadingHTTPServer(("127.0.0.1", 0), handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        host, port = server.server_address
        yield f"http://{host}:{port}"
    finally:
        server.shutdown()
        server.server_close()
        thread.join()


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


class RemoteArtifactVerificationTests(unittest.TestCase):
    def test_accepts_remote_assets_with_matching_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            publish = create_publish_tree(Path(temporary_directory))
            with serve_directory(publish) as base_url:
                result = run_verifier(
                    publish,
                    "--base-url",
                    base_url,
                    "--attempts",
                    "1",
                    "--delay",
                    "0",
                )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Remote deployment matches", result.stdout)

    def test_rejects_remote_html_fallback_for_wasm(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            local_publish = create_publish_tree(root / "local")
            remote_publish = copytree(local_publish, root / "remote")
            remote_corelib = (
                remote_publish / "_framework" / "System.Private.CoreLib.test.wasm"
            )
            remote_corelib.write_text("<!doctype html><title>fallback</title>")

            with serve_directory(remote_publish) as base_url:
                result = run_verifier(
                    local_publish,
                    "--base-url",
                    base_url,
                    "--attempts",
                    "1",
                    "--delay",
                    "0",
                )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("returned HTML", result.stderr)

    def test_rejects_remote_digest_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            local_publish = create_publish_tree(root / "local")
            remote_publish = copytree(local_publish, root / "remote")
            remote_runtime = (
                remote_publish / "_framework" / "dotnet.runtime.test.js"
            )
            remote_runtime.write_text("console.log('different runtime');")

            with serve_directory(remote_publish) as base_url:
                result = run_verifier(
                    local_publish,
                    "--base-url",
                    base_url,
                    "--attempts",
                    "1",
                    "--delay",
                    "0",
                )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("SHA-256 mismatch", result.stderr)


if __name__ == "__main__":
    unittest.main()
