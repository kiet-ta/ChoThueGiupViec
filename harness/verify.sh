#!/bin/sh
# Verification levels (fail-fast). Every run writes an EVIDENCE LOG:
#   .harness/evidence/<timestamp>-<level>.log   (command, exit code, tool output with file:line)
# Reports/PRs must cite this log. No log -> the claim does not count.
#
# Usage: sh harness/verify.sh L1|L2|L3
#   L1  format + lint            (fast; whole tree; Backend, Frontend, Mobile)
#   L2  L1 + compile/type-check  (Mobile: flutter build web)
#   L3  L2 + tests + architecture rules + secret scan + frontend build (CI)
# Mobile (Flutter) is mandatory everywhere (owner decision 2026-10-13, MOB-BASE-05): without
# `flutter` on PATH every level fails at the 'mobile toolchain' step.

LEVEL=${1:-L1}
ROOT=$(git rev-parse --show-toplevel) || exit 1
cd "$ROOT" || exit 1
mkdir -p .harness/evidence
LOG=".harness/evidence/$(date +%Y%m%d-%H%M%S)-$LEVEL.log"
echo "# verify $LEVEL @ $(date -u +%FT%TZ) commit=$(git rev-parse --short HEAD 2>/dev/null)" > "$LOG"

step() { # $1=name $2=dir $3...=command
  name=$1; dir=$2; shift 2
  echo "" >> "$LOG"; echo "## STEP: $name  (cwd=$dir)  cmd: $*" >> "$LOG"
  ( cd "$dir" && "$@" ) >> "$LOG" 2>&1
  rc=$?
  echo "## EXIT: $rc" >> "$LOG"
  if [ $rc -ne 0 ]; then
    echo "VERIFY $LEVEL FAILED at step '$name' (exit $rc)" >&2
    echo "  evidence log: $LOG" >&2
    grep -nE 'error|Error|FAIL|✖|×' "$LOG" | tail -n 15 | sed 's/^/  evidence: /' >&2
    exit 1
  fi
  echo "ok   $name"
}

# ---- L1 -------------------------------------------------------------------
step "agent config"   .       sh harness/check-agent-config.sh
step "backend format" Backend dotnet format CommonService.sln whitespace --verify-no-changes
step "frontend lint"  Frontend npx --no-install oxlint
step "mobile toolchain" Mobile sh -c 'command -v flutter >/dev/null 2>&1 || { echo "error: flutter not found on PATH. Install Flutter stable (Dart >= 3.13.3, Mobile/pubspec.yaml) and put its bin/ on PATH."; exit 1; }; flutter --version'
step "mobile pub get"   Mobile flutter pub get
step "mobile format"    Mobile dart format --output=none --set-exit-if-changed lib test
step "mobile analyze"   Mobile flutter analyze --no-fatal-infos

# ---- L2 -------------------------------------------------------------------
if [ "$LEVEL" = "L2" ] || [ "$LEVEL" = "L3" ]; then
  step "backend build"   Backend dotnet build CommonService.sln --nologo -v q
  step "frontend tsc"    Frontend npx --no-install tsc -b
  # web: builds on any host and on the CI runner without an Android/iOS toolchain
  step "mobile build"    Mobile flutter build web
fi

# ---- L3 -------------------------------------------------------------------
if [ "$LEVEL" = "L3" ]; then
  step "backend test"    Backend dotnet test CommonService.sln --no-build --nologo -v q
  step "frontend build"  Frontend npm run build
  step "mobile test"     Mobile flutter test

  # Architecture rules: dependencies point inward
  echo "" >> "$LOG"; echo "## STEP: architecture rules" >> "$LOG"
  ARCH=$( { grep -rnE 'using CommonService\.(Application|Infrastructure|WebAPI|Middleware)' Backend/Domain --include=*.cs;
            grep -rnE 'using CommonService\.(Infrastructure|WebAPI|Middleware)' Backend/Application --include=*.cs; } 2>/dev/null )
  if [ -n "$ARCH" ]; then
    echo "$ARCH" >> "$LOG"
    echo "VERIFY L3 FAILED at step 'architecture rules' (inward-dependency rule broken)" >&2
    echo "$ARCH" | sed 's/^/  evidence: /' >&2; echo "  evidence log: $LOG" >&2; exit 1
  fi
  echo "ok   architecture rules"

  # Secret scan over tracked files
  echo "" >> "$LOG"; echo "## STEP: secret scan" >> "$LOG"
  SECRETS=$(git ls-files -z | xargs -0 grep -nIE '(sk|rk)_live_[A-Za-z0-9]{10,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----|VITE_[A-Z_]*SECRET' -- 2>/dev/null | grep -v '^harness/verify.sh' )
  if [ -n "$SECRETS" ]; then
    echo "$SECRETS" | sed -E 's/(:[0-9]+:).*/\1 <redacted>/' >> "$LOG"
    echo "VERIFY L3 FAILED at step 'secret scan'" >&2
    echo "$SECRETS" | sed -E 's/(:[0-9]+:).*/\1 <redacted>/; s/^/  evidence: /' >&2; echo "  evidence log: $LOG" >&2; exit 1
  fi
  echo "ok   secret scan"
fi

echo "VERIFY $LEVEL PASSED  evidence log: $LOG"
