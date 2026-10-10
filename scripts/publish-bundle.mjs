// npm run publish:bundle -- --product <code> [--bundle output/bundle] [--bucket <name>] [--distribution <id>]
//   [--profile <aws profile>] [--dry-run]
// Uploads the build that {bundle}/{product}/current.json names to the assets bucket, then switches the
// live current.json and invalidates it in CloudFront. Uses the AWS CLI and whatever credentials it
// finds (a profile locally, the deploy role in CI). --bucket and --distribution default to the
// BUNDLE_BUCKET and ASSET_DISTRIBUTION_ID environment variables. --dry-run prints the commands only.
import { mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, relative, sep } from 'node:path'
import { gzipSync } from 'node:zlib'
import { fail, publishPlan, root, run } from './lib.mjs'

const opts = { bundle: join(root, 'output', 'bundle'), bucket: process.env.BUNDLE_BUCKET, distribution: process.env.ASSET_DISTRIBUTION_ID }
const argv = process.argv.slice(2)
for (let i = 0; i < argv.length; i++) {
  const a = argv[i]
  if (a === '--dry-run') opts.dryRun = true
  else if (['--product', '--bundle', '--bucket', '--distribution', '--profile'].includes(a) && argv[i + 1]) opts[a.slice(2)] = argv[++i]
  else fail(`Unknown or incomplete argument: ${a}`)
}
if (!opts.product) fail('--product is required, for example --product wow_classic_beta')
if (!opts.bucket || !opts.distribution) fail('Set --bucket and --distribution, or BUNDLE_BUCKET and ASSET_DISTRIBUTION_ID')

let current
try {
  current = JSON.parse(readFileSync(join(opts.bundle, opts.product, 'current.json'), 'utf8'))
} catch (e) {
  fail(`Could not read ${opts.product}/current.json in ${opts.bundle}: ${e.message}`)
}

const buildDir = join(opts.bundle, opts.product, current.build)
const files = []
const walk = (dir) => {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name)
    if (e.isDirectory()) walk(p)
    else files.push({ path: relative(buildDir, p).split(sep).join('/'), size: statSync(p).size })
  }
}
try { walk(buildDir) } catch (e) { fail(`Could not read the build folder ${buildDir}: ${e.message}`) }

let plan
try {
  plan = publishPlan({ bundleDir: opts.bundle, product: opts.product, bucket: opts.bucket, distribution: opts.distribution, profile: opts.profile, current, files })
} catch (e) {
  fail(e.message)
}
console.log(`Publishing ${opts.product} ${current.build}: ${files.length} files, ${plan.filter((s) => s.kind === 'gzip').length} gzipped.`)

const tmp = mkdtempSync(join(tmpdir(), 'altrobe-publish-'))
try {
  for (const step of plan) {
    let args = step.args
    if (step.kind === 'gzip') {
      const gz = join(tmp, `${plan.indexOf(step)}.gz`)
      if (!opts.dryRun) writeFileSync(gz, gzipSync(readFileSync(step.file), { level: 9 }))
      args = args.map((x) => (x === '{gz}' ? gz : x))
    }
    console.log(`aws ${args.join(' ')}`)
    if (!opts.dryRun) run('aws', args)
  }
} finally {
  rmSync(tmp, { recursive: true, force: true })
}
console.log(opts.dryRun ? 'Dry run: nothing was uploaded.' : `${opts.product} now serves ${current.build}.`)
