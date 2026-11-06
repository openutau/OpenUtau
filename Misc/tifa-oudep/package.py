#!/usr/bin/env python3
"""Build the OpenUtau .oudep packages for the TIFA forced aligner.

The aligner itself is tifa.cpp; its releases already ship self-contained
CLI bundles. This script repacks one of those bundles into OpenUtau's
dependency format:

    tifa-ggml-<platform>-q4.oudep   (flat zip)
      oudep.yaml      package manifest, OpenUtau extracts to Dependencies/<id>
      config.json     platform tag + model path used by OpenUtau.Core
      tifa_ggml_cli   the CLI (tifa_ggml_cli.exe on Windows)
      libggml*.so*    runtime libraries for the platform
      models/tifa.gguf

A CLI bundle also carries weights and tables OpenUtau never reads, so they
are dropped by default:

* breath/AP detector weights - the `breathe` subcommand belongs to
  tifa.cpp's dataset workflow (align -> breathe --merge -> align).
* the English LSTM G2P and the text G2P dictionaries - OpenUtau always
  passes an explicit phone list, so the CLI never builds a G2P pipeline.

Pass --keep-all to package the bundle verbatim.

Usage:
    python package.py --tag v0.1.5 --out dist
    python package.py --tag v0.1.5 --platforms windows-x64 linux-x64

Only the q4 variant is packaged: it is ~2.5x smaller than f16 and the
quantization study in tifa.cpp reports the same onset error.
"""

import argparse
import json
import os
import shutil
import tarfile
import tempfile
import time
import urllib.request
import zipfile

REPO = "KakaruHayate/tifa.cpp"
PACKAGE_ID = "tifa-ggml"
DEFAULT_PLATFORMS = ["windows-x64", "linux-x64", "macos-arm64"]
BINARY = {"windows-x64": "tifa_ggml_cli.exe"}
# The only file in models/ the align path loads.
MODEL_FILE = "tifa.gguf"

OUDEP_YAML = """id: {id}
version: {version}
description: TIFA forced aligner (tifa.cpp {tag}, q4 GGUF, {platform}) used to
  extract phoneme timing from a recording.
entrypoints:
  - loader: Executable
    path: {binary}
"""


def bundle_url(tag: str, platform: str) -> str:
    return f"https://github.com/{REPO}/releases/download/{tag}/tifa-cli-{platform}-q4.tar.gz"


def download(url: str, path: str, attempts: int = 6) -> None:
    if os.path.exists(path) and os.path.getsize(path) > 0:
        print(f"  cached {os.path.basename(path)}")
        return
    print(f"  downloading {url}")
    part = path + ".part"
    for attempt in range(attempts):
        try:
            urllib.request.urlretrieve(url, part)
            os.replace(part, path)
            return
        except Exception as e:  # noqa: BLE001 - network hiccups are expected
            print(f"  attempt {attempt + 1} failed: {e}")
            if os.path.exists(part):
                os.remove(part)
            time.sleep(3)
    raise SystemExit(f"could not download {url}")


def trim_bundle(root: str) -> list:
    """Drop everything under models/ except the aligner weights."""
    models = os.path.join(root, "models")
    if not os.path.isdir(models):
        return []
    removed = []
    for name in sorted(os.listdir(models)):
        if name == MODEL_FILE:
            continue
        path = os.path.join(models, name)
        if os.path.isdir(path):
            shutil.rmtree(path)
        else:
            os.remove(path)
        removed.append(name)
    return removed


def package(tag: str, platform: str, cache: str, workdir: str, outdir: str,
            keep_all: bool = False) -> str:
    binary = BINARY.get(platform, "tifa_ggml_cli")
    # The tag is part of the cache key: reusing a bundle from another
    # release would label the package with the wrong version.
    archive = os.path.join(cache, f"tifa-cli-{platform}-q4-{tag}.tar.gz")
    download(bundle_url(tag, platform), archive)

    extract = os.path.join(workdir, f"extract-{platform}")
    if os.path.exists(extract):
        shutil.rmtree(extract)
    os.makedirs(extract)
    with tarfile.open(archive) as tar:
        # filter="data" rejects absolute paths and ".." members.
        tar.extractall(extract, filter="data")
    roots = [os.path.join(extract, name) for name in os.listdir(extract)]
    root = next((r for r in roots if os.path.isdir(r)), extract)
    if not os.path.exists(os.path.join(root, binary)):
        raise SystemExit(f"{binary} not found in {root}")
    if not os.path.exists(os.path.join(root, "models", MODEL_FILE)):
        raise SystemExit(f"models/{MODEL_FILE} not found in {root}")
    if not keep_all:
        removed = trim_bundle(root)
        print(f"  trimmed {', '.join(removed) if removed else 'nothing'}")

    version = tag.lstrip("v")
    with open(os.path.join(root, "oudep.yaml"), "w", encoding="utf-8", newline="\n") as f:
        f.write(OUDEP_YAML.format(id=PACKAGE_ID, version=version, tag=tag,
                                  platform=platform, binary=binary))
    with open(os.path.join(root, "config.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({
            "platform": platform,
            "model": "models/tifa.gguf",
            "languages": ["zh", "ja", "yue", "en"],
        }, f, indent=2)
        f.write("\n")

    oudep = os.path.join(outdir, f"{PACKAGE_ID}-{platform}-q4.oudep")
    # Flat archive: OpenUtau extracts every entry into Dependencies/<id>.
    with zipfile.ZipFile(oudep, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
        for folder, _, files in os.walk(root):
            for name in files:
                full = os.path.join(folder, name)
                zf.write(full, os.path.relpath(full, root))
    return oudep


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--tag", default="v0.1.3", help="tifa.cpp release tag (default: v0.1.3)")
    parser.add_argument("--platforms", nargs="+", default=DEFAULT_PLATFORMS)
    parser.add_argument("--out", default="dist", help="output directory (default: dist)")
    parser.add_argument("--cache", default="", help="download cache (default: <out>/cache)")
    parser.add_argument("--keep-all", action="store_true",
                        help="package the whole tifa.cpp bundle instead of trimming")
    args = parser.parse_args()

    outdir = os.path.abspath(args.out)
    os.makedirs(outdir, exist_ok=True)
    cache = os.path.abspath(args.cache) if args.cache else os.path.join(outdir, "cache")
    os.makedirs(cache, exist_ok=True)
    workdir = tempfile.mkdtemp(prefix="tifa-oudep-")
    try:
        for platform in args.platforms:
            print(f"{platform}:")
            path = package(args.tag, platform, cache, workdir, outdir, args.keep_all)
            print(f"  {os.path.basename(path)}  {os.path.getsize(path) / 1e6:.1f} MB")
    finally:
        shutil.rmtree(workdir, ignore_errors=True)


if __name__ == "__main__":
    main()
