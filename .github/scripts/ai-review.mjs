#!/usr/bin/env node
// AI code review voor een pull request.
//
// Werkt met elke OpenAI-compatibele API (DeepSeek, Qwen/DashScope, ...):
//   1. haalt de PR-diff op via de GitHub REST API
//   2. stuurt die naar het model met een vaste review-opdracht
//   3. plaatst (of werkt bij) één review-comment op de PR
//   4. schrijft het resultaat als JSON naar REVIEW_OUTPUT (voor de latere fix-loop)
//   5. eindigt met exit code 1 als het model "request_changes" geeft
//
// Alleen ingebouwde Node 22 modules, geen npm install nodig.
//
// Verplichte env:
//   GITHUB_TOKEN, GITHUB_REPOSITORY, PR_NUMBER
//   AI_NAME      weergavenaam, bv. "DeepSeek" of "Qwen"
//   AI_BASE_URL  bv. https://api.deepseek.com
//   AI_MODEL     bv. deepseek-v4-pro
//   AI_API_KEY
// Optioneel:
//   MAX_DIFF_CHARS (standaard 60000), REVIEW_OUTPUT (standaard review-result.json)

import { writeFileSync } from 'node:fs'

const env = (name, fallback) => {
  const value = (process.env[name] ?? '').trim()
  if (value) return value
  if (fallback !== undefined) return fallback
  console.error(`::error::Environment variable ${name} is required.`)
  process.exit(2)
}

const token = env('GITHUB_TOKEN')
const repo = env('GITHUB_REPOSITORY')
const pr = env('PR_NUMBER')
const aiName = env('AI_NAME')
const baseUrl = env('AI_BASE_URL').replace(/\/+$/, '')
const model = env('AI_MODEL')
const apiKey = env('AI_API_KEY')
const rawMaxDiffChars = env('MAX_DIFF_CHARS', '60000')
const maxDiffChars = Number(rawMaxDiffChars)
if (!Number.isSafeInteger(maxDiffChars) || maxDiffChars <= 0) {
  console.error('::error::MAX_DIFF_CHARS must be a positive safe integer.')
  process.exit(2)
}
const outputPath = env('REVIEW_OUTPUT', 'review-result.json')
const githubApi = (process.env.GITHUB_API_URL || 'https://api.github.com').replace(/\/+$/, '')

const marker = `<!-- ai-review:${aiName.toLowerCase()} -->`

async function gh(path, { method = 'GET', accept = 'application/vnd.github+json', body } = {}) {
  const res = await fetch(`${githubApi}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: accept,
      'X-GitHub-Api-Version': '2022-11-28',
      'User-Agent': 'ai-review-script',
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  if (!res.ok) {
    throw new Error(`GitHub ${method} ${path} failed: ${res.status} ${(await res.text()).slice(0, 300)}`)
  }
  return accept.includes('diff') ? res.text() : res.json()
}

async function askModel(system, user) {
  const res = await fetch(`${baseUrl}/chat/completions`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${apiKey}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({
      model,
      temperature: 0.1,
      response_format: { type: 'json_object' },
      messages: [
        { role: 'system', content: system },
        { role: 'user', content: user },
      ],
    }),
  })
  const text = await res.text()
  if (!res.ok) throw new Error(`${aiName} API failed: ${res.status} ${text.slice(0, 500)}`)
  const content = JSON.parse(text).choices?.[0]?.message?.content ?? ''
  // Sommige modellen zetten JSON toch in een ```json blok
  const cleaned = content.replace(/^```(?:json)?\s*/i, '').replace(/\s*```\s*$/, '')
  return JSON.parse(cleaned)
}

const SYSTEM = `You are a strict, senior code reviewer for a production business application
(.NET 9 backend, Next.js frontend). Review ONLY the pull request diff you are given.
Focus on: correctness and bugs, security (auth, injection, secrets, data exposure),
data integrity, error handling, and maintainability. Ignore pure taste.

Respond with ONLY a JSON object of this exact shape:
{
  "verdict": "approve" | "request_changes",
  "summary": "one or two sentences",
  "findings": [
    { "path": "file path from the diff", "line": <new-file line number or null>,
      "severity": "blocker" | "major" | "minor" | "nit",
      "title": "short title", "detail": "what is wrong and how to fix it" }
  ]
}
Use "request_changes" only when there is at least one blocker or major finding.
Write summary and findings in Dutch.`

const severityIcon = { blocker: '🛑', major: '🟠', minor: '🟡', nit: '⚪' }

function renderComment(result, truncated) {
  const ok = result.verdict === 'approve'
  const lines = [
    marker,
    `## ${ok ? '✅' : '❌'} ${aiName} review: ${ok ? 'goedgekeurd' : 'wijzigingen nodig'}`,
    '',
    result.summary || '',
  ]
  const findings = Array.isArray(result.findings) ? result.findings : []
  if (findings.length) {
    lines.push('', '| | Bestand | Probleem |', '|---|---|---|')
    for (const f of findings) {
      const where = f.line ? `${f.path}:${f.line}` : f.path
      const detail = `**${f.title ?? ''}** ${f.detail ?? ''}`.replace(/\|/g, '\\|').replace(/\n/g, ' ')
      lines.push(`| ${severityIcon[f.severity] ?? '•'} ${f.severity ?? ''} | \`${where}\` | ${detail} |`)
    }
  }
  if (truncated) lines.push('', `_Let op: de diff was groter dan ${maxDiffChars} tekens en is ingekort._`)
  lines.push('', `<sub>Model: \`${model}\`</sub>`)
  return lines.join('\n')
}

async function upsertComment(body) {
  let existing
  for (let page = 1; ; page++) {
    const comments = await gh(`/repos/${repo}/issues/${pr}/comments?per_page=100&page=${page}`)
    existing = comments.find((c) => typeof c.body === 'string' && c.body.includes(marker))
    if (existing || comments.length < 100) break
  }

  if (existing) {
    await gh(`/repos/${repo}/issues/comments/${existing.id}`, { method: 'PATCH', body: { body } })
  } else {
    await gh(`/repos/${repo}/issues/${pr}/comments`, { method: 'POST', body: { body } })
  }
}

async function main() {
  const meta = await gh(`/repos/${repo}/pulls/${pr}`)
  let diff = await gh(`/repos/${repo}/pulls/${pr}`, { accept: 'application/vnd.github.diff' })
  const truncated = diff.length > maxDiffChars
  if (truncated) diff = diff.slice(0, maxDiffChars)

  console.log(`Reviewing PR #${pr} "${meta.title}" with ${aiName} (${model}), diff ${diff.length} chars`)

  const user = `Pull request #${pr}: ${meta.title}\n\nDescription:\n${meta.body || '(none)'}\n\nDiff:\n${diff}`
  const result = await askModel(SYSTEM, user)
  if (result.verdict !== 'approve' && result.verdict !== 'request_changes') {
    throw new Error(`Unexpected verdict from ${aiName}: ${JSON.stringify(result.verdict)}`)
  }

  writeFileSync(outputPath, JSON.stringify({ reviewer: aiName, model, pr: Number(pr), head: meta.head?.sha, ...result }, null, 2))
  await upsertComment(renderComment(result, truncated))

  console.log(`Verdict: ${result.verdict} — ${result.summary}`)
  if (process.env.GITHUB_STEP_SUMMARY) {
    writeFileSync(process.env.GITHUB_STEP_SUMMARY, renderComment(result, truncated).replace(marker, '') + '\n', { flag: 'a' })
  }
  if (result.verdict === 'request_changes') {
    console.log(`::error::${aiName} requests changes on PR #${pr}.`)
    process.exit(1)
  }
}

main().catch((error) => {
  console.error(`::error::${error instanceof Error ? error.message : String(error)}`)
  process.exit(2)
})
