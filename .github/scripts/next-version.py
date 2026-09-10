#!/usr/bin/env python3
"""Computes the next alpha version.

Alpha is the only auto-versioned channel; stable and beta versions are
given by hand when the release workflow is dispatched, so this script
does not deal with them.

Only alpha builds carry a 4th version component (e.g. 1.5.1.3); stable
and beta use plain 3-component versions. The 4th component is what the
client uses to recognize a build as alpha (ReleaseChannel.FromVersion),
and it counts alpha builds on top of the next patch version after the
highest already-released one. "Released" covers stable (1.5.0) and beta
(1.6.0-beta) tags alike, so an alpha always sorts above anything users
can already be running on another channel.

build.yml invokes this once per day, skipping days with no new commits
on master, so the 4th component advances roughly once per active day.

Reads `git ls-remote --tags` output on stdin and prints the version.
"""
import re
import sys

# Stable tags are bare (1.5.0) and beta tags carry a -beta suffix.
# Legacy betas were published as bare prerelease tags, which is why the
# suffix is optional here.
RELEASE_TAG = re.compile(r'^(\d+\.\d+\.\d+)(?:-beta)?$')
ALPHA_TAG = re.compile(r'^(\d+\.\d+\.\d+)\.(\d+)-alpha$')


def parse_base(s):
    return tuple(int(p) for p in s.split('.'))


def read_tags():
    tags = set()
    for line in sys.stdin:
        parts = line.split()
        if len(parts) != 2 or not parts[1].startswith('refs/tags/'):
            continue
        tag = parts[1][len('refs/tags/'):]
        if not tag.endswith('^{}'):
            tags.add(tag)
    return tags


def next_alpha(tags):
    bases = [parse_base(m.group(1)) for m in map(RELEASE_TAG.match, tags) if m]
    latest_base = max(bases) if bases else (0, 0, 0)
    next_base = (latest_base[0], latest_base[1], latest_base[2] + 1)

    candidates = [(parse_base(m.group(1)), int(m.group(2)))
                  for m in map(ALPHA_TAG.match, tags) if m]
    if candidates:
        base, n = max(candidates)
        if base > latest_base:
            # In-development base ahead of the latest release: keep bumping.
            return f"{base[0]}.{base[1]}.{base[2]}.{n + 1}"
    # No alphas yet, or the in-development version has since been released
    # on another channel: start the next base from scratch.
    return f"{next_base[0]}.{next_base[1]}.{next_base[2]}.1"


def main():
    print(next_alpha(read_tags()))


if __name__ == '__main__':
    main()
