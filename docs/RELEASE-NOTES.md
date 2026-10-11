# Organized Crime release notes

## 2.0.0: Chapter 3

Requires Schedule I 0.4.7 (default branch) and S1API 3.2.1. Your 1.0 saves
carry over. Chapter 3 starts on its own once you've finished Nell's six 1.0
jobs, ending with The Envelope.

- **Working for the family:** Nell offers repeatable dead drops, package
  recovery, emergency supply, safehouse storage runs, cash movements and
  property shutdowns. Offers show the reward and deadline before you accept,
  and you can turn them down. Paid deadline extensions, trust milestones and
  one overnight line from Nell each night are available.
- **Arthur Selby:** failed jobs leave a make-good to pay. Arthur comes to
  collect in person, and if you stay overdue he'll beat you.
- **The Syndicate Warehouse:** buy the family's warehouse on the docks for
  $300,000, hire up to 20 staff in three tiers, and pay the family $30,000 a
  week. Fill weekly production demands at the HQ family locker and get paid
  for each batch you hand over.
- **Chief Campbell and the police:** the file-wipe price climbs from $25,000
  to $35,000 with a daily late fee, and you can pay at any time. A $30,000
  weekly protection retainer prevents lockdowns and checkpoints and reduces
  patrols. Unpaid lockdowns lead to a forced Wanted chase after 24 hours.
  Checkpoints, evening patrols, crackdown sweeps and property raids add
  pressure at Watched and Critical heat. Raids can seize up to a quarter of
  the product at one site, at most once a week; the HQ family locker, dead
  drops and job stashes are never touched.
- **Fixes and quality:** Nell texts once per hand-over, leaving a save with
  the warehouse no longer throws errors, cloned police no longer render as
  black figures or carry stray equipment, loading is faster than in 1.0,
  and logs are quieter while failures remain clearly reported.

The shutdown job pays $15,000-$20,000 to keep one operating owned property
quiet for twelve game hours, with two days to finish. Monitoring covers
employee work and chemistry stations, lab ovens, mixing stations, cauldrons
and drying racks; manual pressing, packaging and laundering are not detected.

**Known limits:** Chief Campbell still uses a plain messaging icon. In a
multiplayer session the mod loads, but the story and police pressure stay off.

**Not in this release / coming later:** a forklift and a backpack for the
warehouse, multiplayer support, and the family's later chapters.

## 1.0.1

- Nell's first offer now arrives within seconds of the cartel's defeat,
  even when it happens mid-session. Previously, players had to return to
  the menu and reload their save to receive it.
- A few progression bug fixes.

## 1.0.0

- **The Vacuum arc:** six missions from Nell Grey: Small Courtesy, Wrong
  Address, A Room With No Name, Short Notice, Keep the Lights Off and The
  Envelope. Keep the Lights Off uses a production census; The Envelope
  uses an HQ closet deposit.
- **Nell Grey:** the primary contact. Her portrait borrows the vanilla NPC
  `lily_turner`'s icon and can be overridden through
  `OrganizedCrime.NellPortraitNpcId`.
- **Arthur Selby:** the enforcer, reachable by phone on failure paths.
- **Chief Campbell:** messages at the first arrest and every Watched and
  Critical crossing. He demands $15,000 to wipe the arrest record, rising
  to $20,000 and then $25,000 on a decline. At Critical heat, he announces
  a round the clock curfew lockdown that lifts on payment or when heat
  cools below Watched.
- **The Syndicate HQ:** a hidden interior with nine native 20-slot storage
  closets for the hold-room and cash-gate missions.
- **Local Heat and Known Offender:** a bounded police response dispatches
  two officers at Watched or Critical heat.
- **Performance:** repeated scene scans were removed; the HQ interior
  still rebuilds at load and save boundaries.
