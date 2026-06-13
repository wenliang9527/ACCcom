using System.Collections.Generic;
using System.Linq;
using ACCcom.Core.Models;

namespace ACCcom.ViewModels;

public class FieldItemViewModel : ObservableObject
{
    private static readonly Dictionary<string, int> TypeLengthMap = new()
    {
        ["uint8"] = 1, ["int8"] = 1, ["bcd"] = 1,
        ["uint16"] = 2, ["int16"] = 2,
        ["uint32"] = 4, ["int32"] = 4, ["float"] = 4,
        ["double"] = 8,
        ["hex"] = 1, ["string"] = 1, ["enum"] = 1, ["bitfield"] = 1
    };

    private string _name = "";
    private int _offset;
    private int _length = 1;
    private string _type = "uint8";
    private bool _bigEndian;
    private string _unit = "";
    private string _color = "#3478F6";
    private string _valuesText = "";
    private string _format = "";

    public string Name { get => _name; set => SetField(ref _name, value); }
    public int Offset { get => _offset; set => SetField(ref _offset, value); }
    public int Length { get => _length; set => SetField(ref _length, value); }

    public string Type
    {
        get => _type;
        set
        {
            if (SetField(ref _type, value))
            {
                if (TypeLengthMap.TryGetValue(value, out var len))
                    Length = len;
                OnPropertyChanged(nameof(IsEnumOrBitfield));
            }
        }
    }

    public bool BigEndian { get => _bigEndian; set => SetField(ref _bigEndian, value); }
    public string Unit { get => _unit; set => SetField(ref _unit, value); }
    public string Color { get => _color; set => SetField(ref _color, value); }
    public string ValuesText { get => _valuesText; set => SetField(ref _valuesText, value); }
    public string Format { get => _format; set => SetField(ref _format, value); }
    public bool IsEnumOrBitfield => _type == "enum" || _type == "bitfield";

    public Dictionary<string, string>? ParseValues()
    {
        if (string.IsNullOrWhiteSpace(_valuesText))
            return null;

        var dict = new Dictionary<string, string>();
        var pairs = _valuesText.Split(',');
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=');
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (!string.IsNullOrEmpty(key))
                    dict[key] = value;
            }
        }
        return dict.Count > 0 ? dict : null;
    }

    public static string SerializeValues(Dictionary<string, string>? values)
    {
        if (values == null || values.Count == 0)
            return "";
        return string.Join(",", values.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    public FieldSchema ToFieldSchema()
    {
        return new FieldSchema
        {
            Name = _name,
            Offset = _offset,
            Length = _length,
            Type = _type,
            Unit = string.IsNullOrEmpty(_unit) ? null : _unit,
            Color = string.IsNullOrEmpty(_color) ? null : _color,
            Values = ParseValues(),
            BigEndian = _bigEndian,
            Format = string.IsNullOrEmpty(_format) ? null : _format
        };
    }

    public static FieldItemViewModel FromFieldSchema(FieldSchema field)
    {
        return new FieldItemViewModel
        {
            Name = field.Name,
            Offset = field.Offset,
            Length = field.Length,
            Type = field.Type,
            Unit = field.Unit ?? "",
            Color = field.Color ?? "#3478F6",
            ValuesText = SerializeValues(field.Values),
            BigEndian = field.BigEndian,
            Format = field.Format ?? ""
        };
    }
}
