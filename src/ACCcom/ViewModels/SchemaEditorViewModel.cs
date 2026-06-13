using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class SchemaEditorViewModel : ObservableObject
{
    private readonly ParserManager _parserManager;

    private string _parserName = "NewProtocol";
    private string _description = "";
    private string _header = "AA 55";
    private string _footer = "";
    private int _minLength = 6;
    private string _checksumType = "none";
    private string _testHexInput = "";

    private string _generatedJson = "";
    private string _generatedCode = "";
    private string _parseResult = "";
    private FieldItemViewModel? _selectedField;

    public ObservableCollection<FieldItemViewModel> Fields { get; } = new();

    public string ParserName { get => _parserName; set => SetField(ref _parserName, value); }
    public string Description { get => _description; set => SetField(ref _description, value); }
    public string Header { get => _header; set => SetField(ref _header, value); }
    public string Footer { get => _footer; set => SetField(ref _footer, value); }
    public int MinLength { get => _minLength; set => SetField(ref _minLength, value); }
    public string ChecksumType { get => _checksumType; set => SetField(ref _checksumType, value); }
    public string TestHexInput { get => _testHexInput; set => SetField(ref _testHexInput, value); }

    public string GeneratedJson { get => _generatedJson; set => SetField(ref _generatedJson, value); }
    public string GeneratedCode { get => _generatedCode; set => SetField(ref _generatedCode, value); }
    public string ParseResult { get => _parseResult; set => SetField(ref _parseResult, value); }
    public FieldItemViewModel? SelectedField { get => _selectedField; set => SetField(ref _selectedField, value); }

    public ICommand AddFieldCommand { get; }
    public ICommand RemoveFieldCommand { get; }
    public ICommand MoveFieldUpCommand { get; }
    public ICommand MoveFieldDownCommand { get; }
    public ICommand GenerateCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand TestParseCommand { get; }
    public ICommand LoadSchemaTemplateCommand { get; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string[] ChecksumTypes { get; } = { "none", "xor8", "sum8", "crc16" };
    public static string[] FieldTypes { get; } = { "uint8", "uint16", "uint32", "int8", "int16", "int32", "float", "double", "string", "hex", "bcd", "enum", "bitfield" };

    public SchemaEditorViewModel(ParserManager parserManager)
    {
        _parserManager = parserManager;

        AddFieldCommand = new RelayCommand(_ => AddField());
        RemoveFieldCommand = new RelayCommand(_ => RemoveField(), _ => SelectedField != null);
        MoveFieldUpCommand = new RelayCommand(_ => MoveField(-1), _ => CanMoveUp());
        MoveFieldDownCommand = new RelayCommand(_ => MoveField(1), _ => CanMoveDown());
        GenerateCommand = new RelayCommand(_ => Generate());
        SaveCommand = new RelayCommand(_ => Save());
        TestParseCommand = new RelayCommand(_ => _ = TestParseAsync());
        LoadSchemaTemplateCommand = new RelayCommand(_ => LoadDefaultTemplate());

        Fields.CollectionChanged += OnFieldsCollectionChanged;
    }

    private void OnFieldsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (FieldItemViewModel item in e.NewItems)
                item.PropertyChanged += OnFieldPropertyChanged;
        }
        if (e.OldItems != null)
        {
            foreach (FieldItemViewModel item in e.OldItems)
                item.PropertyChanged -= OnFieldPropertyChanged;
        }
        RefreshCommands();
    }

    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is FieldItemViewModel field && e.PropertyName == nameof(field.IsEnumOrBitfield))
            RefreshCommands();
    }

    private void RefreshCommands()
    {
        CommandManager.InvalidateRequerySuggested();
    }

    private bool CanMoveUp()
    {
        if (SelectedField == null) return false;
        var idx = Fields.IndexOf(SelectedField);
        return idx > 0;
    }

    private bool CanMoveDown()
    {
        if (SelectedField == null) return false;
        var idx = Fields.IndexOf(SelectedField);
        return idx >= 0 && idx < Fields.Count - 1;
    }

    private void AddField()
    {
        var field = new FieldItemViewModel
        {
            Name = $"field{Fields.Count + 1}",
            Offset = Fields.Count > 0 ? Fields.Max(f => f.Offset + f.Length) : 0,
            Length = 1,
            Type = "uint8"
        };
        Fields.Add(field);
        SelectedField = field;
    }

    private void RemoveField()
    {
        if (SelectedField == null) return;
        var idx = Fields.IndexOf(SelectedField);
        Fields.Remove(SelectedField);
        if (idx < Fields.Count)
            SelectedField = Fields[idx];
        else if (Fields.Count > 0)
            SelectedField = Fields[^1];
        RefreshCommands();
    }

    private void MoveField(int direction)
    {
        if (SelectedField == null) return;
        var idx = Fields.IndexOf(SelectedField);
        var newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= Fields.Count) return;
        Fields.Move(idx, newIdx);
    }

    private ProtocolSchema BuildSchema()
    {
        var schema = new ProtocolSchema
        {
            Name = string.IsNullOrWhiteSpace(_parserName) ? "NewProtocol" : _parserName,
            Description = string.IsNullOrWhiteSpace(_description) ? "" : _description,
            MinLength = _minLength,
            Fields = Fields.Select(f => f.ToFieldSchema()).ToList()
        };

        var hasHeader = !string.IsNullOrWhiteSpace(_header);
        var hasFooter = !string.IsNullOrWhiteSpace(_footer);
        var hasChecksum = _checksumType != "none";

        if (hasHeader || hasFooter || hasChecksum)
        {
            schema.Frame = new FrameSchema
            {
                Header = hasHeader ? _header.Trim() : null,
                Footer = hasFooter ? _footer.Trim() : null,
                Checksum = hasChecksum ? new ChecksumSchema { Type = _checksumType } : null
            };
        }

        return schema;
    }

    private void Generate()
    {
        var schema = BuildSchema();
        var generator = new ParserGenerator();

        GeneratedJson = JsonSerializer.Serialize(schema, JsonOptions);

        try
        {
            GeneratedCode = generator.Generate(schema);
        }
        catch (Exception ex)
        {
            GeneratedCode = $"// Error: {ex.Message}";
        }
    }

    private void Save()
    {
        Generate();
        var schema = BuildSchema();
        var (success, error) = _parserManager.GenerateParser(schema);
        if (success)
            ParseResult = $"Parser '{schema.Name}' saved to parsers/ directory.";
        else
            ParseResult = $"Error: {error}";
    }

    private async System.Threading.Tasks.Task TestParseAsync()
    {
        if (string.IsNullOrWhiteSpace(_testHexInput))
        {
            ParseResult = "Please enter hex data to parse.";
            return;
        }

        Generate();

        try
        {
            var code = GeneratedCode;
            var engine = new ParserEngine();
            if (!engine.Load(code))
            {
                ParseResult = $"Compile error: {engine.LastError}";
                return;
            }

            var hex = _testHexInput.Replace(" ", "").Replace("-", "");
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            var result = await engine.ExecuteAsync(bytes, DateTime.Now);

            if (result == null || result.Count == 0)
            {
                ParseResult = engine.LastError != null ? $"Parse error: {engine.LastError}" : "No fields parsed.";
                return;
            }

            var sb = new StringBuilder();
            foreach (var field in result)
            {
                var sev = field.Severity switch
                {
                    FieldSeverity.Warning => "⚠",
                    FieldSeverity.Error => "✗",
                    _ => "✓"
                };
                sb.AppendLine($"{sev} [{field.Offset:X2}] {field.Name,-10} {field.RawHex,-8} {field.DisplayValue}");
            }
            ParseResult = sb.ToString();
        }
        catch (Exception ex)
        {
            ParseResult = $"Error: {ex.Message}";
        }
    }

    private void LoadDefaultTemplate()
    {
        ParserName = "MyProtocol";
        Description = "Custom serial protocol with header + length + command + data + checksum";
        Header = "AA 55";
        Footer = "";
        MinLength = 6;
        ChecksumType = "xor8";

        Fields.Clear();
        Fields.Add(new FieldItemViewModel
        {
            Name = "帧头", Offset = 0, Length = 2, Type = "hex",
            Color = "#22C55E"
        });
        Fields.Add(new FieldItemViewModel
        {
            Name = "长度", Offset = 2, Length = 1, Type = "uint8",
            Color = "#3478F6"
        });
        Fields.Add(new FieldItemViewModel
        {
            Name = "命令码", Offset = 3, Length = 1, Type = "enum",
            ValuesText = "0x01=读取,0x02=写入,0x03=状态",
            Color = "#F59E0B"
        });
        Fields.Add(new FieldItemViewModel
        {
            Name = "数据", Offset = 4, Length = 1, Type = "uint8",
            Unit = "", Color = "#3478F6"
        });

        Generate();
    }
}
