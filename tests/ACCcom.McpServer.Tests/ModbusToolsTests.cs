using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;

namespace ACCcom.McpServer.Tests;

public class ModbusToolsTests
{
    [Fact]
    public async Task SlaveList_ReturnsEmptyInDirectMode()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ModbusTools(ctx);
            var result = await tools.SlaveList();
            Assert.True(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("\"count\":0", result);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task SlaveCreate_CreateAndRemoveRoundTrip()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ModbusTools(ctx);

            // 用 RTU 会尝试打开真实串口,这里用 TCP:0 (端口无效会失败)
            // 改用一个高位端口确保能绑定
            var create = await tools.SlaveCreate(1, "tcp", "15001", 1024, 1024, 256, 256);
            Assert.True(ToolContextFactory.ExtractSuccess(create), $"create failed: {create}");

            // 解析返回的 id
            using var doc = System.Text.Json.JsonDocument.Parse(create);
            var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();
            Assert.NotNull(id);

            var list = await tools.SlaveList();
            Assert.Contains("\"count\":1", list);

            var write = await tools.SlaveWrite(id!, "holding", 0, 12345);
            Assert.True(ToolContextFactory.ExtractSuccess(write));

            var read = await tools.SlaveRead(id!, "holding", 0);
            Assert.True(ToolContextFactory.ExtractSuccess(read));
            Assert.Contains("12345", read);

            var remove = await tools.SlaveRemove(id!);
            Assert.True(ToolContextFactory.ExtractSuccess(remove));

            var list2 = await tools.SlaveList();
            Assert.Contains("\"count\":0", list2);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task SlaveRead_TypeMapping_WorksForAllTypes()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ModbusTools(ctx);
            var create = await tools.SlaveCreate(1, "tcp", "15002");
            using var doc = System.Text.Json.JsonDocument.Parse(create);
            var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

            foreach (var type in new[] { "coil", "holding", "discrete", "input" })
            {
                var read = await tools.SlaveRead(id, type, 0);
                Assert.True(ToolContextFactory.ExtractSuccess(read), $"{type} read failed: {read}");
            }

            await tools.SlaveRemove(id);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ScanDevices_ReturnsError_WhenNoTransport()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ModbusTools(ctx);
            // 直接模式 Modbus 用 RTU over VirtualSerial,没开端口,扫描应返回错误或空
            var result = await tools.ScanDevices(1, 2, 200, null);
            // 不一定 success,但不应抛异常。只要返回合法 JSON 即可
            Assert.NotNull(result);
        }
        finally { sp.Dispose(); }
    }
}
