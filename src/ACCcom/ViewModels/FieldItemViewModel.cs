using System.Collections.Generic;
using System.Linq;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class FieldItemViewModel : ObservableObject
{
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
                if (FieldTypeCatalog.GetFixedLength(value) is { } len)
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
        => FieldValueMap.Parse(_valuesText);

    public static string SerializeValues(Dictionary<string, string>? values)
        => FieldValueMap.Serialize(values);

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
