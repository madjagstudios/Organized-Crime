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

## Retained corrected owner result

The owner completed the corrected bounded run and retained it in a local
evidence archive. The evidence pins:

- corrected HEAD
  `f54f88f1f1cb39ac636aa26320a11178a6bd93c7`;
- installed Debug DLL SHA-256
  `E286DEA3BACC877374222C3FC2D389BBE6ED4A1221CECADBE17D211647976D75`;
- text receipt SHA-256
  `EAC64EDEB3A6AE5CF1249F70456044C49260B9064DD12B87795E6BA216715E9E`;
- JSON receipt SHA-256
  `16CE32CF4C277A478AD157CCF9A386442C87564701B23B8337D45D2D4BEFA5BB`;
- final log SHA-256
  `5AA6385A2D0E281D605FF16B2EF8B9B39574A7216D5CDC72FA40DE8E6825AA45`.

The run had `Host` authority and two exact candidates. It captured the same
exact door at `4.269s` and `8.870s`, ordinals `1` and `2`, with
`DuplicateInteractionTested: true`. Listener teardown completed, vanilla
invariants were unchanged, and no mutation, Harmony, scene change, or
PropertyProbe exception/error was recorded. The pre/post `SaveGame_4`
snapshots each contain 35 files with zero byte differences. The temporary
DLL was removed from the game and retained recoverably under the evidence
root's `removed-after-run` directory.

The formal receipt remains **INCONCLUSIVE solely because no `NPCSummonMenu`
surface was available**: `MenuOutcome: NotObserved` and
`MenuSurfaceObserved: false`. This does not prove the menu never exists, and
callback order remains unknown. The product/architecture result is **PASS for
the exact-door event seam**. Native `NPCSummonMenu` remains
unproven/unavailable and is not an R1 dependency; the OC-owned `Enter Syndicate
HQ` fallback presentation is primary.

No further OC-47 gameplay run is required. OC-47 is ready for final evidence
review and owner-authorized merge/closeout. Successor work is HQ
entry/interior implementation, not more event-seam research.

## Retention

The owner must retain the text/JSON receipts and the exact build, extraction,
index, API, DLL, and disposable-save hashes for independent review. The first
INCONCLUSIVE polling-seam run remains part of the history; the corrected
rerun does not overwrite it. Repository closeout does not authorize another
installation/game launch, save mutation, merge, push, Jira close, or
branch/worktree removal.
