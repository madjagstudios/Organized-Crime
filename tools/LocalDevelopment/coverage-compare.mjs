// usage: node coverage-compare.mjs <before.json> <after.json>
import { readFileSync } from 'node:fs';
const [, , a, b] = process.argv;
if (!a || !b) { console.error('usage: coverage-compare.mjs <before.json> <after.json>'); process.exit(2); }
const before = JSON.parse(readFileSync(a, 'utf8'));
const after = JSON.parse(readFileSync(b, 'utf8'));
const lost = [];
for (const [file, entry] of Object.entries(before.files)) {
  const now = new Set(after.files[file]?.covered ?? []);
  const gone = entry.covered.filter(n => !now.has(n));
  if (gone.length) lost.push(`${file}: ${gone.join(',')}`);
}
console.log(`before ${before.covered}/${before.total}, after ${after.covered}/${after.total}`);
if (lost.length) { console.log('Newly uncovered production lines:\n' + lost.join('\n')); process.exit(1); }
console.log('No production line lost coverage.');
