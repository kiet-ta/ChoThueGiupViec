#!/bin/sh
# Hard scope guard. Every changed file must be inside the allowed paths of the ticket
# (GitHub Issue #N mirrored in .spec/tasks.md). Violations FAIL with evidence (file + rule).
#
# Usage:
#   sh harness/scope-check.sh              # staged files (pre-commit); during a merge of origin/main:
#                                          #   staged result vs MERGE_HEAD (the ticket's own changes)
#   sh harness/scope-check.sh --ci <base>  # files changed vs <base> (CI), e.g. origin/main
# Branch must be named: ticket/<issue-number>-<slug>   (override in CI: HARNESS_BRANCH)
#
# Need a path that is not allowed? Do NOT bypass: open a GitHub Issue (template "Scope exception").

set -f  # no shell globbing: patterns must stay literal
ROOT=$(git rev-parse --show-toplevel) || exit 1
cd "$ROOT" || exit 1
TASKS=".spec/tasks.md"
PROTECTED="harness/protected-paths.txt"

fail() { echo "SCOPE-CHECK FAILED: $1" >&2; shift; for l in "$@"; do echo "  evidence: $l" >&2; done; exit 1; }

BRANCH=${HARNESS_BRANCH:-$(git rev-parse --abbrev-ref HEAD)}
case "$BRANCH" in
  ticket/[0-9]*) ID=$(echo "$BRANCH" | sed -E 's#^ticket/([0-9]+).*#\1#') ;;
  *) fail "branch name does not map to a ticket" "branch='$BRANCH' (expected ticket/<issue#>-<slug>)" "no ticket -> no code: open a GitHub Issue first" ;;
esac

[ -f "$TASKS" ] || fail "tasks file missing" "$TASKS not found (run: node harness/sync-issues.mjs)"

BLOCK=$(awk -v id="#$ID" '/^## /{p=($2==id)} p' "$TASKS")
[ -n "$BLOCK" ] || fail "ticket #$ID not found" "$TASKS has no section '## #$ID' (sync from GitHub Issues)"

STATUS=$(echo "$BLOCK" | sed -n 's/^- status: *//p' | head -1 | tr -d '\r ')
[ "$STATUS" = "in-progress" ] || fail "ticket #$ID is not in-progress" "$TASKS: '- status: $STATUS' (must be in-progress)"

ALLOWED=$(echo "$BLOCK" | awk '/^- allowed:/{a=1;next} /^- /{a=0} a && /^ +- /{sub(/^ +- */,""); gsub(/[`\r ]/,""); print}')
[ -n "$ALLOWED" ] || fail "ticket #$ID has no allowed paths" "$TASKS section '## #$ID' lacks '- allowed:' entries"

MERGE_FILE=$(git rev-parse --git-path MERGE_HEAD)
if [ "$1" = "--ci" ]; then
  FILES=$(git diff --name-only --diff-filter=ACMRD "$2"...HEAD)
elif [ -f "$MERGE_FILE" ]; then
  # Merge commit (pre-commit after a conflicted merge): only a merge of origin/main is allowed. The ticket's own
  # changes are then what the staged result still differs from main by (its commits + any conflict resolution).
  MERGED=$(tr -d '\r' < "$MERGE_FILE")
  [ "$(echo "$MERGED" | wc -l | tr -d ' ')" = "1" ] || fail "octopus merge is not allowed" "MERGE_HEAD lists: $(echo "$MERGED" | tr '\n' ' ')"
  git merge-base --is-ancestor "$MERGED" origin/main 2>/dev/null \
    || fail "only a merge of origin/main into a ticket branch is allowed" \
      "MERGE_HEAD=$MERGED is not contained in origin/main (another ticket branch or an unpushed commit?)" \
      "abort with 'git merge --abort', then 'git fetch origin' and 'git merge origin/main'"
  FILES=$(git diff --cached --name-only --diff-filter=ACMRD "$MERGED")
else
  FILES=$(git diff --cached --name-only --diff-filter=ACMRD)
fi
[ -n "$FILES" ] || { echo "scope-check: no changed files (ticket #$ID)"; exit 0; }

# glob -> case pattern ('**' behaves like '*', and '*' in case matches '/'). Converted ONCE (no per-file forks).
ALLOWED_P=$(echo "$ALLOWED" | sed 's#\*\*#*#g')
PROT_RAW=$(grep -vE '^[[:space:]]*(#|$)' "$PROTECTED" 2>/dev/null | tr -d '\r')
PROT_P=$(echo "$PROT_RAW" | sed 's#\*\*#*#g')

VIOLATIONS=""
for f in $FILES; do
  # protected paths need the protected pattern listed LITERALLY in allowed (a bare ** is not enough)
  prot=""; n=0
  for pp in $PROT_P; do
    n=$((n+1))
    case "$f" in $pp) prot=$(echo "$PROT_RAW" | sed -n "${n}p"); break ;; esac
  done
  if [ -n "$prot" ]; then
    case "
$ALLOWED
" in *"
$prot
"*) continue ;; esac
    VIOLATIONS="$VIOLATIONS
$f  (protected by '$prot'; ticket #$ID must list '$prot' explicitly)"
    continue
  fi
  ok=0
  for a in $ALLOWED_P; do case "$f" in $a) ok=1; break ;; esac; done
  [ $ok -eq 1 ] || VIOLATIONS="$VIOLATIONS
$f  (not in allowed paths of ticket #$ID)"
done
if [ -n "$VIOLATIONS" ]; then
  echo "SCOPE-CHECK FAILED: files outside ticket #$ID scope" >&2
  echo "$VIOLATIONS" | sed '/^$/d; s/^/  evidence: /' >&2
  echo "Allowed (from $TASKS):" >&2; echo "$ALLOWED" | sed 's/^/  - /' >&2
  echo "If this change is really required: open a GitHub Issue (template 'Scope exception'); do not bypass." >&2
  exit 1
fi
echo "scope-check OK (ticket #$ID, $(echo "$FILES" | wc -l | tr -d ' ') file(s))"
