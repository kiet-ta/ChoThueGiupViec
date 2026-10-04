// Start (or resume) work on a ticket in ONE command, so nobody creates branches by hand.
//   node harness/start-ticket.mjs <issue#> [--base <ref>] [--dry-run]
// (PowerShell: ./harness/run.ps1 start-ticket <issue#>)
//
// What it does, in order (every failure prints `evidence:` lines and changes nothing before the failing step):
//   1. validates the GitHub Issue: open, label `ticket`, not `blocked`/`review`, every "Blocked by" issue is closed
//   2. refuses to switch branches with uncommitted tracked changes
//   3. creates ticket/<issue#>-<slug> from a freshly fetched base (default origin/main) or resumes the existing one
//   4. labels the issue `in-progress` (removes `ready`) and assigns you
//   5. regenerates .spec/tasks.md from GitHub Issues (generated file, not tracked) and runs scope-check as proof
import { execFileSync } from 'node:child_process'

const args = process.argv.slice(2)
const dry = args.includes('--dry-run')
const bi = args.indexOf('--base')
const base = bi >= 0 ? args[bi + 1] : 'origin/main'
const n = args.find((a, i) => /^\d+$/.test(a) && (bi < 0 || i !== bi + 1))

const run = (cmd, a, opts = {}) =>
  execFileSync(cmd, a, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], ...opts }).trim()
const fail = (msg, ...evidence) => {
  console.error(`START-TICKET FAILED: ${msg}`)
  evidence.forEach((e) => console.error(`  evidence: ${e}`))
  process.exit(1)
}
const tryRun = (cmd, a) => {
  try {
    return run(cmd, a)
  } catch {
    return null
  }
}

if (!n) fail('missing issue number', 'usage: node harness/start-ticket.mjs <issue#> [--base <ref>] [--dry-run]')
if (bi >= 0 && !base) fail('--base needs a value')

const root = tryRun('git', ['rev-parse', '--show-toplevel'])
if (!root) fail('not inside a git repository')
process.chdir(root)
if (tryRun('gh', ['--version']) === null) fail('GitHub CLI `gh` not found', 'install it, then run: gh auth login')
if (tryRun('gh', ['auth', 'status']) === null) fail('gh is not logged in', 'run: gh auth login')

// 1. validate the issue (GitHub Issues are the source of truth)
let issue
try {
  issue = JSON.parse(run('gh', ['issue', 'view', n, '--json', 'number,title,state,labels,body']))
} catch (e) {
  fail(`cannot read issue #${n}`, String(e.stderr || e.message).split('\n')[0])
}
const labels = issue.labels.map((l) => l.name)
if (issue.state !== 'OPEN') fail(`issue #${n} is not open`, `state=${issue.state}`)
if (!labels.includes('ticket')) fail(`issue #${n} has no label "ticket"`, `labels=${labels.join(',') || '(none)'}`)
if (labels.includes('blocked')) fail(`issue #${n} is blocked`, 'read the issue comments, resolve the blocker, remove the label `blocked`')
if (labels.includes('review')) fail(`issue #${n} is already in review`, 'open a new ticket or ask the reviewer')

const body = (issue.body ?? '').replace(/\r/g, '')
const sec = body.match(/### Blocked by\n\n([\s\S]*?)(?=\n### |$)/)
const blockers = sec ? [...sec[1].matchAll(/#?(\d+)/g)].map((m) => m[1]) : []
for (const b of blockers) {
  let st
  try {
    st = JSON.parse(run('gh', ['issue', 'view', b, '--json', 'state'])).state
  } catch {
    fail(`cannot read blocker #${b} of #${n}`)
  }
  if (st !== 'CLOSED') fail(`ticket #${n} is blocked by #${b}`, `#${b} state=${st} (must be CLOSED / done first)`)
}

let slug = issue.title
  .replace(/^\[ticket\]\s*/i, '')
  .normalize('NFD')
  .replace(/[̀-ͯ]/g, '')
  .replace(/đ/gi, 'd')
  .toLowerCase()
  .replace(/[^a-z0-9]+/g, '-')
  .replace(/^-+|-+$/g, '')
if (slug.length > 40) {
  slug = slug.slice(0, 40) // cut on a word boundary, never mid-word
  const cut = slug.lastIndexOf('-')
  if (cut > 10) slug = slug.slice(0, cut)
}
const branch = `ticket/${n}-${slug || 'work'}`

// existing branch for this ticket? (resume instead of creating a second one: one ticket = one branch)
const prefix = `ticket/${n}-`
const localBranches = run('git', ['branch', '--list', `${prefix}*`, '--format=%(refname:short)']).split('\n').filter(Boolean)
const current = run('git', ['rev-parse', '--abbrev-ref', 'HEAD'])
const existing = localBranches[0]
const remoteExisting = !existing
  ? (tryRun('git', ['ls-remote', '--heads', 'origin', `${prefix}*`]) || '').split('\n').filter(Boolean).map((l) => l.split('\t')[1].replace('refs/heads/', ''))[0]
  : null

const target = existing ?? remoteExisting ?? branch
const mode = existing ? 'resume (local branch exists)' : remoteExisting ? 'resume (branch exists on origin)' : `create from ${base}`

if (dry) {
  console.log(`DRY RUN for #${n} "${issue.title}"`)
  console.log(`  branch : ${target}  [${mode}]`)
  console.log(`  labels : ${labels.join(', ')}  ->  in-progress${labels.includes('in-progress') ? ' (already)' : ''}`)
  console.log(`  blockers: ${blockers.length ? blockers.map((b) => `#${b} closed`).join(', ') : 'none'}`)
  process.exit(0)
}

// 2. never carry half-finished work into another ticket's branch
if (current !== target) {
  const dirty = run('git', ['status', '--porcelain', '--untracked-files=no'])
  if (dirty) fail('uncommitted changes would follow you to the new branch', ...dirty.split('\n').slice(0, 5), 'commit or stash them first')
}

// 3. branch
if (current === target) {
  console.log(`already on ${target}`)
} else if (existing) {
  run('git', ['switch', existing])
  console.log(`resumed ${existing}`)
} else if (remoteExisting) {
  run('git', ['fetch', 'origin', remoteExisting])
  run('git', ['switch', '-c', remoteExisting, '--track', `origin/${remoteExisting}`])
  console.log(`resumed ${remoteExisting} from origin`)
} else {
  if (base.startsWith('origin/')) {
    try {
      run('git', ['fetch', 'origin', base.slice('origin/'.length)])
    } catch (e) {
      fail(`cannot fetch ${base}`, String(e.stderr || e.message).split('\n')[0])
    }
  }
  if (tryRun('git', ['rev-parse', '--verify', `${base}^{commit}`]) === null) fail(`base ref not found: ${base}`)
  run('git', ['switch', '-c', branch, '--no-track', base])
  console.log(`created ${branch} from ${base} (${run('git', ['rev-parse', '--short', base])})`)
}

// 4. label + assignee
if (!labels.includes('in-progress')) {
  try {
    run('gh', ['issue', 'edit', n, '--add-label', 'in-progress'])
    if (labels.includes('ready')) run('gh', ['issue', 'edit', n, '--remove-label', 'ready'])
    console.log(`issue #${n}: label in-progress set`)
  } catch (e) {
    fail(`could not label issue #${n}`, String(e.stderr || e.message).split('\n')[0], `branch ${target} already exists; fix the label by hand`)
  }
}
tryRun('gh', ['issue', 'edit', n, '--add-assignee', '@me'])

// 5. regenerate tasks.md from the Issues and prove scope-check sees the ticket
try {
  console.log(run('node', ['harness/sync-issues.mjs']))
} catch (e) {
  fail('sync-issues failed', String(e.stderr || e.message).split('\n')[0])
}
try {
  console.log(run('sh', ['harness/scope-check.sh']))
} catch (e) {
  fail('scope-check does not accept this ticket', String(e.stderr || e.stdout || e.message).split('\n').slice(0, 4).join(' | '))
}
console.log(`\nREADY: work only on branch ${target}; allowed paths are listed in issue #${n} (see .spec/tasks.md).`)
console.log('Commit with the normal hooks; never use --no-verify. When done: verify L3, open a PR "Closes #' + n + '".')
