#!/usr/bin/env python3
# Copyright (c) 2026, The Videre Project Authors. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
"""Verify the release package and its API assembly against the host build."""

from __future__ import annotations

import argparse
import hashlib
from io import BytesIO
import time
import urllib.error
import urllib.request
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import BadZipFile, ZipFile


def identity() -> tuple[str, str]:
    root = Path(__file__).resolve().parents[2]
    version = ET.parse(root / "Directory.Packages.props").findtext(
        ".//TrackerPluginsVersion"
    )
    if not version:
        raise ValueError("TrackerPluginsVersion is missing from Directory.Packages.props")
    package_id = ET.parse(root / "src/plugins/plugins.csproj").findtext(".//PackageId")
    if not package_id:
        raise ValueError("PackageId is missing from src/plugins/plugins.csproj")
    return package_id, version


def verify(package: Path | BytesIO, host_assembly: Path | None) -> bytes:
    expected_id, version = identity()
    with ZipFile(package) as archive:
        manifests = [name for name in archive.namelist() if name.endswith(".nuspec")]
        if len(manifests) != 1:
            raise ValueError("Expected exactly one NuGet package manifest")
        manifest = ET.fromstring(archive.read(manifests[0]))
        package_id = manifest.findtext("{*}metadata/{*}id")
        package_version = manifest.findtext("{*}metadata/{*}version")
        if package_id != expected_id or package_version != version:
            raise ValueError(
                f"Expected {expected_id} {version}; found {package_id} {package_version}"
            )
        assemblies = [
            name for name in archive.namelist()
            if name.startswith("lib/") and name.endswith("/MTGOTracker.Plugins.dll")
        ]
        if len(assemblies) != 1:
            raise ValueError("Expected exactly one MTGOTracker.Plugins runtime assembly")
        api = archive.read(assemblies[0])
    if host_assembly is not None and api != host_assembly.read_bytes():
        raise ValueError(
            "The NuGet API assembly differs from Tracker's host assembly; "
            "pack and publish from the same API build"
        )
    source = str(package) if isinstance(package, Path) else "NuGet.org"
    print(f"Verified {expected_id} {version}: {source}")
    print(f"API assembly SHA-256: {hashlib.sha256(api).hexdigest()}")
    if host_assembly is not None:
        print("The package and host API assemblies are identical.")
    return api


def verify_published(expected_api: bytes, wait_seconds: int = 180) -> None:
    package_id, version = identity()
    name = package_id.lower()
    version = version.lower()
    url = f"https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg"
    deadline = time.monotonic() + wait_seconds
    while True:
        try:
            request = urllib.request.Request(url, headers={"Cache-Control": "no-cache"})
            with urllib.request.urlopen(request, timeout=15) as response:
                published_api = verify(BytesIO(response.read()), None)
            if published_api != expected_api:
                raise ValueError("The published plugin API differs from the CI artifact; use a new package version")
            print("The published package and CI artifact API assemblies are identical.")
            return
        except urllib.error.HTTPError as error:
            if error.code != 404:
                raise
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise ValueError(
                    f"{package_id} {version} is not available on NuGet.org. "
                    "An HTTP 409 upload response does not establish that a duplicate package exists."
                ) from error
            print("Waiting for the uploaded package to become available on NuGet.org…", flush=True)
            time.sleep(min(15, remaining))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path)
    parser.add_argument("--host-assembly", type=Path)
    parser.add_argument("--published", action="store_true", help="Verify the public NuGet package against the CI artifact")
    args = parser.parse_args()
    packages = [args.package] if args.package else sorted(
        Path("packages").glob("*.nupkg")
    )
    if len(packages) != 1:
        parser.exit(1, "Expected exactly one packages/*.nupkg\n")
    try:
        api = verify(packages[0], args.host_assembly)
        if args.published:
            verify_published(api)
    except (OSError, ValueError, BadZipFile, ET.ParseError) as error:
        parser.exit(1, f"Plugin package verification failed: {error}\n")


if __name__ == "__main__":
    main()
