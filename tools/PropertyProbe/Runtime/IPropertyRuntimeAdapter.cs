using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Runtime;

internal interface IPropertyRuntimeAdapter
{
    string AdapterName { get; }
    IReadOnlyList<PropertySnapshot> CaptureProperties();
}
