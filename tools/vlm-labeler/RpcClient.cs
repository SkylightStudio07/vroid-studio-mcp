using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace VroidVlmLabeler;

/// <summary>Line-delimited JSON-RPC 2.0 client for the plugin's TCP command server.</summary>
public sealed class RpcClient : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private int _id;

    public RpcClient(string host, int port, int timeoutMs)
    {
        _tcp = new TcpClient { ReceiveTimeout = timeoutMs, SendTimeout = timeoutMs };
        _tcp.Connect(host, port);
        var stream = _tcp.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public JsonElement Call(string method, params object[] args)
    {
        var id = ++_id;
        _writer.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = args }));
        var line = _reader.ReadLine() ?? throw new IOException("command server closed the connection");
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : error.ToString();
            throw new RpcException($"{method}: {message}");
        }
        return root.GetProperty("result").Clone();
    }

    public void Dispose()
    {
        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _tcp?.Close(); } catch { }
    }
}

public sealed class RpcException : Exception
{
    public RpcException(string message) : base(message) { }
}
