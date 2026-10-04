# PowerShell entry points. Logic lives in the .sh files (single source of truth, no drift);
# this wrapper finds Git's bash on Windows. Usage:
#   ./harness/run.ps1 verify L1|L2|L3
#   ./harness/run.ps1 scope-check [--ci origin/main]
#   ./harness/run.ps1 guard-command "<command>"
#   ./harness/run.ps1 start-ticket <issue#> [--base <ref>] [--dry-run]   (node; creates/resumes ticket/<n>-<slug>)
param(
  [Parameter(Mandatory = $true)][ValidateSet('verify', 'scope-check', 'guard-command', 'start-ticket')][string]$Tool,
  [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)
if ($Tool -eq 'start-ticket') {
  $root = git rev-parse --show-toplevel
  Push-Location $root
  try { & node 'harness/start-ticket.mjs' @Rest; exit $LASTEXITCODE } finally { Pop-Location }
}
$sh =(Get-Command sh -ErrorAction SilentlyContinue).Source
if (-not $sh) { $sh = 'C:\Program Files\Git\bin\sh.exe' }
if (-not (Test-Path $sh)) { Write-Error 'Git Bash (sh) not found. Install Git for Windows.'; exit 1 }
$root = git rev-parse --show-toplevel
Push-Location $root
try { & $sh "harness/$Tool.sh" @Rest; exit $LASTEXITCODE } finally { Pop-Location }
