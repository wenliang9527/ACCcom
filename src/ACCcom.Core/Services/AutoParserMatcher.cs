using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class AutoParserMatcher
{
    private readonly Dictionary<string, ParserFingerprint> _fingerprints = new();
    private readonly object _lock = new();

    public int Count
    {
        get
        {
            lock (_lock) { return _fingerprints.Count; }
        }
    }

    public void UpdateFingerprint(string parserName, ParserFingerprint fingerprint)
    {
        lock (_lock)
        {
            _fingerprints[parserName] = fingerprint;
        }
    }

    public void RemoveFingerprint(string parserName)
    {
        lock (_lock)
        {
            _fingerprints.Remove(parserName);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _fingerprints.Clear();
        }
    }

    public string? MatchParser(byte[] data)
    {
        if (data == null || data.Length == 0)
            return null;

        lock (_lock)
        {
            if (_fingerprints.Count == 0)
                return null;

            string? bestMatch = null;
            int bestPriority = int.MinValue;

            foreach (var kv in _fingerprints)
            {
                if (kv.Value.Matches(data) && kv.Value.Priority > bestPriority)
                {
                    bestMatch = kv.Key;
                    bestPriority = kv.Value.Priority;
                }
            }

            return bestMatch;
        }
    }

    public List<string> GetAllMatches(byte[] data)
    {
        if (data == null || data.Length == 0)
            return new List<string>();

        lock (_lock)
        {
            return _fingerprints
                .Where(kv => kv.Value.Matches(data))
                .OrderByDescending(kv => kv.Value.Priority)
                .Select(kv => kv.Key)
                .ToList();
        }
    }
}
