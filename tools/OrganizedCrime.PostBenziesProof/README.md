# OC-50 post-Benzies restart proof

This is a temporary, read-only MelonLoader proof assembly for owner `SaveGame_5`. It exercises the exact OC-50 `Release1PostBenziesUnlockReader` after a full Schedule I process restart.

It does not mutate the save, complete a quest, publish an OC-49 transition, queue a call, unlock the HQ, add a key binding, or prove that natural gameplay produced the `Defeated` state.

## Build

```powershell
dotnet restore tools\OrganizedCrime.PostBenziesProof\OrganizedCrime.PostBenziesProof.csproj
dotnet build tools\OrganizedCrime.PostBenziesProof\OrganizedCrime.PostBenziesProof.csproj -c Release --no-restore
```

The matching pair is emitted under `artifacts/oc-50-post-benzies-proof/Release/`:

- `OrganizedCrime.dll`
- `OrganizedCrime.PostBenziesProof.dll`

## Owner protocol

Run only while starting from a fully closed game and only with owner approval:

1. Preserve the current `SaveGame_5/Cartel.json` status and hash.
2. Back up the installed Organized Crime DLL, then install the matching pair above.
3. Start Schedule I and load single-player `SaveGame_5`.
4. Wait for one `[OC-50 Proof]` terminal JSON receipt in the MelonLoader log.
5. Exit the game fully.
6. Remove `OrganizedCrime.PostBenziesProof.dll`, restore the prior Organized Crime DLL, and re-read `Cartel.json`.

PASS requires `Classification=Pass`, `ReadStatus=Unlocked`, `CartelStatus=Defeated`, `SaveName=SaveGame_5`, and `NaturalTransitionProven=false`. A readiness timeout is INCONCLUSIVE. Wrong save, non-host, multiplayer, identity mismatch, locked state, or a fault is STOP.
