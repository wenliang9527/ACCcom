using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class PresetManager : JsonFilePersistenceManager<SerialPreset>
{
    public static readonly string PresetsFile = Path.Combine(BaseDir, "presets.json");
    protected override string FileName => "presets.json";

    public static SerialPreset Create(string port, int baudRate, int dataBits, int stopBits, int parity, bool dtr, bool rts)
    {
        // A null/blank port would silently produce a corrupted preset named
        // "@9600" (empty Name.Port); reject it at the entry point.
        ArgumentNullException.ThrowIfNull(port);
        if (string.IsNullOrWhiteSpace(port))
            throw new ArgumentException("port must not be empty or whitespace", nameof(port));

        return new SerialPreset
        {
            Name = $"{port}@{baudRate}",
            Port = port,
            BaudRate = baudRate,
            DataBits = dataBits,
            StopBits = stopBits,
            Parity = parity,
            Dtr = dtr,
            Rts = rts
        };
    }

    public static (string Port, int BaudRate, int DataBits, int StopBits, int Parity, bool Dtr, bool Rts) GetConfig(SerialPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return (preset.Port, preset.BaudRate, preset.DataBits, preset.StopBits, preset.Parity, preset.Dtr, preset.Rts);
    }
}
