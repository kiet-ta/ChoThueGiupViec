// Ratchet: one top-level type per `.cs` file in Backend/ (production code; Backend/Tests is out of scope).
//   node harness/check-one-type-per-file.mjs            fail on a multi-type file that is not in the baseline,
//                                                       and on a baseline entry that no longer violates
//   node harness/check-one-type-per-file.mjs --update   rewrite the baseline (refuses to ADD a file)
//   node harness/check-one-type-per-file.mjs --list     print the current violations
//
// Why a ratchet and not a hard rule: the repo already holds files with several types. The baseline names them,
// so the list can only shrink while the module tickets split them; a NEW multi-type file fails at once. A baseline
// entry that is already split also fails, otherwise the file could be merged back unnoticed. The last split ticket
// leaves the baseline empty. Owner of the rule: Backend/ARCHITECTURE.md "One type per file".
import { execFileSync } from 'node:child_process'
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { readCs, scanText } from './split-types.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const root = join(here, '..')
const baselinePath = join(here, 'one-type-per-file-baseline.txt')

const tracked = execFileSync('git', ['ls-files', '-z', '--', 'Backend/*.cs'], { cwd: root, encoding: 'utf8' })
  .split('\0')
  .filter(Boolean)
  .filter((p) => !p.startsWith('Backend/Tests/') && !/(^|\/)Migrations\//.test(p) && !/(^|\/)(bin|obj)\//.test(p))
  .filter((p) => !p.endsWith('Designer.cs') && !p.endsWith('.g.cs'))

const violations = []
const unparsed = []
for (const p of tracked) {
  if (!existsSync(join(root, p))) continue // deleted in the working tree
  const scan = scanText(readCs(join(root, p)).text)
  if (scan.problems.length) unparsed.push(`${p}: ${scan.problems.join('; ')}`)
  else if (scan.members.length > 1) violations.push(p)
}
violations.sort()

const baseline = existsSync(baselinePath)
  ? readFileSync(baselinePath, 'utf8').split('\n').map((l) => l.trim()).filter((l) => l && !l.startsWith('#'))
  : []
const header = [
  '# Backend production files that still hold more than one top-level type (ratchet, see harness/check-one-type-per-file.mjs).',
  '# Remove a line by splitting the file (node harness/split-types.mjs <file>); never add one.',
]

const args = process.argv.slice(2)
if (args.includes('--list')) {
  console.log(violations.join('\n'))
  process.exit(0)
}
if (unparsed.length) {
  console.error('ONE-TYPE-PER-FILE: the scanner could not read these files (fix the scanner or the file):')
  unparsed.forEach((u) => console.error(`  evidence: ${u}`))
  process.exit(1)
}
const added = violations.filter((v) => !baseline.includes(v))
const stale = baseline.filter((b) => !violations.includes(b))
if (args.includes('--update')) {
  if (added.length) {
    console.error('refusing to add files to the baseline (split them instead):')
    added.forEach((a) => console.error(`  ${a}`))
    process.exit(1)
  }
  writeFileSync(baselinePath, [...header, ...violations].join('\n') + '\n')
  console.log(`baseline updated: ${violations.length} file(s) remain (${stale.length} removed)`)
  process.exit(0)
}
if (added.length || stale.length) {
  console.error('ONE-TYPE-PER-FILE FAILED (rule: Backend/ARCHITECTURE.md "One type per file")')
  added.forEach((a) => console.error(`  evidence: ${a} holds several top-level types; split it: node harness/split-types.mjs ${a}`))
  stale.forEach((s) => console.error(`  evidence: ${s} is split already; run: node harness/check-one-type-per-file.mjs --update`))
  process.exit(1)
}
console.log(`ok   one type per file (${violations.length} baseline file(s) left to split)`)
