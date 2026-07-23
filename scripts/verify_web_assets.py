#!/usr/bin/env python3
"""Validate that a published Blazor WebAssembly asset set is complete."""

from __future__ import annotations

import argparse
import hashlib
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import quote


WASM_MAGIC = b"\x00asm"
HTML_MARKERS = (b"<!doctype html", b"<html")
REQUIRED_EXACT_FILES = (
    Path("index.html"),
    Path("_framework/blazor.webassembly.js"),
    Path("_framework/dotnet.js"),
)
REQUIRED_FRAMEWORK_PATTERNS = (
    "System.Private.CoreLib.*.wasm",
    "dotnet.runtime.*.js",
    "dotnet.native.*.wasm",
)


def collect_framework_files(directory: Path) -> list[Path]:
    framework = directory / "_framework"
    if not framework.is_dir():
        return []
    return sorted(path for path in framework.iterdir() if path.is_file())


def begins_with_html(data: bytes) -> bool:
    leading_bytes = data.lstrip().lower()
    return any(leading_bytes.startswith(marker) for marker in HTML_MARKERS)


def verify_local(directory: Path) -> list[str]:
    errors: list[str] = []
    framework = directory / "_framework"

    if not directory.is_dir():
        return [f"publish directory does not exist: {directory}"]

    for relative_path in REQUIRED_EXACT_FILES:
        if not (directory / relative_path).is_file():
            errors.append(f"required file is missing: {relative_path}")

    if not framework.is_dir():
        errors.append("required directory is missing: _framework")
        return errors

    for pattern in REQUIRED_FRAMEWORK_PATTERNS:
        if not any(framework.glob(pattern)):
            errors.append(f"required framework asset is missing: {pattern}")

    for path in collect_framework_files(directory):
        leading_bytes = path.read_bytes()[:256]
        relative_path = path.relative_to(directory)
        if path.suffix == ".wasm" and not leading_bytes.startswith(WASM_MAGIC):
            errors.append(f"{relative_path} does not contain WebAssembly bytes")
        if path.suffix == ".js" and begins_with_html(leading_bytes):
            errors.append(f"{relative_path} contains HTML instead of JavaScript")

    return errors


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def verify_remote_once(directory: Path, base_url: str) -> list[str]:
    errors: list[str] = []
    normalized_base_url = base_url.rstrip("/")

    for local_path in collect_framework_files(directory):
        relative_path = local_path.relative_to(directory)
        encoded_path = quote(relative_path.as_posix(), safe="/")
        url = f"{normalized_base_url}/{encoded_path}"
        request = urllib.request.Request(
            url,
            headers={
                "Accept-Encoding": "identity",
                "User-Agent": "slicefx-shortlink-asset-verifier/1.0",
            },
        )

        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                remote_bytes = response.read()
        except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError) as error:
            errors.append(f"{relative_path} request failed: {error}")
            continue

        if local_path.suffix in {".wasm", ".js"} and begins_with_html(remote_bytes[:256]):
            errors.append(f"{relative_path} returned HTML from {url}")
            continue

        local_digest = sha256_bytes(local_path.read_bytes())
        remote_digest = sha256_bytes(remote_bytes)
        if local_digest != remote_digest:
            errors.append(
                f"{relative_path} SHA-256 mismatch: "
                f"local={local_digest} remote={remote_digest}"
            )

    return errors


def verify_remote(
    directory: Path,
    base_url: str,
    attempts: int,
    delay: float,
) -> list[str]:
    errors: list[str] = []
    for attempt in range(1, attempts + 1):
        errors = verify_remote_once(directory, base_url)
        if not errors:
            return []
        if attempt < attempts:
            print(
                f"Remote verification attempt {attempt}/{attempts} failed; "
                f"retrying in {delay:g}s...",
                file=sys.stderr,
            )
            time.sleep(delay)
    return errors


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--directory",
        required=True,
        type=Path,
        help="Published wwwroot directory to verify",
    )
    parser.add_argument(
        "--base-url",
        help="Deployment URL whose _framework assets must match the directory",
    )
    parser.add_argument(
        "--attempts",
        type=int,
        default=1,
        help="Maximum remote verification attempts (default: 1)",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=0,
        help="Seconds between remote verification attempts (default: 0)",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    directory = args.directory.resolve()
    if args.attempts < 1:
        print("ERROR: --attempts must be at least 1", file=sys.stderr)
        return 1
    if args.delay < 0:
        print("ERROR: --delay cannot be negative", file=sys.stderr)
        return 1

    errors = verify_local(directory)

    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    framework_file_count = len(collect_framework_files(directory))
    print(f"Verified {framework_file_count} framework files in {directory}")

    if args.base_url:
        remote_errors = verify_remote(
            directory,
            args.base_url,
            args.attempts,
            args.delay,
        )
        if remote_errors:
            for error in remote_errors:
                print(f"ERROR: {error}", file=sys.stderr)
            return 1
        print(
            f"Remote deployment matches {framework_file_count} framework files "
            f"at {args.base_url.rstrip('/')}"
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
