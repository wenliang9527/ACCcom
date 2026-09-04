namespace ACCcom.Core.Models;

/// <summary>One rendered line of a compared file.</summary>
public sealed record DiffRow(string Display, bool IsDiff);
