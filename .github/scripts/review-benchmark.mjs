#!/usr/bin/env node
// Meet hoe goed een AI-reviewer echte fouten vangt.
//
// Elk geval in .github/review-benchmark/cases/<naam>/ bestaat uit:
//   case.json   { title, body, bug: "omschrijving" | null, expect: { path, lines: [van, tot] } | null }
//   files/...   de bestanden zoals ze in de PR zouden staan (nieuwe bestanden)
// Gevallen met bug: null zijn foutloze controles (meet valse alarmen).
//
// Een bug telt als "gevangen" als de reviewer een blocker/major meldt in het juiste bestand,
// op of vlak bij de juiste regels (±3) of zonder regelnummer. Draait ai-review.mjs met DRY_RUN=1,
// dus er wordt niets op GitHub geplaatst. Gebruikt dezelfde AI_* variabelen als ai-review.mjs.
//
//   node .github/scripts/review-benchmark.mjs [casesDir]

import { readdirSync, readFileSync, statSync, writeFileSync, appendFileSync, mkdtempSync } from 'node:fs'
import { join, relative, dirname } from 'node:path'
import { tmpdir } from 'node:os'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const casesDir = process.argv[2] || join(here, '..', 'review-benchmark', 'cases')
const reviewer = process.env.AI_NAME || 'reviewer'
const work = mkdtempSync(join(tmpdir(), 'review-benchmark-'))

const walk = (dir) =>
  readdirSync(dir).flatMap((name) => {
    const full = join(dir, name)
    return statSync(full).isDirectory() ? walk(full) : [full]
  })

const blocking = (f) => f.severity === 'blocker' || f.severity === 'major'
const near = (line, [from, to]) => line === null || (line >= from - 3 && line <= to + 3)

const rows = []
for (const name of readdirSync(casesDir).sort()) {
  const dir = join(casesDir, name)
  if (!statSync(dir).isDirectory()) continue
  const meta = JSON.parse(readFileSync(join(dir, 'case.json'), 'utf8'))
  const filesDir = join(dir, 'files')
  const files = walk(filesDir).map((full) => {
    const content = readFileSync(full, 'utf8')
    const lines = content.replace(/\n+$/, '').split('\n')
    return {
      filename: relative(filesDir, full).split('\\').join('/'),
      status: 'added',
      additions: lines.length,
      deletions: 0,
      patch: `@@ -0,0 +1,${lines.length} @@\n` + lines.map((l) => '+' + l).join('\n'),
      content,
    }
  })
  const input = join(work, `${name}.json`)
  const output = join(work, `${name}-result.json`)
  writeFileSync(input, JSON.stringify({ title: meta.title, body: meta.body, files }))

  const started = Date.now()
  const run = spawnSync(process.execPath, [join(here, 'ai-review.mjs')], {
    env: { ...process.env, DRY_RUN: '1', PR_FILES_JSON: input, REVIEW_OUTPUT: output, GITHUB_STEP_SUMMARY: '', GITHUB_OUTPUT: '' },
    encoding: 'utf8',
  })
  const seconds = Math.round((Date.now() - started) / 1000)
  let result
  try {
    result = JSON.parse(readFileSync(output, 'utf8'))
  } catch {
    rows.push({ name, meta, outcome: 'error', note: (run.stderr || run.stdout || '').trim().split('\n').pop(), seconds })
    console.log(`${name}: ERROR`)
    continue
  }

  const majors = result.findings.filter(blocking)
  let outcome
  let note
  if (meta.expect) {
    const hit = majors.find((f) => f.path === meta.expect.path && near(f.line, meta.expect.lines))
    outcome = hit ? 'caught' : 'missed'
    note = hit
      ? `${hit.severity}: ${hit.title}`
      : result.findings.length
        ? `wel gemeld: ${result.findings.map((f) => `${f.severity} r${f.line ?? '?'} ${f.title}`).join('; ')}`
        : 'niets gemeld'
  } else {
    outcome = majors.length ? 'false_alarm' : 'clean'
    note = majors.length ? majors.map((f) => `${f.severity}: ${f.title}`).join('; ') : `${result.findings.length} kleine opmerking(en)`
  }
  rows.push({ name, meta, outcome, note, seconds })
  console.log(`${name}: ${outcome} (${seconds}s) - ${note}`)
}

const bugs = rows.filter((r) => r.meta.expect)
const controls = rows.filter((r) => !r.meta.expect)
const caught = bugs.filter((r) => r.outcome === 'caught').length
const falseAlarms = controls.filter((r) => r.outcome === 'false_alarm').length
const errors = rows.filter((r) => r.outcome === 'error').length

const icon = { caught: '✅ gevangen', missed: '❌ gemist', clean: '✅ geen vals alarm', false_alarm: '⚠️ vals alarm', error: '💥 fout' }
const esc = (s) => String(s ?? '').replace(/\|/g, '\\|').replace(/\n/g, ' ')
const md = [
  `## Review-benchmark: ${reviewer} (\`${process.env.AI_MODEL}\`)`,
  '',
  `**Bugs gevangen: ${caught} van ${bugs.length}** · valse alarmen: ${falseAlarms} van ${controls.length} foutloze gevallen` + (errors ? ` · ${errors} fout(en)` : ''),
  '',
  '| Geval | Ingebouwde bug | Resultaat | Toelichting | Tijd |',
  '|---|---|---|---|---|',
  ...rows.map((r) => `| ${r.name} | ${esc(r.meta.bug ?? '(geen, controle)')} | ${icon[r.outcome]} | ${esc(r.note)} | ${r.seconds}s |`),
  '',
].join('\n')

console.log('\n' + md)
writeFileSync(`benchmark-${reviewer.toLowerCase()}.md`, md)
writeFileSync(`benchmark-${reviewer.toLowerCase()}.json`, JSON.stringify({ reviewer, model: process.env.AI_MODEL, caught, bugs: bugs.length, falseAlarms, controls: controls.length, errors, rows: rows.map(({ meta, ...r }) => ({ ...r, bug: meta.bug })) }, null, 2))
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, md + '\n')
