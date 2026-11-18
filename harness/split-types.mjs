// One type per file: scanner + mechanical splitter for the Backend `.cs` files.
//   node harness/split-types.mjs <file.cs>... [--dry-run]     split each file into one file per top-level type
//   node harness/split-types.mjs --types <dir|file>...        print the sorted fully-qualified top-level type names
//
// Why a splitter and not a hand edit: a split must change NOTHING but the file boundaries. The tool keeps the
// namespace, the `using` lines, and every comment / attribute / XML doc with the type that follows it, then
// proves it: the non-whitespace text of the new files, in order, must equal the original (`verifySplit`).
// `--types` before and after a split must print the same list (a type must not vanish, appear or change namespace).
//
// How: a small C# lexer skips comments, string/char literals (regular, verbatim, interpolated, raw) and
// `[attributes]`, so the end of a type is the matching `}` (or the `;` of a positional record), not a regex guess.
// Limits (the file is SKIPPED and reported, never half-split): `#if`/`#region`/other directives between types,
// block-scoped `namespace X { }`, several namespaces, `delegate`, a type that has no recognisable name,
// two types that would share a file name, a target file that already exists.
import { existsSync, readFileSync, writeFileSync, readdirSync, statSync, unlinkSync } from 'node:fs'
import { basename, dirname, join } from 'node:path'
import { pathToFileURL } from 'node:url'

const KIND_RE = /\b(class|interface|struct|enum|record(?:\s+(?:struct|class))?)\s+([A-Za-z_]\w*)\s*(<)?/
const isIdent = (c) => /[A-Za-z0-9_]/.test(c)

// ---- lexer ------------------------------------------------------------------------------------------------
// Each skip* takes the index of the first char of the construct and returns the index just after it.
function skipLineComment(t, i) {
  while (i < t.length && t[i] !== '\n') i++
  return i
}
function skipBlockComment(t, i) {
  const e = t.indexOf('*/', i + 2)
  return e < 0 ? t.length : e + 2
}
function skipChar(t, i) {
  i++ // opening '
  while (i < t.length && t[i] !== '\'') i += t[i] === '\\' ? 2 : 1
  return i + 1
}
// a `{ ... }` hole of an interpolated string; i points at the first char AFTER the opening `{`
function skipHole(t, i) {
  let d = 1
  while (i < t.length && d > 0) {
    const c = t[i]
    if (c === '"' || c === '$' || c === '@') {
      const e = tryString(t, i)
      if (e > i) { i = e; continue }
    }
    if (c === '\'') { i = skipChar(t, i); continue }
    if (c === '/' && t[i + 1] === '/') { i = skipLineComment(t, i); continue }
    if (c === '/' && t[i + 1] === '*') { i = skipBlockComment(t, i); continue }
    if (c === '{') d++
    else if (c === '}') d--
    i++
  }
  return i
}
// returns the index after a string literal starting at i, or i when there is none at i
function tryString(t, i) {
  let j = i, interp = false, verbatim = false, dollars = 0
  while (t[j] === '$' || t[j] === '@') {
    if (t[j] === '$') { interp = true; dollars++ } else verbatim = true
    j++
  }
  if (t[j] !== '"') return i
  let q = 0
  while (t[j + q] === '"') q++
  if (q >= 3 && !verbatim) {
    // raw string: ends at a run of exactly q quotes
    const close = '"'.repeat(q)
    let k = j + q
    for (;;) {
      const e = t.indexOf(close, k)
      if (e < 0) return t.length
      let n = 0
      while (t[e + n] === '"') n++
      if (n >= q) return e + n
      k = e + n
    }
  }
  j++ // opening quote
  while (j < t.length) {
    const c = t[j]
    if (verbatim) {
      if (c === '"') { if (t[j + 1] === '"') { j += 2; continue } return j + 1 }
    } else {
      if (c === '\\') { j += 2; continue }
      if (c === '"') return j + 1
    }
    if (interp && c === '{') {
      if (t[j + 1] === '{') { j += 2; continue }
      j = skipHole(t, j + 1)
      continue
    }
    if (interp && c === '}' && t[j + 1] === '}') { j += 2; continue }
    j++
  }
  return t.length
}

// the first `{` (body) or `;` (no body) at paren/bracket depth 0, then the matching `}`.
// Returns { end, headerEnd, hasBody } where headerEnd = index of that `{` / `;`.
function scanMember(t, from) {
  let i = from, paren = 0, bracket = 0
  let bodyStart = -1, depth = 0
  while (i < t.length) {
    const c = t[i]
    if (c === '/' && t[i + 1] === '/') { i = skipLineComment(t, i); continue }
    if (c === '/' && t[i + 1] === '*') { i = skipBlockComment(t, i); continue }
    if (c === '"' || c === '$' || c === '@') {
      const e = tryString(t, i)
      if (e > i) { i = e; continue }
    }
    if (c === '\'') { i = skipChar(t, i); continue }
    if (bodyStart < 0) {
      if (c === '(') paren++
      else if (c === ')') paren--
      else if (c === '[') bracket++
      else if (c === ']') bracket--
      else if (paren === 0 && bracket === 0 && c === ';') return { end: i + 1, headerEnd: i, hasBody: false }
      else if (paren === 0 && bracket === 0 && c === '{') { bodyStart = i; depth = 1 }
    } else if (c === '{') depth++
    else if (c === '}') { depth--; if (depth === 0) return { end: i + 1, headerEnd: bodyStart, hasBody: true } }
    i++
  }
  return null
}

// strip comments and balanced [attributes] so the kind/name regex sees only the declaration
function cleanHeader(h) {
  let out = '', i = 0
  while (i < h.length) {
    const c = h[i]
    if (c === '/' && h[i + 1] === '/') { i = skipLineComment(h, i); continue }
    if (c === '/' && h[i + 1] === '*') { i = skipBlockComment(h, i); continue }
    if (c === '[') {
      let d = 0
      while (i < h.length) {
        const e = tryString(h, i)
        if (e > i) { i = e; continue }
        if (h[i] === '[') d++
        else if (h[i] === ']') { d--; if (d === 0) { i++; break } }
        i++
      }
      continue
    }
    out += c
    i++
  }
  return out
}

// ---- file scan --------------------------------------------------------------------------------------------
// -> { problems: [], ns, preambleEnd, members: [{ kind, name, arity, start, end }] }   (indices into the LF text)
export function scanText(text) {
  const res = { problems: [], ns: null, preambleEnd: 0, members: [] }
  if (/^[ \t]*#(if|elif|else|endif|region|endregion|pragma|define|undef|line)\b/m.test(text)) {
    res.problems.push('preprocessor directive (#if/#region/...) in file')
    return res
  }
  let i = 0, preambleEnd = 0, prevEnd = 0, sawMember = false
  const n = text.length
  for (;;) {
    // trivia
    while (i < n) {
      if (/\s/.test(text[i])) i++
      else if (text[i] === '/' && text[i + 1] === '/') i = skipLineComment(text, i)
      else if (text[i] === '/' && text[i + 1] === '*') i = skipBlockComment(text, i)
      else if (text[i] === '﻿') i++
      else break
    }
    if (i >= n) break
    if (res.blockNs && text[i] === '}') break
    const word = /^[A-Za-z_]\w*/.exec(text.slice(i, i + 40))?.[0]
    if (!sawMember && /^\[\s*(assembly|module)\s*:/.test(text.slice(i, i + 40))) {
      // assembly-level attribute: part of the preamble, not a type
      let d = 0
      for (;;) {
        const e = tryString(text, i)
        if (e > i) { i = e; continue }
        if (text[i] === '[') d++
        else if (text[i] === ']' && --d === 0) { i++; break }
        i++
      }
      preambleEnd = i
      prevEnd = i
      continue
    }
    if (!sawMember && (word === 'using' || word === 'global' || word === 'extern' || word === 'namespace')) {
      const e = text.indexOf(';', i)
      const b = text.indexOf('{', i)
      if (word === 'namespace') {
        if (res.ns) {
          res.problems.push('several namespaces')
          return res
        }
        if (b >= 0 && (e < 0 || b < e)) {
          // block-scoped: members are read inside the braces (counting works; planSplit refuses to split it)
          res.ns = text.slice(i + 'namespace'.length, b).trim()
          res.blockNs = true
          i = b + 1
          preambleEnd = i
          prevEnd = i
          continue
        }
        res.ns = text.slice(i + 'namespace'.length, e).trim()
      }
      if (e < 0) { res.problems.push('unterminated directive'); return res }
      i = e + 1
      preambleEnd = i
      prevEnd = i
      continue
    }
    const m = scanMember(text, i)
    if (!m) { res.problems.push(`unbalanced braces near offset ${i}`); return res }
    const header = cleanHeader(text.slice(i, m.headerEnd))
    const k = KIND_RE.exec(header)
    if (!k) {
      res.problems.push(`cannot find a type name in: ${header.trim().slice(0, 60).replace(/\s+/g, ' ')}`)
      return res
    }
    let arity = 0
    if (k[3]) {
      // count generic params: `<A, B>` -> 2 (commas at depth 1)
      const gi = header.indexOf('<', k.index + k[0].length - 1)
      let d = 0, commas = 0
      for (let p = gi; p < header.length; p++) {
        if (header[p] === '<') d++
        else if (header[p] === '>') { d--; if (d === 0) break }
        else if (header[p] === ',' && d === 1) commas++
      }
      arity = commas + 1
    }
    res.members.push({ kind: k[1].replace(/\s+/g, ' '), name: k[2], arity, start: prevEnd, end: m.end })
    sawMember = true
    prevEnd = m.end
    i = m.end
    // a stray `;` after `}` belongs to the member
    if (text[i] === ';') { res.members[res.members.length - 1].end = i + 1; prevEnd = i + 1; i++ }
  }
  res.preambleEnd = preambleEnd
  // text after the last member that is not whitespace (a trailing comment) is kept with the last member
  if (res.members.length && text.slice(prevEnd).trim()) res.members[res.members.length - 1].end = n
  return res
}

export function readCs(path) {
  const raw = readFileSync(path, 'utf8')
  const bom = raw.startsWith('﻿')
  const crlf = raw.includes('\r\n')
  let text = bom ? raw.slice(1) : raw
  if (crlf) text = text.replace(/\r\n/g, '\n')
  return { text, bom, crlf }
}

export const fullName = (ns, m) => `${ns ? ns + '.' : ''}${m.name}${m.arity ? '`' + m.arity : ''}`

// ---- split ------------------------------------------------------------------------------------------------
const squash = (s) => s.replace(/\s+/g, '')

export function planSplit(path) {
  const { text, bom, crlf } = readCs(path)
  const scan = scanText(text)
  if (scan.problems.length) return { path, skip: scan.problems.join('; ') }
  if (scan.members.length < 2) return { path, skip: 'single type, nothing to split' }
  if (scan.blockNs) return { path, skip: 'block-scoped namespace' }
  const names = scan.members.map((m) => m.name)
  const dup = names.find((x, i) => names.indexOf(x) !== i)
  if (dup) return { path, skip: `two types share the file name ${dup}.cs (generic arity or partial in one file)` }
  const dir = dirname(path)
  const own = basename(path, '.cs')
  const header = text.slice(0, scan.preambleEnd).replace(/^\s+/, '') // never starts with blank lines
  const files = scan.members.map((m) => {
    const body = text.slice(m.start, m.end).replace(/^\s*\n/, '').replace(/^\n+/, '').trimEnd()
    return { name: m.name, kind: m.kind, target: join(dir, `${m.name}.cs`), body, member: m }
  })
  for (const f of files) {
    if (f.name !== own && existsSync(f.target)) return { path, skip: `target exists: ${f.target}` }
  }
  const render = (f) => {
    let out = `${header}\n\n${f.body}\n`
    if (crlf) out = out.replace(/\n/g, '\r\n')
    return (bom ? '﻿' : '') + out
  }
  const out = files.map((f) => ({ target: f.target, name: f.name, content: render(f) }))
  // proof: the non-whitespace text of the bodies, in order, equals the original after the preamble
  const original = squash(text.slice(scan.preambleEnd))
  const rebuilt = squash(files.map((f) => f.body).join(''))
  if (original !== rebuilt) return { path, skip: 'internal check failed: split text differs from the original' }
  return { path, ns: scan.ns, out, keeps: files.some((f) => f.name === own) }
}

export function typeNames(path) {
  const { text } = readCs(path)
  const scan = scanText(text)
  if (scan.problems.length) return { path, problems: scan.problems, names: [] }
  return { path, problems: [], names: scan.members.map((m) => fullName(scan.ns, m)) }
}

// A tree walk skips what the one-type rule excludes (check-one-type-per-file.mjs): migrations and generated files.
// A file named explicitly on the command line is always read.
export function walkCs(p, acc = [], explicit = true) {
  const st = statSync(p)
  if (st.isFile()) {
    if (p.endsWith('.cs') && (explicit || !/\.(Designer|g)\.cs$/.test(p))) acc.push(p)
    return acc
  }
  for (const e of readdirSync(p)) {
    if (e === 'bin' || e === 'obj' || e === 'node_modules' || e === 'Migrations' || e.startsWith('.')) continue
    walkCs(join(p, e), acc, false)
  }
  return acc
}

// ---- cli --------------------------------------------------------------------------------------------------
function main(argv) {
  const dry = argv.includes('--dry-run')
  if (argv[0] === '--types') {
    const names = new Set()
    let bad = 0
    for (const p of argv.slice(1).filter((a) => !a.startsWith('--')).flatMap((a) => walkCs(a))) {
      const r = typeNames(p)
      if (r.problems.length) { console.error(`unparsed: ${p}: ${r.problems.join('; ')}`); bad++ }
      r.names.forEach((x) => names.add(x))
    }
    console.log([...names].sort().join('\n'))
    process.exit(bad ? 1 : 0)
  }
  const files = argv.filter((a) => !a.startsWith('--'))
  if (!files.length) {
    console.error('usage: node harness/split-types.mjs <file.cs>... [--dry-run]   |   --types <dir|file>...')
    process.exit(2)
  }
  let skipped = 0, written = 0
  for (const f of files) {
    const plan = planSplit(f)
    if (plan.skip) {
      console.log(`SKIP  ${f}: ${plan.skip}`)
      if (!/nothing to split/.test(plan.skip)) skipped++
      continue
    }
    console.log(`SPLIT ${f} -> ${plan.out.map((o) => o.name).join(', ')}`)
    if (dry) continue
    for (const o of plan.out) { writeFileSync(o.target, o.content); written++ }
    // the original is overwritten by the file of the type that carries its name; when no type does, it is deleted
    if (!plan.keeps) {
      unlinkSync(f)
      console.log(`  removed ${f} (no type is named ${basename(f, '.cs')})`)
    }
  }
  console.log(`done: ${written} file(s) written, ${skipped} skipped${dry ? ' (dry run)' : ''}`)
  process.exit(skipped ? 1 : 0)
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main(process.argv.slice(2))
