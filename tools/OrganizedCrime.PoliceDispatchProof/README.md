# OC-40 Police Dispatch Runtime Proof Harness

This is an isolated, owner-triggered runtime-proof artifact for the native
`PoliceStation.Dispatch` capacity/recovery question. It is deliberately not
referenced by `tools/OrganizedCrime/OrganizedCrime.csproj` and does not change
the production Organized Crime mod.

The configuration-specific build outputs are:

- `artifacts/oc-40-police-dispatch-proof/Debug/OrganizedCrime.PoliceDispatchProof.dll`
- `artifacts/oc-40-police-dispatch-proof/Release/OrganizedCrime.PoliceDispatchProof.dll`

The DLL is a prepared artifact only. It must not be copied into the live Mods
directory without a separate owner authorization for the live run.

## Static pins

- Schedule I build: `1f1e5669033c6029fb0f64ad335c39dc5125c773a051c48e540627991040990c`
- Native callable: `ScheduleOne.Map.PoliceStation::Dispatch(System.Int32,ScheduleOne.PlayerScripts.Player,ScheduleOne.Map.PoliceStation+EDispatchType,System.Boolean):System.Void`
- Call shape: `Dispatch(2, canonicalTarget, EDispatchType.UseVehicle, false)`
- S1API: 3.2.0.0; S1MAPI: 2.0.0.0; MelonLoader: 0.7.3.0; Sideload: 1.31.1.0
- MorePatrols reference artifact is excluded from the proof and must be absent
  from the live process.

The observer reads native station, officer, vehicle, target, authority, and
host-clock state. It never writes those objects. `deployedVehicles` is private
and has no usable current interop wrapper, so its value is explicitly logged as
unavailable; unavailable is never treated as zero. Player observation is named
only for the supported server-initialized target; it does not claim that a
duplicated property read proves client mutation absence.

The first startup `OnPreLoad` before the first `OnLoadComplete` is a non-terminal
load boundary and leaves the proof waiting for the post-load baseline. Any later
save/load or scene boundary remains terminal and stops the proof before mutation.

For each response, the harness retains three distinct snapshots: pre-dispatch,
immediate post-dispatch, and recovery. The dispatched station identity is
retained. Under-production is based on exact officer identities removed from
that station’s immediate pool; recovery requires those same identities to
return to that station and its available-vehicle count to return to baseline.
An untouched second station cannot satisfy recovery.

## Owner-only live protocol

1. Use a disposable single-player save. Confirm exactly one authoritative host
   player and that its `PlayerCode` matches the 17-digit account folder in the
   active `Saves/<account>/SaveGame_*` path. Do not use nearest-player or
   `Player.Local` selection.
2. Confirm the pinned game/dependency versions and confirm `MorePatrols.dll` is
   absent. If any readiness check is pending, stop and do not press a trigger.
3. Install only the prepared OC-40 DLL under an owner-approved live-run
   authorization. Start the game, load the disposable save, and retain the
   bounded OC-40 log output.
4. Wait for `baseline-ready`. Press **F9 exactly once**. This is the only first
   response trigger; the harness calls native `PoliceStation.Dispatch` once.
5. Observe the native response without changing the save, deleting objects,
   respawning officers, or calling any recovery helper. Wait at least the
   native cooldown/realistic recovery interval and until the harness reports
   `response=1; response 2 is now owner-eligible`.
6. Press **F10 exactly once**. The harness calls the same native dispatch shape
   once more. Wait for native recovery again; no third trigger exists.
7. End the session normally. Capture the bounded log and classify it as:
   `PassOptionA` only when both responses are attributed to the canonical player,
   consume two exact native officer identities at the dispatched station,
   produce a vehicle response, recover those identities and station vehicle
   capacity, and show no unsupported-player/pool/session mutation. The result
   prominently remains ordinary live-capacity evidence if natural officer
   death/re-pooling was not observed. `NeedsOptionB` means available evidence
   shows under-production or non-recovery; a first-response timeout records the
   failure and does not issue response 2. `Inconclusive` means a required
   observation was unavailable. `Stop` means an authority, identity,
   dependency, duplicate, unrelated-effect, exception, or corruption guard
   fired.

Each response has a bounded five-minute recovery decision window. The entire
proof run has an absolute fifteen-minute cap, preserving OC-39’s run bound;
five minutes is the per-response cadence for a native recovery decision, not an
unbounded wait or forced-time allowance.

## Cleanup and evidence

Do not repair the game state with custom code. If the owner approves cleanup,
remove only the temporary OC-40 DLL after the session is closed and the log is
captured; never replace `OrganizedCrime.dll` or delete a broad directory.

Record the exact game/dependency hashes, save identity (redacted as needed),
trigger timestamps, both response observations, native recovery interval,
classification, and whether `deployedVehicles` remained unavailable. A live
run result is still conditional evidence: it does not authorize production
integration or an OC-owned police implementation.
