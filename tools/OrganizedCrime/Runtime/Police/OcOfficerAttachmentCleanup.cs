using Il2CppScheduleOne.Core.Avatar;
using Il2CppScheduleOne.Police;
using OrganizedCrime.Runtime.Platform;
using UnityEngine;
using Avatar = Il2CppScheduleOne.AvatarFramework.Avatar;
using Object = UnityEngine.Object;

namespace OrganizedCrime.Runtime.Police;

internal static class OcOfficerAttachmentCleanup
{
    // Only called with the fresh Object.Instantiate result, before activation.
    // Never repair Parent: the clone appearance creates its own initialized set.
    internal static int RemoveBeforeActivation(GameObject root, PoliceOfficer officer)
    {
        if (!UnityLife.IsAlive(root) || root.activeInHierarchy || root.activeSelf)
            throw new InvalidOperationException("Attachment cleanup requires an inactive clone.");
        var avatars = officer.GetComponentsInChildren<Avatar>(true);
        if (avatars.Length != 1 || !UnityLife.IsAlive(avatars[0]) ||
            !avatars[0].transform.IsChildOf(root.transform))
            throw new InvalidOperationException("Attachment cleanup requires one owned clone avatar.");
        var stale = Select(avatars[0]);
        // Snapshot first: immediate destruction mutates the transform hierarchy.
        foreach (var attachment in stale)
        {
            if (!UnityLife.IsAlive(attachment)) continue;
            Object.DestroyImmediate(attachment.gameObject);
        }
        return stale.Count;
    }

    internal static int Count(Avatar avatar) => Select(avatar).Count;

    private static IReadOnlyList<AvatarAttachment> Select(Avatar avatar)
    {
        var attachments = new List<AvatarAttachment>();
        foreach (var component in avatar.GetComponentsInChildren<AvatarAttachment>(true))
        {
            if (!UnityLife.IsAlive(component)) continue;
            var attachment = component.TryCast<AvatarAttachment>();
            if (UnityLife.IsAlive(attachment)) attachments.Add(attachment!);
        }
        return OcOfficerStaleAttachments.Select(attachments,
            a => a.transform.IsChildOf(avatar.transform),
            a => UnityLife.IsAlive(a),
            a => UnityLife.IsAlive(a.Parent));
    }
}
