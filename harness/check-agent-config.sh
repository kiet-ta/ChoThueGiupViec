#!/bin/sh
# Agent-config gate (runs in verify L1, CI and pre-commit when agent files change).
# Guarantees every agent (Claude / Codex / Antigravity) loads the same rules, skills and command gate.
# Exit 1 with `evidence:` lines on any problem.

ROOT=$(git rev-parse --show-toplevel) || exit 1
cd "$ROOT" || exit 1
ERR=""
err() { ERR="$ERR
  evidence: $1"; }

# 1. required files
for f in AGENTS.md CLAUDE.md .claude/settings.json .codex/rules/default.rules .spec/decisions.md; do
  [ -f "$f" ] || err "missing required file: $f"
done
[ -d .agents/skills ] || err "missing directory: .agents/skills"

# 2. CLAUDE.md must import AGENTS.md (Claude Code does not read AGENTS.md by itself)
grep -q '^@AGENTS.md' CLAUDE.md 2>/dev/null || err "CLAUDE.md must start with '@AGENTS.md'"

# 3. .claude/settings.json: valid JSON, guard-command hook present, no permission bypass
if [ -f .claude/settings.json ]; then
  node -e "JSON.parse(require('fs').readFileSync('.claude/settings.json','utf8'))" 2>/dev/null || err ".claude/settings.json is not valid JSON"
  grep -q 'harness/guard-command.sh' .claude/settings.json || err ".claude/settings.json lost the guard-command hook (harness/guard-command.sh)"
  grep -Eq 'bypassPermissions|dangerously|"defaultMode" *: *"(acceptEdits|auto)"' .claude/settings.json && err ".claude/settings.json weakens permissions (bypass/auto mode)"
fi

# 4. Codex rules: only prefix_rule lines/comments, and the key bans still exist
if [ -f .codex/rules/default.rules ]; then
  BAD=$(grep -nvE '^[[:space:]]*(#|$)|^prefix_rule\(' .codex/rules/default.rules | head -3)
  [ -z "$BAD" ] || err ".codex/rules/default.rules has non prefix_rule lines: $BAD"
  for k in '"push", "--force"' '"reset", "--hard"' '"--no-verify"'; do
    grep -q "$k" .codex/rules/default.rules || err ".codex/rules/default.rules lost the ban for $k"
  done
fi

# 5. every skill: SKILL.md with frontmatter name + description
for d in .agents/skills/*/; do
  [ -d "$d" ] || continue
  f="${d}SKILL.md"
  if [ ! -f "$f" ]; then err "skill without SKILL.md: $d"; continue; fi
  fm=$(tr -d '\r' < "$f" | awk 'NR==1 && $0!="---"{exit} NR>1 && $0=="---"{exit} NR>1{print}')
  [ -n "$fm" ] || { err "no YAML frontmatter in $f"; continue; }
  echo "$fm" | grep -q '^name:'        || err "frontmatter 'name:' missing in $f"
  echo "$fm" | grep -q '^description:' || err "frontmatter 'description:' missing in $f"
done

# 6. Claude mirror equals the source of truth
node harness/sync-agent-skills.mjs --check > /tmp/skill-mirror.$$ 2>&1 || { ERR="$ERR
$(sed 's/^/  /' /tmp/skill-mirror.$$)"; }
rm -f /tmp/skill-mirror.$$

# 7. credentials / local files must never be tracked
TRACKED=$(git ls-files | grep -E '(^|/)(\.codex/auth\.json|\.claude/settings\.local\.json|[^/]+\.sqlite(-shm|-wal)?)$')
[ -z "$TRACKED" ] || err "local/credential files are tracked: $(echo "$TRACKED" | head -3 | tr '\n' ' ')"

if [ -n "$ERR" ]; then
  echo "AGENT-CONFIG CHECK FAILED" >&2
  echo "$ERR" | sed '/^$/d' >&2
  exit 1
fi
echo "agent-config OK (rules, hooks, $(ls -d .agents/skills/*/ 2>/dev/null | wc -l | tr -d ' ') skills, mirror in sync)"
