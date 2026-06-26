using System.Buffers;
using System.Net;
using System.Net.Sockets;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public enum NetworkProtocol
{
    TCP,
    UDP
}

public class NetworkBridgeService : IDisposable
{
    private TcpClient? _tcpClient;
    private UdpClient? _udpClient;
    private NetworkStream? _tcpStream;
    private CancellationTokenSource? _receiveCts;
    private readonly object _lock = new();
    private bool _disposed;
    private int _rxEntryId;
    private int _txEntryId;

    public bool IsConnected { get; private set; }
    public NetworkProtocol Protocol { get; private set; }
    public string? Host { get; private set; }
    public int Port { get; private set; }

    public event Action<LogEntry>? OnDataReceived;
    public event Action<string>? OnError;
    public event Action? OnDisconnected;

    public async Task<bool> ConnectTcp(string host, int port)
    {
        if (IsConnected) return true;

        try
        {
            _tcpClient = new TcpClient();
            var connectTask = _tcpClient.ConnectAsync(host, port);
            if (await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) != connectTask)
            {
                Cleanup();
                OnError?.Invoke($"TCP connect to {host}:{port} timed out after 5s");
                return false;
            }
            await connectTask.ConfigureAwait(false);
            _tcpStream = _tcpClient.GetStream();
            Protocol = NetworkProtocol.TCP;
            Host = host;
            Port = port;
            IsConnected = true;

            StartReceiveLoop();
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"TCP connect failed: {ex.Message}");
            Cleanup();
            return false;
        }
    }

    public bool ConnectUdp(string host, int port)
    {
        if (IsConnected) return true;

        try
        {
            _udpClient = new UdpClient();
            _udpClient.Connect(host, port);
            Protocol = NetworkProtocol.UDP;
            Host = host;
            Port = port;
            IsConnected = true;

            StartReceiveLoop();
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"UDP connect failed: {ex.Message}");
            Cleanup();
            return false;
        }
    }

    public bool Send(string data, bool isHex)
    {
        if (!IsConnected)
        {
            OnError?.Invoke("Network not connected");
            return false;
        }

        try
        {
            byte[] bytes;
            if (isHex)
            {
                bytes = Convert.FromHexString(data.Replace(" ", ""));
            }
            else
            {
                bytes = System.Text.Encoding.UTF8.GetBytes(data);
            }

            lock (_lock)
            {
                if (Protocol == NetworkProtocol.TCP)
                {
                    _tcpStream?.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    _udpClient?.Send(bytes, bytes.Length);
                }
            }

            var entry = new LogEntry
            {
                Id = Interlocked.Increment(ref _txEntryId),
                Timestamp = DateTime.Now,
                Direction = "TX",
                RawHex = isHex ? data.Replace(" ", "") : HexHelper.BytesToHexSpaced(bytes, 0, bytes.Length),
                Text = data
            };
            OnDataReceived?.Invoke(entry);
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Network send failed: {ex.Message}");
            HandleDisconnect();
            return false;
        }
    }

    public bool SendHex(string hex) => Send(hex, true);

    public bool Close()
    {
        lock (_lock)
        {
            if (!IsConnected) return true;

            try
            {
                IsConnected = false;
                Cleanup();
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Network close error: {ex.Message}");
                Cleanup();
                return false;
            }
        }
    }

    private void StartReceiveLoop()
    {
        var oldCts = _receiveCts;
        _receiveCts = new CancellationTokenSource();
        oldCts?.Dispose();
        var token = _receiveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (Protocol == NetworkProtocol.TCP)
                {
                    await ReceiveTcpLoop(token).ConfigureAwait(false);
                }
                else
                {
                    await ReceiveUdpLoop(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    OnError?.Invoke($"Receive error: {ex.Message}");
            }
        }, token);
    }

    private async Task ReceiveTcpLoop(CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (!token.IsCancellationRequested && IsConnected && _tcpStream != null)
            {
                int bytesRead = await _tcpStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    HandleDisconnect();
                    return;
                }

                RaiseDataReceived(buffer, 0, bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task ReceiveUdpLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && IsConnected && _udpClient != null)
        {
            var result = await _udpClient.ReceiveAsync(token).ConfigureAwait(false);
            RaiseDataReceived(result.Buffer, 0, result.Buffer.Length);
        }
    }

    private void RaiseDataReceived(byte[] buffer, int offset, int count)
    {
        var hex = HexHelper.BytesToHexSpaced(buffer, offset, count);
        var text = System.Text.Encoding.UTF8.GetString(buffer, offset, count);

        var entry = new LogEntry
        {
            Id = Interlocked.Increment(ref _rxEntryId),
            Timestamp = DateTime.Now,
            Direction = "RX",
            RawHex = hex,
            Text = text
        };
        OnDataReceived?.Invoke(entry);
    }

    private void HandleDisconnect()
    {
        lock (_lock)
        {
            if (!IsConnected) return;
            IsConnected = false;
            Cleanup();
        }
        OnDisconnected?.Invoke();
    }

    private void Cleanup()
    {
        var cts = Interlocked.Exchange(ref _receiveCts, null);
        cts?.Cancel();
        cts?.Dispose();

        var tcpStream = Interlocked.Exchange(ref _tcpStream, null);
        tcpStream?.Dispose();

        var tcpClient = Interlocked.Exchange(ref _tcpClient, null);
        tcpClient?.Dispose();

        var udpClient = Interlocked.Exchange(ref _udpClient, null);
        udpClient?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}
