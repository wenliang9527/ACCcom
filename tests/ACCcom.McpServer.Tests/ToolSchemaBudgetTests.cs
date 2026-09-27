using System.ComponentModel;
using System.Reflection;
using ACCcom.McpServer.Tools;
using ModelContextProtocol.Server;

namespace ACCcom.McpServer.Tests;

/// <summary>
/// Guards the token budget of the MCP tool schema. Tool + parameter
/// descriptions are re-sent by inline-tool hosts on every API request, so
/// description bloat is a recurring token tax. The budget is deliberately
/// loose enough for honest documentation but trips on the failure mode this
/// refactor fixed: repeating every parameter detail in the tool-level
/// description (read_data alone used to be 932 chars).
/// </summary>
public class ToolSchemaBudgetTests
{
    private static readonly MethodInfo[] Tools = typeof(SerialTools)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
        .ToArray();

    [Fact]
    public void ToolLevelDescriptions_StayWithinBudget()
    {
        int total = Tools.Sum(m => m.GetCustomAttribute<DescriptionAttribute>()?.Description?.Length ?? 0);

        Assert.Equal(10, Tools.Length); // MCP tool count must not drift silently
        Assert.True(total < 1_600,
            $"tool-level descriptions total {total} chars (budget 1600) — keep per-parameter detail in [Description] on the parameter, not repeated in the tool summary");
    }

    [Fact]
    public void ParameterDescriptions_StayWithinBudget()
    {
        int total = Tools
            .SelectMany(m => m.GetParameters())
            .Sum(p => p.GetCustomAttribute<DescriptionAttribute>()?.Description?.Length ?? 0);

        Assert.True(total < 3_200,
            $"parameter descriptions total {total} chars (budget 3200)");
    }

    [Fact]
    public void EveryTool_HasADescription()
    {
        foreach (var tool in Tools)
        {
            var desc = tool.GetCustomAttribute<DescriptionAttribute>()?.Description;
            Assert.False(string.IsNullOrWhiteSpace(desc),
                $"{tool.Name} is missing a [Description]");
        }
    }
}
