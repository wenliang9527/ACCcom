using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>Serializes the TCP-transport tests and keeps them from running
/// alongside the rest of the suite. These tests are timing-sensitive on both
/// sides of the wire (handshake/reconnect land on background threads) and
/// they add live sockets + polling tasks to the parallel pool — full-suite
/// runs showed rotating one-off failures in unrelated marginal tests (e.g.
/// WaitForQuiet) as well as in these tests themselves, while the suite
/// without them was green; an exclusive collection restores the proven
/// baseline and de-flakes both directions. TcpEndpointTests is pure parsing
/// with no I/O — grouped here only to keep all TCP surface in one place.</summary>
[CollectionDefinition("tcp-serial", DisableParallelization = true)]
public sealed class TcpSerialCollection
{
}
