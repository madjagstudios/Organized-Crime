using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Platform;
using UnityEngine;

namespace OrganizedCrime.Runtime.Qa;

internal sealed class FishWarehouseLampTuningQa
{
    private readonly RuntimePropertyHost host;

    internal FishWarehouseLampTuningQa(RuntimePropertyHost host)
    {
        this.host = host;
    }

    public bool TryAddLampAbovePlayer()
    {
        if (!host.LightingActive || host._property is null || host._lighting is null)
        {
            RuntimePropertyHost.Log("OC-109 lamp add refused: the room lighting is not active");
            return false;
        }

        try
        {
            if (!PlayerSingleton<PlayerMovement>.InstanceExists)
            {
                RuntimePropertyHost.Log("OC-109 lamp add refused: the local player movement was unavailable");
                return false;
            }

            var movement = PlayerSingleton<PlayerMovement>.Instance;
            if (!UnityLife.IsAlive(movement))
            {
                RuntimePropertyHost.Log("OC-109 lamp add refused: the local player movement was unavailable");
                return false;
            }

            Vector3 local = host._property.transform.InverseTransformPoint(movement.transform.position);
            float x = ClampLighting(
                SnapLighting(local.x),
                -(FishWarehouseInteriorRoomDefinition.HalfWidth - FishWarehouseLightingDefinition.BarLength / 2f),
                FishWarehouseInteriorRoomDefinition.HalfWidth - FishWarehouseLightingDefinition.BarLength / 2f);
            float z = ClampLighting(
                SnapLighting(local.z),
                -(FishWarehouseInteriorRoomDefinition.HalfDepth - FishWarehouseLightingDefinition.BarWidth / 2f),
                FishWarehouseInteriorRoomDefinition.HalfDepth - FishWarehouseLightingDefinition.BarWidth / 2f);
            float intensity = host._lighting.LampCount == 0
                ? FishWarehouseLightingDefinition.DefaultIntensity
                : host._lighting.CurrentLayout[^1].Intensity;
            var spec = new FishWarehouseLightingLamp(
                x,
                z,
                FishWarehouseLightingDefinition.DefaultHeight,
                0,
                intensity,
                FishWarehouseLightingDefinition.DefaultRange);
            if (!host._lighting.TryAddLamp(spec, out int index))
            {
                RuntimePropertyHost.Log("OC-109 lamp add refused: the lamp could not be built");
                return false;
            }

            RuntimePropertyHost.Log(FormattableString.Invariant(
                $"OC-109 added lamp {index} x={x:0.00} z={z:0.00} y={spec.HeightY:0.00} yaw=0 intensity={intensity:0.00} (lamps={host._lighting.LampCount})"));
            return true;
        }
        catch (Exception ex)
        {
            RuntimePropertyHost.Log($"OC-109 lamp add refused: {ex.Message}");
            return false;
        }
    }

    public bool TryRemoveMostRecentLamp()
    {
        if (!host.LightingActive || host._lighting is null)
        {
            RuntimePropertyHost.Log("OC-109 lamp remove refused: the room lighting is not active");
            return false;
        }

        try
        {
            if (!host._lighting.TryRemoveMostRecent(out int removedIndex))
            {
                RuntimePropertyHost.Log("OC-109 lamp remove refused: there is no lamp to remove");
                return false;
            }

            RuntimePropertyHost.Log(FormattableString.Invariant($"OC-109 removed lamp {removedIndex} (lamps={host._lighting.LampCount})"));
            return true;
        }
        catch (Exception ex)
        {
            RuntimePropertyHost.Log($"OC-109 lamp remove refused: {ex.Message}");
            return false;
        }
    }

    public bool TryAdjustMostRecentLampHeight(float delta)
    {
        if (!host.LightingActive || host._lighting is null)
        {
            RuntimePropertyHost.Log("OC-109 lamp height refused: the room lighting is not active");
            return false;
        }

        try
        {
            if (!host._lighting.TryAdjustMostRecentHeight(delta, out float newHeight))
            {
                RuntimePropertyHost.Log("OC-109 lamp height refused: there is no lamp to adjust");
                return false;
            }

            RuntimePropertyHost.Log(FormattableString.Invariant($"OC-109 lamp {host._lighting.LampCount - 1} height {newHeight:0.00}"));
            return true;
        }
        catch (Exception ex)
        {
            RuntimePropertyHost.Log($"OC-109 lamp height refused: {ex.Message}");
            return false;
        }
    }

    public bool TryAdjustAllLampIntensity(float delta)
    {
        if (!host.LightingActive || host._lighting is null)
        {
            RuntimePropertyHost.Log("OC-109 lamp intensity refused: the room lighting is not active");
            return false;
        }

        try
        {
            if (!host._lighting.TryAdjustAllIntensity(delta, out float newIntensity))
            {
                RuntimePropertyHost.Log("OC-109 lamp intensity refused: there is no lamp to adjust");
                return false;
            }

            RuntimePropertyHost.Log(FormattableString.Invariant($"OC-109 lamp intensity {newIntensity:0.00} on {host._lighting.LampCount} lamps"));
            return true;
        }
        catch (Exception ex)
        {
            RuntimePropertyHost.Log($"OC-109 lamp intensity refused: {ex.Message}");
            return false;
        }
    }

    public bool TryToggleMostRecentLampYaw()
    {
        if (!host.LightingActive || host._lighting is null)
        {
            RuntimePropertyHost.Log("OC-109 lamp yaw refused: the room lighting is not active");
            return false;
        }

        try
        {
            if (!host._lighting.TryToggleMostRecentYaw(out int newYaw))
            {
                RuntimePropertyHost.Log("OC-109 lamp yaw refused: there is no lamp to rotate");
                return false;
            }

            RuntimePropertyHost.Log(FormattableString.Invariant($"OC-109 lamp {host._lighting.LampCount - 1} yaw {newYaw}"));
            return true;
        }
        catch (Exception ex)
        {
            RuntimePropertyHost.Log($"OC-109 lamp yaw refused: {ex.Message}");
            return false;
        }
    }

    public bool TryLogLampLayout()
    {
        if (!host.LightingActive || host._lighting is null)
        {
            RuntimePropertyHost.Log("OC-109 lamp layout refused: the room lighting is not active");
            return false;
        }

        try
        {
            IReadOnlyList<FishWarehouseLightingLamp> layout = host._lighting.CurrentLayout;
            RuntimePropertyHost.Log(FormattableString.Invariant($"OC-109 lamps={layout.Count}"));
            for (var i = 0; i < layout.Count; i++)
            {
                FishWarehouseLightingLamp lamp = layout[i];
                RuntimePropertyHost.Log(FormattableString.Invariant(
                    $"OC-109 lamp {i} x={lamp.X:0.00} z={lamp.Z:0.00} y={lamp.HeightY:0.00} yaw={lamp.Yaw} intensity={lamp.Intensity:0.00}"));
            }

            return true;
        }
        catch (Exception ex)
        {
            RuntimePropertyHost.Log($"OC-109 lamp layout refused: {ex.Message}");
            return false;
        }
    }

    private static float SnapLighting(float value) =>
        MathF.Round(value / FishWarehouseLightingDefinition.SnapStep) * FishWarehouseLightingDefinition.SnapStep;

    private static float ClampLighting(float value, float minimum, float maximum) =>
        MathF.Min(MathF.Max(value, minimum), maximum);
}
