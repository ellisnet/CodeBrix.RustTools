#!/usr/bin/env bash
# Validate, ON A MAC, a binary that was cross-linked on Linux by
# CodeBrix.RustTools.Docker. Read-only checks with the Mac's own tools.
#
# usage: ./validate-on-mac.sh <binary> [out/symbols-<triple>.txt]
#
# Checks:
#   1. the file is a Mach-O for the expected architecture
#   2. the only linked library is /usr/lib/libSystem.B.dylib
#   3. an ad-hoc code signature is present (arm64 Macs refuse to run without one)
#   4. the binary launches and exits 0 within 10 seconds
#   5. (optional) every undefined symbol in the binary is in the derived list
#      (symbols-<triple>.txt, exported from the image with the stub)
#
# Nothing from Xcode or the SDK is read or copied. Phase 3 maintainer tooling.

set -uo pipefail

if [ "$#" -lt 1 ]; then
  echo "usage: $0 <binary> [symbols-<triple>.txt]" >&2
  exit 2
fi
bin="$1"
symbols="${2:-}"
fail=0

echo "== 1. file type"
file "$bin"
chmod +x "$bin"

echo "== 2. linked libraries (expect only libSystem.B.dylib)"
otool -L "$bin"
extra="$(otool -L "$bin" | tail -n +2 | awk '{print $1}' | grep -v '^/usr/lib/libSystem.B.dylib$' || true)"
if [ -n "$extra" ]; then echo "UNEXPECTED libraries: $extra"; fail=1; fi

echo "== 3. code signature (expect: Signature=adhoc)"
if codesign -dv --verbose=2 "$bin" 2>&1 | tee /dev/stderr | grep -q "Signature=adhoc"; then
  echo "ad-hoc signature present"
else
  echo "NO ad-hoc signature. Try: rcodesign sign \"$bin\"  (or codesign -s - \"$bin\" here on the Mac, for diagnosis only)"
  fail=1
fi

echo "== 4. launch"
# macOS has no coreutils `timeout`; fall back to a perl alarm.
if command -v timeout >/dev/null 2>&1; then run_limited() { timeout 10 "$@"; }; else run_limited() { perl -e 'alarm 10; exec @ARGV or die "cannot exec: $!"' -- "$@"; }; fi
if run_limited "$bin"; then echo "exit 0"; else echo "LAUNCH FAILED (exit $?)"; fail=1; fi

if [ -n "$symbols" ]; then
  echo "== 5. symbol cross-check"
  nm -u "$bin" | sed 's/^[[:space:]]*//' | sort -u > /tmp/validate-undefined.txt
  sort -u "$symbols" > /tmp/validate-derived.txt
  missing_from_list="$(comm -23 /tmp/validate-undefined.txt /tmp/validate-derived.txt)"
  if [ -n "$missing_from_list" ]; then
    echo "binary imports symbols NOT in the derived list (derivation incomplete):"; echo "$missing_from_list"; fail=1
  else
    echo "every imported symbol is in the derived list"
  fi
fi

echo
if [ "$fail" -eq 0 ]; then echo "VALIDATION PASSED"; else echo "VALIDATION FAILED"; fi
exit "$fail"
