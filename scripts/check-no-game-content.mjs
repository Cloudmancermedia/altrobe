// Fails when a tracked file looks like Blizzard game content. See gameContentProblems in lib.mjs.
import { spawnSync } from 'node:child_process'
import { fail, gameContentProblems, root } from './lib.mjs'

const r = spawnSync('git', ['ls-files', '-z'], { cwd: root, encoding: 'utf8' })
if (r.error || r.status !== 0) fail(`git ls-files failed: ${r.error?.message ?? r.stderr}`)
const problems = gameContentProblems(r.stdout.split('\0').filter(Boolean))
if (problems.length) fail(`These tracked files look like game content, which this repository must never contain:\n${problems.map((p) => `  ${p}`).join('\n')}`)
console.log('No game content in tracked files.')
