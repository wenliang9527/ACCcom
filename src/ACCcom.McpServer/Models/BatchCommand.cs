using System.Text.Json.Serialization;

namespace ACCcom.McpServer.Models;

public class BatchCommand
{
    [JsonPropertyName("data")]
    public string Data { get; set; } = "";

    [JsonPropertyName("isHex")]
    public bool IsHex { get; set; }

    [JsonPropertyName("waitMs")]
    public int WaitMs { get; set; }
}
