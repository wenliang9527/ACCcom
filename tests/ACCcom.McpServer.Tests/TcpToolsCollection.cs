using Xunit;

namespace ACCcom.McpServer.Tests;

/// <summary>Exclusive collection for the TCP tool-layer tests — same
/// rationale as the Core suite's TcpSerialCollection: live sockets and
/// handshake timing must not join the parallel pool.</summary>
[CollectionDefinition("tcp-tools", DisableParallelization = true)]
public sealed class TcpToolsCollection
{
}
