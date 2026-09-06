using System.Text;
using System.Text.Json;

namespace OrganizedCrime.PoliceDispatchProof;

public sealed class BoundedProofLog
{
    private readonly int _maxRows;
    private readonly int _maxBytes;
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly List<SequencedDispatchProofLogRow> _rows = new();
    private int _bytes;

    public BoundedProofLog(int maxRows, int maxBytes)
    {
        if (maxRows <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRows));
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        _maxRows = maxRows;
        _maxBytes = maxBytes;
    }

    public IReadOnlyList<SequencedDispatchProofLogRow> Rows => _rows;

    public int AcceptedRowCount => _rows.Count;

    public bool Truncated { get; private set; }

    public bool TryAppend(DispatchProofLogRow row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.Key))
            return false;
        if (!_keys.Add(row.Key))
            return false;
        if (_rows.Count >= _maxRows)
        {
            _keys.Remove(row.Key);
            Truncated = true;
            return false;
        }

        var payloadBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(row));
        if (_bytes + payloadBytes > _maxBytes)
        {
            _keys.Remove(row.Key);
            Truncated = true;
            return false;
        }

        _rows.Add(new(_rows.Count + 1L, row.Key, row.Event ?? string.Empty));
        _bytes += payloadBytes;
        return true;
    }
}
