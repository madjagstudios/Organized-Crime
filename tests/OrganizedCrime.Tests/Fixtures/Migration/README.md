# Migration fixtures

These fixtures are Organized Crime-owned sidecar JSON samples, one per available schema.

Player and Steam IDs, including receipt and correlation IDs, use fixed fake
IDs. Product IDs and names use testproductN/TestProductN, and player names use
TestPlayer. Save-folder and native snapshot SHA-256 values use fixed valid
64-character A/B hex strings. Nonpersonal game-object and drop GUIDs remain;
no game saves, native property snapshots, assets or binaries are included.

The numeric suffix after `v` in each filename is its schema version.

- `fish-warehouse-v2.json`
- `local-pressure-v1.json`
- `release1-story-v11.json`
- `release1-story-v1.json`
- `release1-story-v9.json`
- `release1-story-v10.json`
- `release1-story-v3.json`
- `chapter3-v15.json`
- `release1-story-v14.json`
- `chapter3-v18.json`
- `release1-story-v9-derived-active.json`
- `release1-story-v9-oc57-pre-callin.json`
- `release1-story-v11-oc70-gate-run.json`

release1-story-v9-derived-active.json is synthetic, derived from the sanitized
v9 sample. It rolls Keep the Lights Off back from Satisfied to Active, removes
its completion and reward, resets The Envelope to Locked and removes its
effects and receipts, and sets Standing to 60. Prior missions and their effects
remain; both assigned and unassigned employee censuses are tested.
