# Nightclub door/menu timing diagnostic

OC-47 adds one owner-triggered diagnostic run to the existing observational
Property Probe. It does not implement the Nightclub HQ, an unlock, a fallback
prompt, menu injection, teleportation, storage, persistence, or a lifecycle
adapter.

## Owner protocol

Use a disposable single-player save in a clean process. Stand at the approved
Nightclub shell before pressing `F5`. The harness admits exactly one run only
when FishNet reports one server-initialized player with a connection and both
the server and client are started. `F17` is not a trigger for this harness.

After the harness logs admission, it captures the exact validated candidates
and temporarily subscribes to each candidate's native `IntObj.onInteractStart`
event. Manually interact with the exact door twice, without automated input,
then wait twelve seconds. The callback records only captured door
identity/path, monotonic timestamp, ordinal, and duplicate count. It does not
call a door, interaction, menu, scene, save, ownership, or storage method.
The harness separately polls the loaded `NPCSummonMenu` state; an observed menu
is scene-global and not proven door-caused. Callback ownership and exact
callback order remain `UNKNOWN`.

The sole authorized runtime mutation is temporary listener registration. A
0.25-second **per-door** monotonic debounce collapses multiple callbacks from
one physical interaction; it is not a gameplay cooldown. A later interaction,
including one on another exact candidate, remains observable. Every registered
listener is removed exactly once on normal completion, STOP, INCONCLUSIVE,
scene change, authority drift, exception, disposal/application quit, or a
partial-subscription rollback. Any admission, identity, listener-setup, scene,
authority, or exception failure is a hard STOP.

The diagnostic writes only these files under MelonLoader UserData:

```text
<UserData>/CriminalEmpireProbe/nightclub-door-timing.txt
<UserData>/CriminalEmpireProbe/nightclub-door-timing.json
```

These files overwrite on each run. Copy both receipts before starting a
replacement run. Unity InstanceIds within them are session-local and are not
durable identities after a restart; `NotObserved` is not proof of absence in
the bounded window.

## Decision rules

`PASS` requires a unique approved shell, at least one exact-shell
`StaticDoor`, one selected door interaction, a bounded menu result (opened and
closed or not observed), two separately observed manual interactions,
teardown, host authority, and unchanged identity invariants.

`INCONCLUSIVE` means the observation surface or protocol evidence was missing:
for example, no exact selected door, no menu result, no second interaction,
or an incomplete timing window. It is not evidence that the behavior is
absent.

`STOP` means a safety boundary failed: wrong authority or authority drift,
duplicate/missing shell validation, listener setup/cleanup failure, scene
change, exception, identity drift, a mutation marker, or an unauthorized hook.
Do not proceed to HQ implementation from a `STOP`.

The report separates runtime `FACT`-style observations from the
`INTERPRETATION` that callback order remains unknown. A menu opening is a
vanilla behavior observation; it is not treated as a harness mutation.

## Result

The corrected bounded run captured the same exact door twice, at 4.269 s and
8.870 s, with the duplicate-interaction check exercised, listener teardown
completed, vanilla invariants unchanged, and no mutation, Harmony patch, scene
change, or PropertyProbe error recorded. The formal receipt is INCONCLUSIVE
only because no `NPCSummonMenu` surface appeared, so callback order is still
unknown; the exact-door event seam itself passed. The mod's own `Enter
Syndicate HQ` prompt is the primary presentation and does not depend on that
menu.
