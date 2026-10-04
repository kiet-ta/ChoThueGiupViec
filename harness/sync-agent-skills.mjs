// Single source of truth for skills = .agents/skills (read by Codex / Antigravity).
// Claude Code only reads .claude/skills, so that folder is a GENERATED MIRROR (do not hand-edit, do not add Claude-only skills there).
// Usage:  node harness/sync-agent-skills.mjs            copy .agents/skills -> .claude/skills
//         node harness/sync-agent-skills.mjs --check    fail (exit 1) if the mirror differs (used by verify)
import { cpSync, rmSync, existsSync, readdirSync, readFileSync, mkdirSync } from 'node:fs'
import { join } from 'node:path'

const SRC = '.agents/skills'
const DST = '.claude/skills'
const check = process.argv.includes('--check')

const walk = (dir, base = dir, out = new Map()) => {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name)
    if (e.isDirectory()) walk(p, base, out)
    else out.set(p.slice(base.length + 1).replace(/\\/g, '/'), readFileSync(p).toString('latin1').replace(/\r\n/g, '\n'))
  }
  return out
}

if (!existsSync(SRC)) {
  console.error(`sync-agent-skills: ${SRC} not found`)
  process.exit(1)
}

if (check) {
  const a = walk(SRC)
  const b = existsSync(DST) ? walk(DST) : new Map()
  const problems = []
  for (const [k, v] of a) {
    if (!b.has(k)) problems.push(`missing in ${DST}: ${k}`)
    else if (b.get(k) !== v) problems.push(`differs: ${k}`)
  }
  for (const k of b.keys()) if (!a.has(k)) problems.push(`extra in ${DST} (not in ${SRC}): ${k}`)
  if (problems.length) {
    console.error(`SKILL MIRROR DRIFT (${problems.length}): .claude/skills must equal .agents/skills`)
    problems.slice(0, 15).forEach((p) => console.error(`  evidence: ${p}`))
    console.error('  fix: node harness/sync-agent-skills.mjs   (needs a ticket that lists .agents/** and .claude/** literally)')
    process.exit(1)
  }
  console.log(`skill mirror OK (${a.size} files)`)
} else {
  rmSync(DST, { recursive: true, force: true })
  mkdirSync('.claude', { recursive: true })
  cpSync(SRC, DST, { recursive: true })
  console.log(`mirrored ${SRC} -> ${DST} (${walk(DST).size} files)`)
}
