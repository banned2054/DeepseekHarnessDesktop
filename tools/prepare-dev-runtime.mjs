#!/usr/bin/env node
/**
 * Prepare the development backend runtime layout for DshDesktop.
 *
 * The launcher expects a runtime directory whose node_modules contains both
 * @deepseek-ai/dsh (install anchor) and @deepseek-ai/dsh-desktop-host (entry).
 * A built deepseek-harness checkout has neither at any single location, so this
 * script materializes a directory with junction links to the checkout:
 *
 *   <target>/package.json
 *   <target>/node_modules/@deepseek-ai/dsh               -> <repo>/apps/cli
 *   <target>/node_modules/@deepseek-ai/dsh-desktop-host  -> <repo>/apps/desktop-host
 *   <office-skills> (optional)                           -> <repo>/packages/skill/skill-office/assets
 *
 * The office-skills link sits next to the --primary-runtime directory passed to
 * the launcher (the host resolves office-skills as its sibling). Dependencies
 * resolve through the real paths of the links (pnpm-populated node_modules
 * inside the checkout). Run after `pnpm install && pnpm run build` in the
 * checkout.
 *
 * Usage: node tools/prepare-dev-runtime.mjs --repo <deepseek-harness> --target <dir>
 *                       [--office-skills <dir>]
 */

import { mkdirSync, existsSync, rmSync, symlinkSync, writeFileSync, statSync } from 'node:fs'
import { join } from 'node:path'

function parseArguments(argv) {
  const values = {}
  for (let index = 0; index < argv.length; index += 2) {
    const name = argv[index]
    if (!name.startsWith('--')) throw new Error(`unexpected argument ${JSON.stringify(name)}`)
    const key = name.slice(2)
    const value = argv[index + 1]
    if (value === undefined || value.startsWith('--')) throw new Error(`missing value for --${key}`)
    values[key] = value
  }
  if (values.repo === undefined || values.target === undefined) {
    throw new Error('usage: node tools/prepare-dev-runtime.mjs --repo <deepseek-harness> --target <dir>')
  }
  return values
}

function requireDirectory(path, label) {
  if (!existsSync(path)) throw new Error(`${label} directory not found: ${path}`)
  if (!statSync(path).isDirectory()) throw new Error(`${label} is not a directory: ${path}`)
}

function link(target, linkPath) {
  if (existsSync(linkPath)) rmSync(linkPath, { recursive: true, force: true })
  // junction: works on Windows without developer mode; POSIX falls back to symlink.
  try {
    symlinkSync(target, linkPath, 'junction')
  } catch {
    symlinkSync(target, linkPath, 'dir')
  }
}

const args = parseArguments(process.argv.slice(2))
const repo = args.repo
const target = args.target
requireDirectory(join(repo, 'apps', 'cli'), 'repo apps/cli')
requireDirectory(join(repo, 'apps', 'cli', 'lib', 'profile-boot.js').replace(/lib.*$/, ''), 'repo apps/cli')
requireDirectory(join(repo, 'apps', 'desktop-host'), 'repo apps/desktop-host')
const entry = join(repo, 'apps', 'desktop-host', 'lib', 'index.js')
if (!existsSync(entry)) throw new Error(`desktop host entry missing (run pnpm run build first): ${entry}`)
const anchor = join(repo, 'apps', 'cli', 'lib', 'profile-boot.js')
if (!existsSync(anchor)) throw new Error(`dsh install anchor artifact missing (run pnpm run build first): ${anchor}`)

const scope = join(target, 'node_modules', '@deepseek-ai')
mkdirSync(scope, { recursive: true })
const manifestPath = join(target, 'package.json')
if (!existsSync(manifestPath)) {
  writeFileSync(manifestPath, `${JSON.stringify({ name: 'dsh-desktop-dev-runtime', private: true }, undefined, 2)}\n`)
}
link(join(repo, 'apps', 'cli'), join(scope, 'dsh'))
link(join(repo, 'apps', 'desktop-host'), join(scope, 'dsh-desktop-host'))

if (args['office-skills'] !== undefined) {
  const officeAssets = join(repo, 'packages', 'skill', 'skill-office', 'assets')
  if (!existsSync(join(officeAssets, 'scripts', 'check_office.py'))) {
    throw new Error(`office skill assets missing: ${officeAssets}`)
  }
  link(officeAssets, args['office-skills'])
  console.log(`office skills linked: ${args['office-skills']}`)
}
console.log(`dev runtime ready: ${target}`)
