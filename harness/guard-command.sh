#!/bin/sh
# Destructive-command gate for agent shells. Reads the command (or the tool-call JSON that
# contains it) from stdin or $1. Exit 2 = BLOCK (Claude Code PreToolUse convention), 0 = allow.
# Wired in .claude/settings.json; for other agents call it before executing shell commands.

INPUT="${1:-$(cat)}"

check() { # $1=regex $2=reason
  if printf '%s' "$INPUT" | grep -Eiq "$1"; then
    echo "BLOCKED by harness/guard-command.sh: $2" >&2
    echo "  evidence: matched /$1/ in: $(printf '%s' "$INPUT" | head -c 200)" >&2
    echo "  If truly needed, open a GitHub Issue (template 'Scope exception')." >&2
    exit 2
  fi
}

check 'rm +(-[a-z]*r[a-z]*f|-[a-z]*f[a-z]*r|--recursive)'          'recursive force delete'
check 'Remove-Item[^|;]*-Recurse[^|;]*-Force|Remove-Item[^|;]*-Force[^|;]*-Recurse' 'recursive force delete (PowerShell)'
check 'git +push[^|;]*(--force|-f( |$)|--force-with-lease)'         'force push'
check 'git +push[^|;]* (origin +)?(main|master)( |$|")'             'direct push to main'
check 'git +reset +--hard'                                          'hard reset'
check 'git +clean +-[a-z]*f'                                        'git clean -f'
check 'git +(commit|push)[^|;]*--no-verify'                         'bypassing hooks'
check 'git +config[^|;]*core\.hooksPath'                            'changing hooks path'
check 'dotnet +ef +database +drop|DROP +(TABLE|DATABASE)|TRUNCATE +TABLE' 'destructive database command'
check '(curl|wget|iwr|Invoke-WebRequest)[^|;]*\|[^|;]*(sh|bash|iex|pwsh)' 'pipe download to shell'
check '(cat|type|Get-Content|echo|printenv)[^|;]*(\.env|id_rsa|\.pem)' 'reading secrets'
check '(Set-Content|Out-File|>|tee)[^|;]*(\.husky/|harness/|\.github/)' 'writing protected paths via shell'

exit 0
