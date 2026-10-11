using Il2CppFishNet;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Actions;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.NPCs.Framework;
using Il2CppScheduleOne.NPCs.Responses;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.VoiceOver;
using OrganizedCrime.Core.Time;
using OrganizedCrime.Runtime.Platform;
using UnityEngine;

namespace OrganizedCrime.Runtime.Police;

internal sealed class OcOfficerWorldContext
{
    internal readonly Action<string> _warn;
    internal bool _dialogueWarned;
    internal OfficerTemplate? _template;
    internal long? _stableRoot;
    internal string? _stableId;
    internal long _stableSinceMs;
    internal string? _faultedSourceId;
    internal readonly HashSet<long> _ownPointers = new();
    internal readonly Dictionary<long, string> _ownCloneIds = new();
    internal readonly Dictionary<long, OcOfficerCloneRef> _ownClones = new();
    internal readonly Dictionary<long, GameObject> _ownCloneRoots = new();
    internal readonly OcOfficerGuidOwnership _ownGuids = new();
    internal int _serial;

    internal OcOfficerWorldContext(Action<string>? warn = null)
    {
        _warn = warn ?? (_ => { });
    }

    internal bool IsHost => FishNetHostGate.IsHostNowDoubleRead();

    internal sealed class OfficerTemplate
    {
        internal OfficerTemplate(GameObject root, PoliceOfficer officer, NetworkObject network, ushort prefabId, ushort collectionId, string templateId, string sourceId)
        {
            Root = root;
            Officer = officer;
            Network = network;
            PrefabId = prefabId;
            CollectionId = collectionId;
            TemplateId = templateId;
            SourceId = sourceId;
        }

        internal GameObject Root { get; }
        internal PoliceOfficer Officer { get; }
        internal NetworkObject Network { get; }
        internal ushort PrefabId { get; }
        internal ushort CollectionId { get; }
        internal string TemplateId { get; }
        internal string SourceId { get; }
    }
}
