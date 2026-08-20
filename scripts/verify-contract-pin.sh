#!/bin/bash
# Fails when contracts/README.md declares a SHA-256 that is not the hash of the
# file beside it.
#
# This is not hypothetical. The cardholder recaptured its snapshot and left the
# old hash in place, so its README described a document it no longer carried,
# and nothing noticed for weeks. The in-repo contract assertions cannot catch
# that: they validate the committed file, so a stale file passes them happily.
set -euo pipefail

contract="contracts/backend.openapi.json"
readme="contracts/README.md"

actual=$(sha256sum "$contract" | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')
declared=$(grep -oE '\b[0-9A-Fa-f]{64}\b' "$readme" | head -1 | tr '[:lower:]' '[:upper:]')

if [ -z "$declared" ]; then
  echo "No SHA-256 found in $readme. The pin must record the hash of the"
  echo "captured document so drift is detectable."
  exit 1
fi

if [ "$actual" != "$declared" ]; then
  echo "Contract pin is stale."
  echo "  $readme declares: $declared"
  echo "  $contract hashes: $actual"
  echo
  echo "Recapture the document and update the recorded commit and hash together,"
  echo "or restore the file. Do not edit the hash to match a file you did not"
  echo "capture: the point of the pin is that it records a real backend commit."
  exit 1
fi

echo "Contract pin matches: $actual"
