// usage: node coverage-summary.mjs <coverage.cobertura.xml> <out.json>
import { readFileSync, writeFileSync } from 'node:fs';
const [, , input, output] = process.argv;
if (!input || !output) { console.error('usage: coverage-summary.mjs <cobertura.xml> <out.json>'); process.exit(2); }
const xml = readFileSync(input, 'utf8');
const files = {};
const classRe = /<class\b[^>]*\bfilename="([^"]+)"[^>]*>([\s\S]*?)<\/class>/g;
const lineRe = /<line\b[^>]*\bnumber="(\d+)"[^>]*\bhits="(\d+)"/g;
for (const [, filename, body] of xml.matchAll(classRe)) {
  const norm = filename.replace(/\\/g, '/');
  const at = norm.indexOf('tools/OrganizedCrime/');
  if (at < 0) continue;
  const key = norm.slice(at);
  const entry = files[key] ??= { covered: new Set(), total: new Set() };
  for (const [, num, hits] of body.matchAll(lineRe)) {
    entry.total.add(Number(num));
    if (Number(hits) > 0) entry.covered.add(Number(num));
  }
}
const out = {};
let covered = 0, total = 0;
for (const [k, v] of Object.entries(files).sort()) {
  out[k] = { covered: [...v.covered].sort((a, b) => a - b), total: v.total.size };
  covered += v.covered.size; total += v.total.size;
}
writeFileSync(output, JSON.stringify({ covered, total, files: out }, null, 0));
console.log(`files ${Object.keys(out).length}, lines covered ${covered}/${total}`);
