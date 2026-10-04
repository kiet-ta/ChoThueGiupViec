// Mirrors GitHub Issues (label "ticket") into .spec/tasks.md. Issues are the source of truth.
// Usage:  node harness/sync-issues.mjs                 (needs GitHub CLI `gh`, authenticated)
//         node harness/sync-issues.mjs --from-file x.json   (offline/testing: gh JSON output)
// Issue body is produced by .github/ISSUE_TEMPLATE/ticket.yml (### headings).
import { execFileSync } from 'node:child_process'
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'

const args = process.argv.slice(2)
const fileIdx = args.indexOf('--from-file')
let issues
if (fileIdx >= 0) {
  issues = JSON.parse(readFileSync(args[fileIdx + 1], 'utf8'))
} else {
  const out = execFileSync(
    'gh',
    ['issue', 'list', '--label', 'ticket', '--state', 'all', '--limit', '200', '--json', 'number,title,body,labels,state'],
    { encoding: 'utf8' },
  )
  issues = JSON.parse(out)
}

const section = (body, heading) => {
  const m = body.replace(/\r/g, '').match(new RegExp(`### ${heading}\\n\\n([\\s\\S]*?)(?=\\n### |$)`))
  return m ? m[1].trim() : ''
}
const lines = (text) =>
  text.split('\n').map((l) => l.replace(/^[-*]\s*/, '').replace(/`/g, '').trim()).filter((l) => l && l !== '_No response_')

const STATUS_LABELS = ['ready', 'in-progress', 'review', 'blocked']
const out = ['# Tasks (generated from GitHub Issues - DO NOT EDIT BY HAND)', '', 'Regenerate: `node harness/sync-issues.mjs`', '']

for (const i of issues.sort((a, b) => a.number - b.number)) {
  const labels = i.labels.map((l) => l.name)
  const status = i.state === 'CLOSED' ? 'done' : STATUS_LABELS.find((s) => labels.includes(s)) ?? 'ready'
  const body = i.body ?? ''
  const allowed = lines(section(body, 'Allowed paths'))
  const acceptance = lines(section(body, 'Acceptance criteria'))
  const blockedBy = lines(section(body, 'Blocked by')).join(', ') || '-'
  out.push(`## #${i.number} ${i.title}`)
  out.push(`- status: ${status}`)
  out.push(`- area: ${section(body, 'Area') || '-'}`)
  out.push(`- blocked-by: ${blockedBy}`)
  out.push('- allowed:')
  allowed.forEach((p) => out.push(`  - ${p}`))
  out.push('- acceptance:')
  acceptance.forEach((a) => out.push(`  - ${a}`))
  out.push('')
}

mkdirSync('.spec', { recursive: true })
writeFileSync('.spec/tasks.md', out.join('\n'))
console.log(`wrote .spec/tasks.md with ${issues.length} ticket(s)`)
