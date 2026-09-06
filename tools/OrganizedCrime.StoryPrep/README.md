# OrganizedCrime.StoryPrep

An owner tool for preparing the Release 1 story sidecar (`release1-story.json`) at a chosen point
in the arc, so a later mission can be tested without playing the earlier ones by hand.

It does the same validated Accepted, Activated, Completed mission transitions the in-game F8 owner
QA key (`Release1StoryOwnerQaRecognitionHarness`) walks through, but it stops right after the
mission you choose instead of continuing on to full story recognition. The next mission in the arc
is left exactly where a normal completion leaves it (Offered, if there is one), so the game presents
it the next time this sidecar is loaded.

This tool never passes a recognition receipt, so it can never produce a recognized story. Because of
that, `release1.the-envelope` (the final mission) cannot be used with `--through`: completing it is
the one transition in the whole story engine that requires a recognition receipt, so the tool
rejects that request up front rather than failing partway through.

## Before you run this

- Close Schedule I completely. This tool writes the sidecar file directly on disk; a running game
  can overwrite that write on its own next save.
- Turn Steam Cloud saves OFF for Schedule I first. Cloud sync can silently restore the sidecar (and
  its save file siblings) back over what this tool wrote.

## Usage

```
OrganizedCrime.StoryPrep --sidecar <path to release1-story.json> --through <missionKey|none>
OrganizedCrime.StoryPrep --list
```

Options:

- `--sidecar <path>` Path to the `release1-story.json` sidecar to prepare. If it exists, it is read
  first, and a timestamped backup (`release1-story.json.<yyyyMMdd-HHmmss>.bak`) is written beside it
  before it is overwritten. If the existing sidecar fails to decode, the tool stops and explains why
  rather than overwriting it.
- `--through <missionKey|none>` How far into the arc to prepare the story. Use one of the
  `Release1MissionCatalog` keys, in arc order:
  - `release1.small-courtesy`
  - `release1.wrong-address`
  - `release1.room-with-no-name`
  - `release1.short-notice`
  - `release1.keep-the-lights-off`
  - `none` accepts only the Release 1 relationship (the intro), with no mission touched.
  - `release1.the-envelope` is rejected; see above.
- `--player <steamId>` Overrides the player id used to build the story. Defaults to the existing
  sidecar's `story.playerId`. Required if the sidecar does not exist yet.
- `--list` Prints the mission keys in arc order and exits.

Example, preparing the sidecar for the owner's Steam id through Small Courtesy:

```
OrganizedCrime.StoryPrep --sidecar "C:\Users\<you>\AppData\LocalLow\TVGS\Schedule I\Saves\<steamId>\SaveGame_5\OrganizedCrime\release1-story.json" --through release1.small-courtesy
```

After writing, the tool reads the sidecar back through the same codec the runtime uses and prints a
summary: schema version, player id, standing, whether Release 1 is recognized, and every mission key
with its current state. It exits with a non-zero code on any failure, including a codec error on the
existing sidecar, an unknown mission key, or a write failure.
