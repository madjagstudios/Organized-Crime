// Removes the OC-69 spike Arthur's one NPCData entry (id oc_release1_arthur_spike) from a
// Schedule I save's NPCs.json, out of game. This is the OC-69 spec's fallback 3: S1API offers no
// despawn and no per instance save opt out, the parked lifecycle never destroys the contact, and
// the Release build declares no Release1ArthurNpc at all, so nothing in game can clean the entry up.
//
// Usage (game closed, Steam Cloud off):
//   node tools/scripts/scrub-arthur-spike.js "<path to SaveGame_N>"
//
// What it does, in order: refuses if Schedule I is running; copies the whole save folder to
// <Saves>/<SaveGame_N>.oc73-pre-arthur-scrub-<date>.bak (refuses if that backup already exists);
// splices exactly one NPCData block out of NPCs.json by text, touching no other byte; parses the
// result and checks the entry count fell by one, the id is gone, Nell is still present, and every
// other entry is unchanged; only then writes NPCs.json back. Prints ABSENT and exits 0 if the id is
// not in the file.

const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const ID = "oc_release1_arthur_spike";
const saveDir = process.argv[2];
if (!saveDir || !fs.existsSync(path.join(saveDir, "NPCs.json"))) {
  console.error("usage: node scrub-arthur-spike.js <path to SaveGame_N containing NPCs.json>");
  process.exit(2);
}

if (process.platform === "win32") {
  const tasks = execSync("tasklist", { encoding: "utf8" });
  if (/Schedule I/i.test(tasks)) {
    console.error("Schedule I is running; close the game first.");
    process.exit(1);
  }
}

const npcsPath = path.join(saveDir, "NPCs.json");
const original = fs.readFileSync(npcsPath, "utf8");
const needle = `\\"ID\\":\\"${ID}\\"`;
const hit = original.indexOf(needle);
if (hit < 0) {
  console.log(`ABSENT: ${ID} is not in ${npcsPath}; nothing to do.`);
  process.exit(0);
}

// One backup of the whole save folder, beside the Saves folder's other .bak copies.
const stamp = new Date().toISOString().slice(0, 10).replace(/-/g, "");
const savesRoot = path.dirname(path.dirname(saveDir));
const backupDir = path.join(savesRoot, `${path.basename(saveDir)}.oc73-pre-arthur-scrub-${stamp}.bak`);
if (fs.existsSync(backupDir)) {
  console.error(`backup already exists, refusing to overwrite it: ${backupDir}`);
  process.exit(1);
}
fs.cpSync(saveDir, backupDir, { recursive: true });
const backupNpcs = fs.readFileSync(path.join(backupDir, "NPCs.json"), "utf8");
if (backupNpcs !== original) {
  console.error("backup NPCs.json does not match the original; stopping before any change.");
  process.exit(1);
}

// The game pretty prints NPCs[] with eight space indented entries; splice exactly one of them.
const open = "\n        {\n";
const close = "\n        },";
const start = original.lastIndexOf(open, hit);
const closeAt = original.indexOf(close, hit);
if (start < 0 || closeAt < 0) {
  console.error("could not find the entry boundaries; the file layout is not what this script expects.");
  process.exit(1);
}
const end = closeAt + close.length;
const block = original.slice(start, end);
if ((block.match(/"DataType": "NPCData"/g) || []).length !== 1) {
  console.error("the selected block does not hold exactly one NPCData entry; stopping.");
  process.exit(1);
}
const scrubbed = original.slice(0, start) + original.slice(end);

const before = JSON.parse(original);
const after = JSON.parse(scrubbed);
const removedIndex = before.NPCs.findIndex((n) => typeof n.BaseData === "string" && n.BaseData.includes(needle.replace(/\\/g, "")));
const remaining = before.NPCs.filter((_, i) => i !== removedIndex);
const checks = [
  [after.NPCs.length === before.NPCs.length - 1, "entry count fell by exactly one"],
  [!scrubbed.includes(ID), "the id is gone from the file"],
  [after.NPCs.some((n) => typeof n.BaseData === "string" && /nell/i.test(n.BaseData)), "Nell is still present"],
  [JSON.stringify(remaining) === JSON.stringify(after.NPCs), "every other entry is unchanged"],
];
for (const [ok, label] of checks) {
  if (!ok) {
    console.error(`check failed: ${label}; NPCs.json left untouched, backup at ${backupDir}`);
    process.exit(1);
  }
}

fs.writeFileSync(npcsPath, scrubbed);
console.log(`removed NPCs[${removedIndex}] (${block.length} bytes): ${before.NPCs[removedIndex].BaseData}`);
console.log(`NPCs: ${before.NPCs.length} -> ${after.NPCs.length}; backup: ${backupDir}`);
