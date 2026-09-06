# OC-44 Typed Quest Lifecycle Proof

This is a game-free, isolated contract evaluator for OC-44. It does not load
Schedule I, reference S1API/S1MAPI, install a DLL, invoke a native member, or
write a save. The tests replay the minimum observations required for a later
owner-approved disposable-save proof:

`Launch A: create → begin → objective → active save → exit`

`Launch B: full reload/reconstruct once → owner terminal → normal save → exit`

`Launch C: full reload → completed Quest absent/no replay → owner evaluation`

The OC mission key and attempt are the sole progression, Standing, reward, and
correlation authority. Native Quest state is presentation/world state only. An
opaque native reference may change on reconstruction; an empty active-reload
reference is `INCONCLUSIVE`. Launch B must restore title/objective state and
host manager membership sufficiently to correlate the same OC mission key.
Launch A Save and Launch B reconstruction must report the exact enum text
`Active` for both the wrapper Quest state and the sole native objective state.
Objective restoration is observed from wrapper `S1Quest` → native `Entries`
property → each native entry `State` property. The installed IL2CPP `Entries`
projection is read through its `Count` and `Item[int]` properties; it is not a
managed `IEnumerable`. The reader is capped at 64 entries and fails closed on a
partial, throwing, negative, excessive, or null collection result. Managed
`IEnumerable` is used only when the Count+Item surface is wholly absent. A sole
objective state exists only when exactly one entry is readable. Wrapper
`QuestEntries` is not authoritative restoration evidence and is not assumed to
be repopulated.
Launch C absence is a positive `CompletedQuestAbsentNoReplay` observation, not
generic missing native state. Duplicate native creation, terminal delivery,
receipts, reconstruction, or completed-Quest reappearance is `STOP`.
Client/UI mutation or authority drift is `STOP`.

Every JSONL observation carries one durable run-chain id and one process-session
id. PASS requires three distinct process sessions on the same chain, both reload
records to originate at actual `OnLoadComplete` boundaries, and the Launch C
record to belong to the process in which F12 is pressed. The first
`OnLoadComplete` owns that process boundary; subsequent callbacks in the same
process are accepted typed no-ops and cannot record or advance an epoch. They
therefore cannot masquerade as Launch B/C. A stale completed ledger opened in
an unrelated process or missing legacy markers cannot PASS. Only the Terminal
observation may contain the proof-local Standing/reward receipt pair; raw
receipt occurrences must be exactly one each.

The `PASS` result is a deterministic contract classification, not live game
evidence. The owner protocol and static evidence record remain required before
any production integration.

## Owner-only diagnostic protocol

This artifact is a disposable MelonLoader diagnostic DLL. It is not part of
`tools/OrganizedCrime`, does not issue OC Standing/reward/story writes, and does
not force a save. Use it only in one owner-authorized single-player save with
one host player:

1. Before Launch A, fully exit the game and remove only
   `Application.persistentDataPath\oc44-typed-quest-proof.jsonl` and
   `oc44-typed-quest-proof-guid.json` from any prior run. Do not modify the
   archived OC-44 evidence folder. Wait for `OnLoadComplete`; press `F7` only
   for optional GUID evidence. The valid diagnostic GUID is never authoritative.
2. Launch A: press `F8` once, `F9` once, and `F10` once. Use the normal save
   flow while the Quest is active, confirm `Save`, and fully exit. Do not press
   `F11` in Launch A.
3. Launch B: load the same save and wait for exactly one active reconstruction.
   Confirm the same mission key/attempt, wrapper Quest state `Active`, the sole
   native `S1Quest.Entries` objective state `Active`, restored title, and manager
   membership. Do not use wrapper `QuestEntries` as restoration evidence. The
   owner then presses `F11` once, uses the normal save flow, and fully exits.
4. Launch C: load the same save and wait for the completed presentation Quest
   to be absent. The runtime records positive no-replay evidence only when the
   mission-key lookup completes and finds no Quest; lookup failure is
   `INCONCLUSIVE`. Press `F12` for the only final decision; reload callbacks
   record/log evidence but do not evaluate automatically.
5. Missing active reconstruction remains `INCONCLUSIVE`. Any duplicate native
   creation, reconstruction, terminal delivery, receipt, authority drift,
   forbidden mutation, or completed-Quest reappearance is `STOP`. Routine
   same-process duplicate load callbacks are accepted no-ops: they do not
   invalidate the first boundary, write a second record, or advance an epoch.
   A stale completed sidecar, missing run/process marker, or receipt outside
   Terminal cannot PASS. The runtime does not retry failed native actions or
   mutate production state.

The MelonLoader log emits bounded event lines. Lifecycle observations are
written to `Application.persistentDataPath\oc44-typed-quest-proof.jsonl`; the
optional GUID observation is written to
`Application.persistentDataPath\oc44-typed-quest-proof-guid.json`. These are
proof-local artifacts, not game-save data. For a rerun, verify the disposable
save is restored before Launch A and clean both proof-local files again.
Missing S1API/native state is reported as `INCONCLUSIVE`; client/UI mutation
or authority drift is `STOP`.

## Build without installing

Use the installed Schedule I assemblies only as compile-time references:

```powershell
dotnet build tools/OrganizedCrime.QuestLifecycleProof/OrganizedCrime.QuestLifecycleProof.csproj --no-restore -c Debug
dotnet build tools/OrganizedCrime.QuestLifecycleProof/OrganizedCrime.QuestLifecycleProof.csproj --no-restore -c Release
dotnet test tests/OrganizedCrime.QuestLifecycleProof.Tests/OrganizedCrime.QuestLifecycleProof.Tests.csproj --no-restore -v minimal
```

The DLLs land under `artifacts/oc-44-quest-lifecycle-proof/{Debug,Release}`.
Do not copy either DLL into `Mods` and do not launch the game as part of the
build/test verification.
