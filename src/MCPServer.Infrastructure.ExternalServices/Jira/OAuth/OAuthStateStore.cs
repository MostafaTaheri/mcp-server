using System.Collections.Concurrent;
using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Infrastructure.ExternalServices.Jira.OAuth;

/// <summary>
/// In-memory state for the OAuth facade: registered clients, pending authorizations, one-time codes and sessions.
/// Entries expire by TTL; one-time entries are removed when read. Session tokens are stored only as SHA-256 hashes.
/// State is lost on restart (encryption at rest / persistence is JIRA-AUTH-04).
/// </summary>
public sealed class OAuthStateStore(TimeProvider clock)
{
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _clients = new();
    private readonly ConcurrentDictionary<string, PendingAuthorization> _pending = new();
    private readonly ConcurrentDictionary<string, IssuedCode> _codes = new();
    private readonly ConcurrentDictionary<string, (JiraOAuthSession Session, DateTimeOffset Expires)> _sessions = new();

    public sealed record PendingAuthorization(
        string ClientId, string RedirectUri, string ClientChallenge, string? ClientState, string ServerVerifier, DateTimeOffset Expires);

    public sealed record IssuedCode(
        string ClientId, string RedirectUri, string ClientChallenge, JiraOAuthSession Session, TimeSpan SessionLifetime, DateTimeOffset Expires);

    public string AddClient(IReadOnlyList<string> redirectUris)
    {
        var id = PkceHelper.RandomToken(16);
        _clients[id] = redirectUris;
        return id;
    }

    public IReadOnlyList<string>? GetClientRedirects(string clientId) =>
        _clients.TryGetValue(clientId, out var uris) ? uris : null;

    public string AddPending(string clientId, string redirectUri, string clientChallenge, string? clientState, string serverVerifier)
    {
        Sweep();
        var state = PkceHelper.RandomToken(24);
        _pending[state] = new(clientId, redirectUri, clientChallenge, clientState, serverVerifier, clock.GetUtcNow() + PendingTtl);
        return state;
    }

    public PendingAuthorization? TakePending(string state) =>
        _pending.TryRemove(state, out var p) && p.Expires > clock.GetUtcNow() ? p : null;

    public string AddCode(PendingAuthorization pending, JiraOAuthSession session, TimeSpan sessionLifetime)
    {
        var code = PkceHelper.RandomToken(24);
        _codes[code] = new(pending.ClientId, pending.RedirectUri, pending.ClientChallenge, session, sessionLifetime, clock.GetUtcNow() + CodeTtl);
        return code;
    }

    public IssuedCode? TakeCode(string code) =>
        _codes.TryRemove(code, out var c) && c.Expires > clock.GetUtcNow() ? c : null;

    public string AddSession(JiraOAuthSession session, TimeSpan lifetime)
    {
        var token = PkceHelper.RandomToken(32);
        _sessions[PkceHelper.Hash(token)] = (session, clock.GetUtcNow() + lifetime);
        return token;
    }

    public JiraOAuthSession? GetSession(string token)
    {
        var key = PkceHelper.Hash(token);
        if (!_sessions.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (entry.Expires > clock.GetUtcNow())
        {
            return entry.Session;
        }

        _sessions.TryRemove(key, out _);
        return null;
    }

    private void Sweep()
    {
        var now = clock.GetUtcNow();
        foreach (var (key, value) in _pending.Where(p => p.Value.Expires <= now))
        {
            _pending.TryRemove(key, out _);
        }

        foreach (var (key, value) in _codes.Where(p => p.Value.Expires <= now))
        {
            _codes.TryRemove(key, out _);
        }

        foreach (var (key, value) in _sessions.Where(p => p.Value.Expires <= now))
        {
            _sessions.TryRemove(key, out _);
        }
    }
}
