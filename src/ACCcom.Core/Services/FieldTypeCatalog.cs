namespace ACCcom.Core.Services;

/// <summary>
/// The authoritative catalog of schema field types and checksum types used by
/// the schema editor (and enforced by protocol generation). Field types are
/// the legal values of <see cref="Models.FieldSchema.Type"/>; the length map
/// drives the editor's auto-length behavior. Previously duplicated between
/// SchemaEditorViewModel (FieldTypes) and FieldItemViewModel (TypeLengthMap) —
/// both now read this single source of truth.
/// </summary>
public static class FieldTypeCatalog
{
    /// <summary>Legal field types, in editor display order.</summary>
    public static readonly string[] FieldTypes =
    [
        "uint8", "uint16", "uint32", "int8", "int16", "int32", "float", "double",
        "string", "hex", "bcd", "enum", "bitfield"
    ];

    /// <summary>Legal checksum types for a protocol schema.</summary>
    public static readonly string[] ChecksumTypes =
    [
        "none", "xor8", "sum8", "crc16"
    ];

    /// <summary>Fixed byte length per field type. Types without a real fixed
    /// size (string/enum/bitfield) still map to 1 — the editor treats them as
    /// a 1-byte default that the user may override, mirroring the original
    /// TypeLengthMap behavior.</summary>
    private static readonly Dictionary<string, int> TypeLengthMap = new()
    {
        ["uint8"] = 1, ["int8"] = 1, ["bcd"] = 1, ["hex"] = 1,
        ["string"] = 1, ["enum"] = 1, ["bitfield"] = 1,
        ["uint16"] = 2, ["int16"] = 2,
        ["uint32"] = 4, ["int32"] = 4, ["float"] = 4,
        ["double"] = 8
    };

    /// <summary>Returns true when <paramref name="type"/> is a legal field type.</summary>
    public static bool IsValidFieldType(string type)
        => Array.IndexOf(FieldTypes, type) >= 0;

    /// <summary>Returns true when <paramref name="type"/> is a legal checksum type.</summary>
    public static bool IsValidChecksumType(string type)
        => Array.IndexOf(ChecksumTypes, type) >= 0;

    /// <summary>Returns the fixed byte length for <paramref name="type"/>, or
    /// null when the type has no fixed length or is unknown/null.</summary>
    public static int? GetFixedLength(string? type)
        => type != null && TypeLengthMap.TryGetValue(type, out var len) ? len : null;
}
