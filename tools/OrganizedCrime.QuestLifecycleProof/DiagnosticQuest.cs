using S1API.Quests;
using S1API.Quests.Identifiers;
using UnityEngine;

namespace OrganizedCrime.QuestLifecycleProof;

[QuestName("oc44.typed-quest.diagnostic")]
public sealed class DiagnosticQuest : Quest
{
    protected override string Title => "OC-44 typed Quest diagnostic";

    protected override string Description => "Disposable OC-44 lifecycle proof quest.";

    public DiagnosticQuest()
        : base()
    {
    }

    public QuestEntry AddDiagnosticEntry(string title) => AddEntry(title, (Vector3?)null);
}
