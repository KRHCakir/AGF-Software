#!/usr/bin/env node
// AI code review voor een pull request, met volledige dekking.
//
// Werkt met elke OpenAI-compatibele API (DeepSeek, Qwen/DashScope, ...):
//   1. haalt de gewijzigde bestanden van de PR op (per bestand, niet één ingekorte diff)
//   2. slaat gegenereerde/binaire bestanden over (en zegt dat)
//   3. verdeelt de rest in delen en laat elk deel apart reviewen, met de volledige
//      nieuwe versie van de bestanden als context
//   4. plaatst (of werkt bij) één review-comment op de PR
//   5. schrijft het resultaat als JSON naar REVIEW_OUTPUT (voor de fix-loop)
//
// Status:
//   approve          alles gezien, geen blocker/major            -> exit 0
//   partial          niet alles gezien (te groot, timeout, ...)  -> exit 0, maar GEEN goedkeuring
//   request_changes  minstens één blocker/major                  -> exit 1
//   (fout)           API/GitHub-fout zonder bruikbaar resultaat  -> exit 2
//
// Alleen ingebouwde Node 22 modules, geen npm install nodig.
//
// Verplichte env:
//   GITHUB_TOKEN, GITHUB_REPOSITORY, PR_NUMBER (niet nodig met DRY_RUN)
//   AI_NAME, AI_BASE_URL, AI_MODEL, AI_API_KEY
// Optioneel:
//   AI_ROLE              primary (standaard) | second  (second = tweede reviewer, zoekt wat de eerste miste)
//   PREVIOUS_REVIEW      pad naar het JSON-resultaat van de eerste reviewer (voor AI_ROLE=second)
//   CHUNK_CHARS          diff-tekens per deel (standaard 45000)
//   CONTEXT_CHARS        tekens volledige-bestandscontext per deel (standaard 45000)
//   MAX_FILE_CONTEXT     grotere bestanden krijgen geen volledige context (standaard 25000)
//   MAX_CHUNKS           maximaal aantal delen; de rest telt als niet gezien (standaard 15)
//   CHUNK_CONCURRENCY    delen tegelijk (standaard 3)
//   AI_REQUEST_TIMEOUT_MS (standaard 240000), AI_RETRIES (standaard 2), AI_JSON_MODE (true/false)
//   GITHUB_REQUEST_TIMEOUT_MS (standaard 30000)
//   REVIEW_OUTPUT        (standaard review-result.json)
//   DRY_RUN=1 + PR_FILES_JSON=pad  -> geen GitHub-aanroepen (gebruikt door de benchmark)

import { readFileSync, writeFileSync, appendFileSync, existsSync } from 'node:fs'

// ---------- configuratie ----------

const fail = (message, code = 2) => {
  console.error('::error::' + message)
  process.exit(code)
}
const env = (name, fallback) => {
  const value = (process.env[name] ?? '').trim()
  if (value) return value
  if (fallback !== undefined) return fallback
  fail(`Environment variable ${name} is required.`)
}
const intEnv = (name, fallback) => {
  const value = Number(env(name, String(fallback)))
  if (!Number.isSafeInteger(value) || value <= 0) fail(`${name} must be a positive integer.`)
  return value
}

const dryRun = env('DRY_RUN', '') === '1'
const token = dryRun ? '' : env('GITHUB_TOKEN')
const repo = dryRun ? 'local/dry-run' : env('GITHUB_REPOSITORY')
const pr = dryRun ? '0' : env('PR_NUMBER')
const aiName = env('AI_NAME')
const baseUrl = env('AI_BASE_URL').replace(/\/+$/, '')
const model = env('AI_MODEL')
const apiKey = env('AI_API_KEY')
const role = env('AI_ROLE', 'primary')
if (!['primary', 'second'].includes(role)) fail('AI_ROLE must be primary or second.')
const chunkChars = intEnv('CHUNK_CHARS', 45000)
const contextChars = intEnv('CONTEXT_CHARS', 45000)
const maxFileContext = intEnv('MAX_FILE_CONTEXT', 25000)
const maxChunks = intEnv('MAX_CHUNKS', 15)
const concurrency = intEnv('CHUNK_CONCURRENCY', 3)
const githubTimeoutMs = intEnv('GITHUB_REQUEST_TIMEOUT_MS', 30000)
const aiTimeoutMs = intEnv('AI_REQUEST_TIMEOUT_MS', 240000)
const aiRetries = Number(env('AI_RETRIES', '2'))
if (!Number.isSafeInteger(aiRetries) || aiRetries < 0) fail('AI_RETRIES must be 0 or a positive integer.')
const jsonModeSetting = env('AI_JSON_MODE', 'true').toLowerCase()
if (!['true', 'false'].includes(jsonModeSetting)) fail('AI_JSON_MODE must be true or false.')
const jsonMode = jsonModeSetting === 'true'
const outputPath = env('REVIEW_OUTPUT', 'review-result.json')
const githubApi = (process.env.GITHUB_API_URL || 'https://api.github.com').replace(/\/+$/, '')

const marker = `<!-- ai-review:${aiName.toLowerCase()} -->`
const FIX_NOTE_MARKER = '<!-- ai-fix-note -->'

// ---------- GitHub ----------

async function gh(path, { method = 'GET', accept = 'application/vnd.github+json', body } = {}) {
  let res
  try {
    res = await fetch(githubApi + path, {
      method,
      headers: {
        Authorization: 'Bearer ' + token,
        Accept: accept,
        'X-GitHub-Api-Version': '2022-11-28',
        'User-Agent': 'ai-review-script',
        ...(body ? { 'Content-Type': 'application/json' } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
      signal: AbortSignal.timeout(githubTimeoutMs),
    })
  } catch (error) {
    if (error?.name === 'TimeoutError' || error?.name === 'AbortError') {
      throw new Error(`GitHub ${method} ${path} timed out after ${githubTimeoutMs} ms.`)
    }
    throw error
  }
  if (!res.ok) {
    throw new Error(`GitHub ${method} ${path} failed: ${res.status} ${(await res.text()).slice(0, 300)}`)
  }
  return accept.includes('json') ? res.json() : res.text()
}

async function ghPaged(path, maxPages = 30) {
  const all = []
  for (let page = 1; page <= maxPages; page++) {
    const sep = path.includes('?') ? '&' : '?'
    const items = await gh(`${path}${sep}per_page=100&page=${page}`)
    all.push(...items)
    if (items.length < 100) break
  }
  return all
}

// ---------- bestanden indelen ----------

// Bestanden die niet door een mens geschreven zijn: wel melden, niet reviewen.
const GENERATED = [
  /(^|\/)Migrations\/.*\.Designer\.cs$/i,
  /ModelSnapshot\.cs$/i,
  /(^|\/)package-lock\.json$/i,
  /(^|\/)(pnpm-lock\.yaml|yarn\.lock)$/i,
  /\.min\.(js|css)$/i,
  /\.(png|jpe?g|gif|webp|ico|pdf|svg|woff2?|ttf|eot|zip)$/i,
]
const isTest = (p) => /(^|\/)(tests?|__tests__|[^/]*\.Tests)\//i.test(p) || /\.(test|spec)\.[jt]sx?$/i.test(p) || /Tests?\.cs$/.test(p)
const isDoc = (p) => /\.(md|txt)$/i.test(p) || /^docs\//i.test(p)
const priority = (p) => (isDoc(p) ? 2 : isTest(p) ? 1 : 0)
// Verwijderde bestanden worden ook beoordeeld: het weghalen van bv. een autorisatiecheck is een wijziging.

// Testdata van de review-benchmark bevat expres bugs: niet reviewen (en niet laten "fixen").
const BENCHMARK_DATA = /^\.github\/review-benchmark\//

function classify(files) {
  const reviewable = []
  const skipped = []
  for (const f of files) {
    if (!dryRun && BENCHMARK_DATA.test(f.filename)) skipped.push({ path: f.filename, reason: 'benchmark-testdata (bevat expres bugs)' })
    else if (GENERATED.some((re) => re.test(f.filename))) skipped.push({ path: f.filename, reason: 'gegenereerd/binair' })
    else if (!f.patch) skipped.push({ path: f.filename, reason: 'geen tekst-diff (binair of te groot voor GitHub)', counts: true })
    else reviewable.push(f)
  }
  // Productiecode eerst, dan tests, dan documentatie
  reviewable.sort((a, b) => priority(a.filename) - priority(b.filename) || a.filename.localeCompare(b.filename))
  return { reviewable, skipped }
}

function buildChunks(files) {
  const chunks = []
  const truncatedFiles = []
  let current = { files: [], size: 0 }
  for (const f of files) {
    let patch = f.patch
    if (patch.length > chunkChars) {
      patch = patch.slice(0, chunkChars)
      truncatedFiles.push(f.filename)
    }
    if (current.files.length && current.size + patch.length > chunkChars) {
      chunks.push(current)
      current = { files: [], size: 0 }
    }
    current.files.push({ ...f, patch, truncated: patch.length !== f.patch.length })
    current.size += patch.length
  }
  if (current.files.length) chunks.push(current)
  return { chunks, truncatedFiles }
}

async function loadContext(chunkFiles, headSha) {
  let budget = contextChars
  const parts = []
  for (const f of chunkFiles) {
    let content = f.content
    if (content === undefined && !dryRun && f.status !== 'removed') {
      try {
        content = await gh(`/repos/${repo}/contents/${encodeURIComponent(f.filename).replace(/%2F/g, '/')}?ref=${headSha}`, {
          accept: 'application/vnd.github.raw',
        })
      } catch {
        content = undefined
      }
    }
    if (typeof content !== 'string' || content.length > maxFileContext || content.length > budget) continue
    budget -= content.length
    const numbered = content.split('\n').map((line, i) => `${String(i + 1).padStart(4)}| ${line}`).join('\n')
    parts.push(`### ${f.filename} (volledige nieuwe versie)\n${numbered}`)
  }
  return parts.join('\n\n')
}

// ---------- model ----------

const SYSTEM_BASE = `You are a senior code reviewer for a production business application for a fresh-produce
(AGF) trading company: .NET 9 backend (EF Core), Next.js/TypeScript frontend, Dutch users.
Money, quantities (kg), dates, invoices and VAT must be exactly right.

You get ONE PART of a pull request: the diff of some files plus the full new version of those files
(with line numbers) as context. Review only the files in this part, but use the PR description and the
list of all changed files to understand intent. Read the full files, not just the changed lines:
check how the changed code interacts with the code around it.

Look actively for: logic errors, off-by-one, null/empty/zero/negative inputs, division by zero,
rounding and decimal/money handling, culture/number/date parsing and formatting, time zones,
missing awaits and async mistakes, transactions and concurrency, EF Core query mistakes,
authorization and input validation, injection, secrets, data loss, and tests that do not actually
test what they claim.

Severity rules (be calibrated, not maximal):
- blocker: will break production, lose or corrupt data, or is a real security hole.
- major: a concrete bug that will realistically happen in normal use of THIS code.
- minor: hardening, robustness for unlikely edge cases, hypothetical "what if" scenarios.
- nit: style, naming, wording.
Theoretical improvements are minor unless you can name a realistic scenario in which the current
code actually fails. Do not report things you are not reasonably sure about as major.

Respond with ONLY a JSON object of this exact shape:
{
  "summary": "one or two sentences about this part",
  "checked": ["3 to 6 concrete things you verified, e.g. 'lege lijst in AverageAmount'"],
  "findings": [
    { "path": "file path", "line": <new-file line number or null>,
      "severity": "blocker" | "major" | "minor" | "nit",
      "title": "short title", "detail": "what is wrong, when it goes wrong, and how to fix it" }
  ]
}
Write summary, checked and findings in Dutch.`

const SYSTEM_SECOND = `${SYSTEM_BASE}

You are the SECOND reviewer. Another model already reviewed this PR; its findings are given to you.
Do NOT repeat its findings. Your job is to find what it missed. Concentrate on the less obvious
categories: edge cases in inputs, money/rounding/culture, data integrity, authorization, concurrency,
and whether the tests really cover the new behaviour. If you disagree with one of its findings
(it is wrong), you may say so as a "nit" finding titled "Oneens met eerdere bevinding".`

function parseModelJson(content) {
  const fence = '`'.repeat(3)
  const cleaned = content.trim().replace(new RegExp('^' + fence + '(?:json)?\\s*', 'i'), '').replace(new RegExp('\\s*' + fence + '\\s*$'), '').trim()
  try {
    const parsed = JSON.parse(cleaned)
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) return parsed
  } catch {
    // Fall back to extracting the first complete JSON object.
  }
  const start = cleaned.indexOf('{')
  if (start < 0) throw new Error(`${aiName} returned no JSON object.`)
  let depth = 0
  let inString = false
  let escaped = false
  for (let i = start; i < cleaned.length; i++) {
    const char = cleaned[i]
    if (inString) {
      if (escaped) escaped = false
      else if (char === '\\') escaped = true
      else if (char === '"') inString = false
      continue
    }
    if (char === '"') inString = true
    else if (char === '{') depth++
    else if (char === '}' && --depth === 0) {
      const parsed = JSON.parse(cleaned.slice(start, i + 1))
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) return parsed
      break
    }
  }
  throw new Error(`${aiName} returned malformed JSON; expected one JSON object.`)
}

const severities = new Set(['blocker', 'major', 'minor', 'nit'])
function validatePart(result) {
  if (!result || typeof result !== 'object' || Array.isArray(result)) throw new Error('result must be a JSON object')
  if (typeof result.summary !== 'string') throw new Error('missing string summary')
  if (!Array.isArray(result.findings)) throw new Error('missing findings array')
  const checked = Array.isArray(result.checked) ? result.checked.filter((c) => typeof c === 'string' && c.trim()) : []
  const findings = result.findings.map((f, i) => {
    if (!f || typeof f !== 'object') throw new Error(`finding ${i + 1} must be an object`)
    if (typeof f.path !== 'string' || !f.path.trim()) throw new Error(`finding ${i + 1} is missing a path`)
    const line = Number.isInteger(f.line) && f.line > 0 ? f.line : null
    if (!severities.has(f.severity)) throw new Error(`finding ${i + 1} has an invalid severity`)
    if (typeof f.title !== 'string' || !f.title.trim()) throw new Error(`finding ${i + 1} is missing a title`)
    if (typeof f.detail !== 'string' || !f.detail.trim()) throw new Error(`finding ${i + 1} is missing a detail`)
    return { path: f.path, line, severity: f.severity, title: f.title, detail: f.detail }
  })
  return { summary: result.summary, checked, findings }
}

async function callModel(system, user) {
  const res = await fetch(baseUrl + '/chat/completions', {
    method: 'POST',
    headers: { Authorization: 'Bearer ' + apiKey, 'Content-Type': 'application/json' },
    body: JSON.stringify({
      model,
      temperature: 0.1,
      ...(jsonMode ? { response_format: { type: 'json_object' } } : {}),
      messages: [
        { role: 'system', content: system },
        { role: 'user', content: user },
      ],
    }),
    signal: AbortSignal.timeout(aiTimeoutMs),
  })
  const text = await res.text()
  if (!res.ok) {
    const error = new Error(`${aiName} API failed: ${res.status} ${text.slice(0, 300)}`)
    error.retryable = res.status === 429 || res.status >= 500
    throw error
  }
  let payload
  try {
    payload = JSON.parse(text)
  } catch {
    throw Object.assign(new Error(`${aiName} API returned invalid JSON.`), { retryable: true })
  }
  const content = payload.choices?.[0]?.message?.content
  if (typeof content !== 'string' || !content.trim()) {
    throw Object.assign(new Error(`${aiName} API response did not contain a message.`), { retryable: true })
  }
  return validatePart(parseModelJson(content))
}

async function reviewPart(system, user, label) {
  let lastError
  for (let attempt = 0; attempt <= aiRetries; attempt++) {
    try {
      if (attempt > 0) {
        console.log(`${label}: retry ${attempt}/${aiRetries}`)
        await new Promise((r) => setTimeout(r, 5000 * attempt))
      }
      return await callModel(system, user)
    } catch (error) {
      const timedOut = error?.name === 'TimeoutError' || error?.name === 'AbortError'
      lastError = timedOut ? new Error(`${aiName} timed out after ${aiTimeoutMs} ms`) : error
      // Ongeldige JSON/validatie of timeout/5xx: nog eens proberen. 4xx (bv. foute key): niet.
      const retryable = timedOut || error.retryable || !String(error.message).includes('API failed')
      console.log(`${label}: ${lastError.message}`)
      if (!retryable) break
    }
  }
  throw lastError
}

async function mapLimit(items, limit, fn) {
  const results = new Array(items.length)
  let next = 0
  const workers = Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const i = next++
      results[i] = await fn(items[i], i)
    }
  })
  await Promise.all(workers)
  return results
}

// ---------- comment ----------

const severityIcon = { blocker: '🛑', major: '🟠', minor: '🟡', nit: '⚪' }
const STATUS_TITLE = {
  approve: '✅ {name} review: goedgekeurd',
  partial: '⚠️ {name} review: gedeeltelijk beoordeeld (geen goedkeuring)',
  request_changes: '❌ {name} review: wijzigingen nodig',
}
const esc = (s) => String(s).replace(/\|/g, '\\|').replace(/\r?\n/g, ' ')

function renderComment(r) {
  const lines = [marker, `## ${STATUS_TITLE[r.status].replace('{name}', aiName)}`, '', r.summary, '']
  const c = r.coverage
  lines.push(
    `**Dekking:** ${c.reviewedFiles} van ${c.reviewableFiles} bestanden volledig beoordeeld, in ${c.parts} ${c.parts === 1 ? 'deel' : 'delen'}` +
      (c.generatedSkipped ? ` · ${c.generatedSkipped} overgeslagen (gegenereerd, binair of benchmark)` : '') +
      '.',
  )
  if (r.status === 'partial') {
    lines.push('', '> Niet alles is gezien. Behandel dit **niet** als goedkeuring: kijk zelf naar de bestanden hieronder, of splits de PR.')
  }
  if (r.findings.length) {
    lines.push('', '| | Bestand | Probleem |', '|---|---|---|')
    for (const f of r.findings) {
      lines.push(`| ${severityIcon[f.severity]} ${f.severity} | \`${f.line ? `${f.path}:${f.line}` : f.path}\` | **${esc(f.title)}** ${esc(f.detail)} |`)
    }
  }
  if (c.notReviewed.length) {
    lines.push('', '<details><summary>Niet (volledig) beoordeeld</summary>', '')
    for (const n of c.notReviewed.slice(0, 60)) lines.push(`- \`${n.path}\`: ${n.reason}`)
    if (c.notReviewed.length > 60) lines.push(`- … en nog ${c.notReviewed.length - 60}`)
    lines.push('', '</details>')
  }
  if (r.checked.length) {
    lines.push('', '<details><summary>Wat is gecontroleerd</summary>', '')
    for (const item of r.checked.slice(0, 40)) lines.push(`- ${item}`)
    lines.push('', '</details>')
  }
  lines.push('', `<sub>Model: \`${model}\` · rol: ${role === 'second' ? 'tweede reviewer' : 'eerste reviewer'}</sub>`)
  return lines.join('\n')
}

async function upsertComment(body) {
  const comments = await ghPaged(`/repos/${repo}/issues/${pr}/comments`, 50)
  const existing = comments.find((c) => typeof c.body === 'string' && c.body.includes(marker))
  if (existing) await gh(`/repos/${repo}/issues/comments/${existing.id}`, { method: 'PATCH', body: { body } })
  else await gh(`/repos/${repo}/issues/${pr}/comments`, { method: 'POST', body: { body } })
}

// ---------- hoofdprogramma ----------

async function main() {
  let meta
  let files
  let fixNotes = ''
  if (dryRun) {
    const data = JSON.parse(readFileSync(env('PR_FILES_JSON'), 'utf8'))
    meta = { title: data.title ?? '(benchmark)', body: data.body ?? '', head: { sha: 'dry-run' } }
    files = data.files
  } else {
    meta = await gh(`/repos/${repo}/pulls/${pr}`)
    files = await ghPaged(`/repos/${repo}/pulls/${pr}/files`)
    const comments = await ghPaged(`/repos/${repo}/issues/${pr}/comments`, 50)
    fixNotes = comments
      .filter((c) => typeof c.body === 'string' && c.body.includes(FIX_NOTE_MARKER))
      .map((c) => c.body.replace(FIX_NOTE_MARKER, '').trim())
      .join('\n\n---\n\n')
      .slice(0, 8000)
  }

  let previous = null
  const previousPath = process.env.PREVIOUS_REVIEW?.trim()
  if (role === 'second' && previousPath && existsSync(previousPath)) {
    previous = JSON.parse(readFileSync(previousPath, 'utf8'))
  }

  const { reviewable, skipped } = classify(files)
  const { chunks: allChunks, truncatedFiles } = buildChunks(reviewable)
  const chunks = allChunks.slice(0, maxChunks)
  const droppedChunks = allChunks.slice(maxChunks)

  const fileList = files.map((f) => `- ${f.filename} (${f.status}, +${f.additions ?? '?'}/-${f.deletions ?? '?'})`).join('\n').slice(0, 12000)
  const header =
    `Pull request #${pr}: ${meta.title}\n\nBeschrijving:\n${(meta.body || '(geen)').slice(0, 4000)}\n\n` +
    `Alle gewijzigde bestanden in deze PR:\n${fileList}\n` +
    (fixNotes ? `\nEerder beoordeeld en met reden afgewezen (niet opnieuw melden, tenzij je concreet aantoont dat de reden niet klopt):\n${fixNotes}\n` : '') +
    (previous
      ? `\nBevindingen van de eerste reviewer (${previous.reviewer}), niet herhalen:\n` +
        (previous.findings?.length ? previous.findings.map((f) => `- [${f.severity}] ${f.path}${f.line ? ':' + f.line : ''} ${f.title}`).join('\n') : '- (geen)') +
        '\n'
      : '')

  console.log(
    `Reviewing PR #${pr} "${meta.title}" with ${aiName} (${model}, ${role}): ` +
      `${reviewable.length} files in ${allChunks.length} part(s), ${skipped.length} skipped`,
  )

  const system = role === 'second' ? SYSTEM_SECOND : SYSTEM_BASE
  const partResults = await mapLimit(chunks, concurrency, async (chunk, i) => {
    const label = `part ${i + 1}/${chunks.length}`
    const context = await loadContext(chunk.files, meta.head?.sha)
    const diff = chunk.files.map((f) => `diff -- ${f.filename} (${f.status})${f.truncated ? ' [INGEKORT]' : ''}\n${f.patch}`).join('\n\n')
    const user = `${header}\nDit is deel ${i + 1} van ${chunks.length}. Bestanden in dit deel: ${chunk.files.map((f) => f.filename).join(', ')}\n\n` +
      `=== DIFF ===\n${diff}\n\n=== VOLLEDIGE BESTANDEN (context) ===\n${context || '(niet beschikbaar of te groot)'}`
    try {
      const result = await reviewPart(system, user, label)
      console.log(`${label}: ${result.findings.length} finding(s)`)
      return { ok: true, index: i, chunk, result }
    } catch (error) {
      return { ok: false, index: i, chunk, error: error.message }
    }
  })

  const okParts = partResults.filter((p) => p.ok)
  if (chunks.length > 0 && okParts.length === 0) {
    throw new Error(`No part could be reviewed: ${partResults[0]?.error}`)
  }

  const notReviewedAll = [
    ...skipped.filter((s) => s.counts).map((s) => ({ path: s.path, reason: s.reason })),
    // Volgorde = belangrijkste reden eerst (bij dubbele paden blijft de eerste staan)
    ...droppedChunks.flatMap((c) => c.files.map((f) => ({ path: f.filename, reason: `PR te groot: meer dan ${maxChunks} delen` }))),
    ...partResults.filter((p) => !p.ok).flatMap((p) => p.chunk.files.map((f) => ({ path: f.filename, reason: `review mislukt (${p.error.slice(0, 120)})` }))),
    ...truncatedFiles.map((p) => ({ path: p, reason: `diff groter dan ${chunkChars} tekens, alleen het begin gezien` })),
  ]
  const seen = new Set()
  const notReviewed = notReviewedAll.filter((n) => !seen.has(n.path) && seen.add(n.path))
  const fullyReviewed = okParts.flatMap((p) => p.chunk.files.filter((f) => !f.truncated)).length
  const findings = okParts.flatMap((p) => p.result.findings)
  const checked = okParts.flatMap((p) => p.result.checked)
  const hasBlocking = findings.some((f) => f.severity === 'blocker' || f.severity === 'major')
  const status = hasBlocking ? 'request_changes' : notReviewed.length ? 'partial' : 'approve'
  const summary =
    okParts.length === 1
      ? okParts[0].result.summary
      : okParts.map((p) => `**Deel ${p.index + 1}:** ${p.result.summary}`).join('\n')

  const result = {
    reviewer: aiName,
    model,
    role,
    pr: Number(pr),
    head: meta.head?.sha,
    status,
    verdict: status, // compatibel met de fix-loop
    summary: summary || (reviewable.length ? '' : 'Geen te beoordelen codewijzigingen (alleen gegenereerde/binaire bestanden).'),
    checked,
    findings,
    coverage: {
      reviewableFiles: reviewable.length + skipped.filter((s) => s.counts).length,
      reviewedFiles: fullyReviewed,
      parts: chunks.length,
      generatedSkipped: skipped.filter((s) => !s.counts).length,
      notReviewed,
    },
  }

  writeFileSync(outputPath, JSON.stringify(result, null, 2))
  const body = renderComment(result)
  if (!dryRun) await upsertComment(body)
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, body.replace(marker, '') + '\n')
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `status=${status}\n`)

  console.log(`Status: ${status} (${findings.length} finding(s), ${notReviewed.length} file(s) not fully reviewed)`)
  if (status === 'partial') console.log(`::warning::${aiName} could not review everything in PR #${pr} - this is NOT an approval.`)
  if (status === 'request_changes') {
    console.log(`::error::${aiName} requests changes on PR #${pr}.`)
    process.exit(1)
  }
}

main().catch((error) => fail(error instanceof Error ? error.message : String(error)))
