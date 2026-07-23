#!/usr/bin/env python3
"""Validate that a published Blazor WebAssembly asset set is complete."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path


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


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--directory",
        required=True,
        type=Path,
        help="Published wwwroot directory to verify",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    directory = args.directory.resolve()
    errors = verify_local(directory)

    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    framework_file_count = len(collect_framework_files(directory))
    print(f"Verified {framework_file_count} framework files in {directory}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
