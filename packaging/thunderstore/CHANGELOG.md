# Changelog

## 2.0.0

**Requires Schedule I 0.4.7 (default branch) and S1API 3.2.1.** Older S1API builds won't work.

Your 1.0 saves carry over. Chapter 3 starts on its own once you've finished Nell's six 1.0 jobs, ending with The Envelope.

**Working for the family**
- Nell now offers repeatable jobs. Each offer shows the reward and deadline before you accept, and you can turn jobs down. The jobs:
  - dead drops
  - package recovery
  - emergency supply
  - safehouse storage runs
  - cash movements
  - property shutdowns
- If you fail a job, you owe a make-good. Arthur Selby comes to collect in person, and if you stay overdue he'll beat you.
- Paid deadline extensions are available, along with trust milestones and one overnight line from Nell each night.

**The Syndicate Warehouse**
- Buy the family's warehouse on the docks, right next to the Docks Warehouse, for $300,000.
- Hire up to 20 staff, in three tiers.
- Pay the family $30,000 a week.
- Fill weekly production demands. You hand them over at the HQ family locker and get paid for each batch you take there.

**Chief Campbell and the police**
- Campbell's file-wipe price now climbs from $25,000 to $35,000, with a daily late fee. You can pay him at any time.
- Protection retainer: $30,000 a week. While you're covered:
  - no lockdowns
  - no checkpoints
  - fewer patrols
- When a lockdown has gone unpaid for 24 hours, you get a forced Wanted chase.
- Police checkpoints go up near your busiest property at Watched and Critical heat.
- Evening patrols run at Watched and Critical heat. During a lockdown, the patrols run all day.
- Crackdown sweeps: a 24-hour police surge at Critical heat, at most once a week. If you're protected, you get a day's warning and a small sweep.
- Raids: while you're away, police can seize up to a quarter of the product stored at one site. This happens at most once a week, and your heat drops to Watched afterwards. The HQ family locker, dead drops and job stashes are never touched.

**Fixes and quality**
- Nell texts you once per hand-over, not once per locker slot.
- Leaving a save with the warehouse no longer throws errors.
- Cloned police no longer render as black figures or carry stray equipment.
- Loading a save is faster than in 1.0.
- Quieter logs, with failures still clearly reported.

**Known limits**
- The shutdown job only watches staff work and machine stations. Manual pressing, packaging and laundering aren't detected.
- Chief Campbell still uses a plain messaging icon.
- Single player, as before. In a multiplayer session the mod loads, but the story and the police pressure stay off.

## 1.0.1

- Fixed: Nell's first offer now arrives within seconds of the cartel's defeat, even when it happens mid-session. Before, the check ran once at load, so a player who beat the cartel and kept playing never got the offer until they quit to the menu and loaded the save again.
- A few progression bug fixes.

## 1.0.0

First public release.

- The Cheeky Blinders story: six jobs from Nell Grey, with Arthur Selby on the failure paths.
- The hidden Syndicate headquarters with nine storage closets.
- Local heat and the Known Offender record, with a bounded police response.
- Chief Campbell: the demand to wipe your file, the rising price, and the round the clock curfew lockdown at Critical heat, lifted by payment or by cooling off.
- Nell's portrait borrowed from a vanilla face, overridable in MelonPreferences.
