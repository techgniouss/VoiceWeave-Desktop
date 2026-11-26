#!/usr/bin/env bash
# Downloads every file listed in indic-files.txt into the folder given as $1 and refuses to continue unless each one has exactly
# the pinned size and SHA-256. Run by .github/workflows/mirror-indic.yml; safe to run by hand.
set -euo pipefail

list="$(dirname "$0")/indic-files.txt"
out="${1:?usage: fetch-indic.sh <output-folder>}"
mkdir -p "$out"

while read -r name sha size url; do
  [[ -z "${name:-}" || "$name" == \#* ]] && continue
  echo "fetching $name"
  curl --fail --silent --show-error --location --retry 3 --output "$out/$name" "$url"
  actual_size=$(stat -c %s "$out/$name")
  actual_sha=$(sha256sum "$out/$name" | cut -d' ' -f1)
  if [[ "$actual_size" != "$size" || "$actual_sha" != "$sha" ]]; then
    echo "REFUSED $name: expected $size bytes / $sha, got $actual_size bytes / $actual_sha" >&2
    exit 1
  fi
  echo "  ok $actual_size bytes $actual_sha"
done < "$list"
