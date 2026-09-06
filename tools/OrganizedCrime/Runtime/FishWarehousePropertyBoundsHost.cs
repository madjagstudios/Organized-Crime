using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Model;
using UnityEngine;

namespace OrganizedCrime.Runtime;

public sealed class FishWarehousePropertyBoundsHost
{
    private Property? _property;
    private GameObject? _root;
    private BoxCollider? _collider;
    private GameObject? _previousBoundingBox;
    private Il2CppReferenceArray<BoxCollider>? _previousColliders;

    public bool IsReady => _property is not null && _root is not null && _collider is not null;

    public bool TryAttach(Property property, Action<string>? log = null)
    {
        if (IsReady)
            return true;
        if (property is null)
            return false;

        GameObject? staging = null;
        try
        {
            staging = new GameObject("OC_FishWarehouse_PropertyBounds");
            staging.transform.SetParent(property.transform, false);
            staging.transform.localPosition = new Vector3(
                (FishWarehousePropertyBoundsDefinition.MinX + FishWarehousePropertyBoundsDefinition.MaxX) / 2f,
                (FishWarehousePropertyBoundsDefinition.MinY + FishWarehousePropertyBoundsDefinition.MaxY) / 2f,
                (FishWarehousePropertyBoundsDefinition.MinZ + FishWarehousePropertyBoundsDefinition.MaxZ) / 2f);
            staging.transform.localRotation = Quaternion.identity;
            staging.transform.localScale = Vector3.one;

            var collider = staging.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(
                FishWarehousePropertyBoundsDefinition.MaxX - FishWarehousePropertyBoundsDefinition.MinX,
                FishWarehousePropertyBoundsDefinition.MaxY - FishWarehousePropertyBoundsDefinition.MinY,
                FishWarehousePropertyBoundsDefinition.MaxZ - FishWarehousePropertyBoundsDefinition.MinZ);
            collider.isTrigger = true;

            _previousBoundingBox = property.BoundingBox;
            _previousColliders = property.propertyBoundsColliders;
            var colliders = new Il2CppReferenceArray<BoxCollider>(1);
            colliders[0] = collider;
            property.BoundingBox = staging;
            property.propertyBoundsColliders = colliders;

            _property = property;
            _root = staging;
            _collider = collider;
            staging = null;

            if (!EnsureActive() || property.BoundingBox != _root ||
                property.propertyBoundsColliders is null || property.propertyBoundsColliders.Length != 1)
            {
                throw new InvalidOperationException("Property did not retain the authored bounds references.");
            }

            var entryPoint = property.transform.TransformPoint(new Vector3(-10.96f, 0f, 6.15f));
            if (!property.DoBoundsContainPoint(entryPoint))
                throw new InvalidOperationException("Native Property point containment rejected the interior entry point.");

            return true;
        }
        catch (Exception ex)
        {
            if (_property is not null)
            {
                _property.BoundingBox = _previousBoundingBox;
                _property.propertyBoundsColliders = _previousColliders;
            }
            if (staging is not null)
                UnityEngine.Object.DestroyImmediate(staging);
            else if (_root is not null)
                UnityEngine.Object.DestroyImmediate(_root);

            _property = null;
            _root = null;
            _collider = null;
            _previousBoundingBox = null;
            _previousColliders = null;
            log?.Invoke($"Fish Warehouse property bounds failed safely: {ex.Message}");
            return false;
        }
    }

    public bool EnsureActive()
    {
        if (_root is null || _collider is null)
            return false;

        _root.SetActive(true);
        _collider.enabled = true;
        return FishWarehousePropertyBoundsDefinition.IsStagedAttachmentUsable(
            _root.activeSelf,
            _collider.enabled);
    }

    public void Dispose()
    {
        if (_property is not null)
        {
            if (_property.BoundingBox == _root)
                _property.BoundingBox = _previousBoundingBox;
            if (_property.propertyBoundsColliders is not null &&
                _property.propertyBoundsColliders.Length == 1 &&
                _property.propertyBoundsColliders[0] == _collider)
            {
                _property.propertyBoundsColliders = _previousColliders;
            }
        }

        if (_root is not null)
            UnityEngine.Object.DestroyImmediate(_root);

        _property = null;
        _root = null;
        _collider = null;
        _previousBoundingBox = null;
        _previousColliders = null;
    }
}
