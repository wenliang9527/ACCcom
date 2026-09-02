using System.Net;
using EmbedIO;

namespace ACCcom.Core.Services;

/// <summary>
/// Filter module that hardens the local HTTP API:
/// - When listening on loopback, rejects requests whose Host header is not
///   loopback (mitigates DNS rebinding) and whose Origin/Referer point to a
///   non-loopback site (blocks browser-based cross-site requests / CSRF).
/// - When an API token is configured, /api and /ws requests must present it
///   via the X-ACCcom-Token header or a "token" query parameter.
/// Valid requests pass through untouched to subsequent modules.
/// </summary>
public sealed class LocalAccessGuardModule : WebModuleBase
{
    private const string TokenHeader = "X-ACCcom-Token";
    private readonly bool _enforceLocalOnly;
    private readonly int _listenPort;
    private readonly string? _apiToken;

    public LocalAccessGuardModule(string listenUrl, string? apiToken = null)
        : base("/")
    {
        var uri = new Uri(listenUrl);
        _listenPort = uri.Port;
        _apiToken = string.IsNullOrWhiteSpace(apiToken) ? null : apiToken;
        _enforceLocalOnly = IsLoopbackHost(uri.Host);
    }

    // Filter module: never final; valid requests continue to the next module.
    public override bool IsFinalHandler => false;

    protected override Task OnRequestAsync(IHttpContext context)
    {
        if (_enforceLocalOnly)
        {
            if (!IsAllowedHost(context.Request.Headers["Host"]))
                throw HttpException.Forbidden("Host header is not allowed.");

            var origin = context.Request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) && !IsLoopbackUrl(origin))
                throw HttpException.Forbidden("Cross-origin requests are not allowed.");

            var referer = context.Request.Headers["Referer"];
            if (!string.IsNullOrEmpty(referer) && !IsLoopbackUrl(referer))
                throw HttpException.Forbidden("Cross-site requests are not allowed.");
        }

        if (_apiToken != null)
        {
            var path = context.Request.Url.LocalPath;
            if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/ws", StringComparison.OrdinalIgnoreCase))
            {
                var provided = context.Request.Headers[TokenHeader];
                if (string.IsNullOrEmpty(provided))
                    provided = context.Request.QueryString["token"];
                if (!string.Equals(provided, _apiToken, StringComparison.Ordinal))
                    throw HttpException.Unauthorized("Invalid or missing API token.");
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsAllowedHost(string? hostHeader)
    {
        if (string.IsNullOrEmpty(hostHeader)) return false;
        if (!Uri.TryCreate("http://" + hostHeader, UriKind.Absolute, out var uri)) return false;
        return IsLoopbackHost(uri.Host);
    }

    private static bool IsLoopbackUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsLoopbackHost(uri.Host);

    private static bool IsLoopbackHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("[::1]", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }
}
