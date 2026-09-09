# Organized Crime, Release 1 (1.0.0)

## What this release contains

- **The Vacuum arc, six missions, all proven live** across four owner sessions (2026-09-03 to
  2026-09-06): Small Courtesy, Wrong Address, A Room With No Name, Short Notice, Keep the Lights Off
  (rebuilt onto a real production census, OC-65), The Envelope (redesigned onto an HQ closet deposit,
  OC-61).
- **Nell Grey**, the primary contact; her portrait borrows the vanilla NPC `lily_turner`'s icon,
  ratified by the owner on 2026-09-06 and overridable through `OrganizedCrime.NellPortraitNpcId`.
- **Arthur Selby**, the enforcer, reachable by phone on failure paths (OC-43). The in-person spike
  (OC-69) is research only; compiled out of this release entirely (OC-70).
- **Chief Campbell** (OC-73), the police made visible: he messages at the first arrest and at every
  Watched and Critical crossing, demands 15000 to wipe the arrest record, raises it to 20000 and then
  25000 on a decline, and at Critical announces a lockdown before a round the clock curfew engages on
  the game's own curfew seam; the lockdown lifts on payment or once heat cools below Watched. Proven
  live 2026-09-06 after two same day fixes. His messaging icon is S1API's default for now (OC-78).
- **The Syndicate HQ**: a hidden pocket interior, nine native 20-slot storage closets used by the
  hold-room and cash-gate missions above.
- **Local Heat and Known Offender**: proven live; a bounded native law response adapter (OC-39/41)
  dispatches a two-officer response at a watched or critical tier.
- **Performance, measured 2026-09-06**: first load 3.1 s (the one door scene scan 2.1 s, the HQ
  interior build 0.9 s), save 0.65 s (the interior build again). OC-70 removed every rescan it
  targeted; the interior rebuild on each boundary is now the whole cost and is tracked as OC-76. Both
  boundaries felt smooth in the owner's run.

## Known follow-ups

- **OC-71**: packaging is a manual step this release; the Thunderstore and Nexus listings are built by
  hand from `tools/OrganizedCrime/bin/Release/net6.0/OrganizedCrime.dll`.
- **OC-72**: the Arthur spike's open questions if ever promoted out of research: a bounded provoke
  visit (the 2026-09-05 run needed a NEEDS OPTION B cutoff; F5 lets a fight run to the player's death
  with no bound) and a real appearance (he spawns naked; an avatar donor needs the game's own registry).
- **OC-75**: pay the Chief without waiting for the next arrest. **OC-76**: retain the HQ interior
  across boundaries. **OC-77**: tidy-ups from the OC-73 fix reviews. **OC-78**: a real icon for the
  Chief.
- **OC-74** (under OC-67) and **OC-68**: the forced Wanted rungs and Federal Heat, Release 2.

## Fixed on main since 1.0.0, not yet packaged

- **OC-79** (2026-09-09, from a Nexus report): the Nell intro never arrived when the cartel was
  defeated mid-session. The eligibility check ran once, about a second after load, and latched off on
  a not-yet-defeated cartel; only quitting to the menu and reloading re-armed it, and sleeping did not
  help because S1API's `OnLoadComplete` is bound to `LoadManager`, not `SaveManager`. A `Locked`
  result now starts a 5 s watch for the rest of the session. Proven live on slot 1: watch line 80 s
  after load, offer without a reload. Whether a
  natural Benzies defeat completes `Quest_DefeatCartel` at all is OC-80, still open.

## Not in this release

Every item the 2026-09-05 feature inventory (`docs/reviews/2026-09-05-feature-inventory.md`) marks
Designed only or Partly built: Chapters 3 through 6, all three story endings, property raids, laying
low, police corruption, Federal Heat, and syndicate services consuming capacity.
