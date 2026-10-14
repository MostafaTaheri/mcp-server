using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using MCPServer.Infrastructure.ExternalServices.Jira.OAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MCPServer.Api.Tests;

public class OAuthFlowTests : IClassFixture<OAuthFlowTests.Factory>
{
    private const string AtlassianToken = "atl-access-token-XYZ";
    private const string ClientRedirect = "http://localhost:7777/cb";

    public sealed class FakeAtlassian : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> TokenForm { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/oauth/token")
            {
                TokenForm.Add(await request.Content!.ReadAsStringAsync(ct));
                return Json($$"""{"access_token":"{{AtlassianToken}}","expires_in":3600}""");
            }

            if (path == "/oauth/token/accessible-resources")
            {
                return Json("""[{"id":"cloud-1","name":"site"}]""");
            }

            if (path == "/ex/jira/cloud-1/rest/api/3/myself")
            {
                return request.Headers.Authorization?.Parameter == AtlassianToken
                    ? Json("""{"accountId":"acc-42","displayName":"Mo"}""")
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public FakeAtlassian Atlassian { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jira:AuthMode", "OAuth");
            builder.UseSetting("Jira:OAuth:ClientId", "atl-client");
            builder.UseSetting("Jira:OAuth:ClientSecret", "atl-secret");
            builder.UseSetting("Jira:OAuth:PublicBaseUrl", "http://localhost");
            builder.UseSetting("Jira:OAuth:AuthorizeUrl", "https://auth.example/authorize");
            builder.UseSetting("Jira:OAuth:TokenUrl", "https://auth.example/oauth/token");
            builder.UseSetting("Jira:OAuth:ApiBaseUrl", "https://api.example/");
            builder.ConfigureServices(s => s.AddHttpClient<AtlassianOAuthClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Atlassian));
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _http;

    public OAuthFlowTests(Factory factory)
    {
        _factory = factory;
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static HttpRequestMessage McpCall(string method, object? parameters = null, string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }),
                Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (bearer is not null)
        {
            request.Headers.Authorization = new("Bearer", bearer);
        }

        return request;
    }

    [Fact]
    public async Task Mcp_WithoutToken_ReturnsChallengeWithResourceMetadata()
    {
        var response = await _http.SendAsync(McpCall("tools/list"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var header = Assert.Single(response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"", header);
    }

    [Fact]
    public async Task Mcp_WithUnknownToken_IsChallenged()
    {
        var response = await _http.SendAsync(McpCall("tools/list", bearer: "nope"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Metadata_AdvertisesPkceAndEndpoints()
    {
        var resource = await _http.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource/mcp");
        Assert.Equal("http://localhost", resource.GetProperty("authorization_servers")[0].GetString());

        var server = await _http.GetFromJsonAsync<JsonElement>("/.well-known/oauth-authorization-server");
        Assert.Equal("S256", server.GetProperty("code_challenge_methods_supported")[0].GetString());
        Assert.Equal("http://localhost/oauth/token", server.GetProperty("token_endpoint").GetString());
    }

    [Fact]
    public async Task FullFlow_ThenToolCall_UsesOnlyTheCallersAtlassianToken()
    {
        var clientId = await RegisterAsync();
        var verifier = "client-verifier-0123456789-0123456789-0123456789";
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // authorize → redirect to Atlassian with PKCE and the server's own state
        var authorize = await _http.GetAsync(
            $"/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(ClientRedirect)}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=client-state");
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var atlassianUrl = authorize.Headers.Location!;
        Assert.StartsWith("https://auth.example/authorize", atlassianUrl.AbsoluteUri);
        var q = HttpUtility.ParseQueryString(atlassianUrl.Query);
        Assert.Equal("atl-client", q["client_id"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("http://localhost/oauth/callback", q["redirect_uri"]);
        Assert.NotEqual("client-state", q["state"]);
        Assert.DoesNotContain("atl-secret", atlassianUrl.AbsoluteUri);

        // callback → server exchanges the code and redirects to the client with a one-time code
        var callback = await _http.GetAsync($"/oauth/callback?code=atl-code&state={Uri.EscapeDataString(q["state"]!)}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var back = HttpUtility.ParseQueryString(callback.Headers.Location!.Query);
        Assert.StartsWith(ClientRedirect, callback.Headers.Location.AbsoluteUri);
        Assert.Equal("client-state", back["state"]);
        var form = Assert.Single(_factory.Atlassian.TokenForm);
        Assert.Contains("code_verifier=", form);
        Assert.Contains("client_secret=atl-secret", form);

        // wrong verifier is rejected (and burns the code)
        var bad = await _http.PostAsync("/oauth/token", Form(back["code"]!, clientId, "wrong-verifier"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // a second flow: correct verifier → session token (never the Atlassian token)
        var token = await CompleteFlowAsync(clientId, verifier, challenge);
        Assert.NotEqual(AtlassianToken, token);

        var call = await _http.SendAsync(McpCall("tools/call", new { name = "jira_whoami", arguments = new { } }, token));
        var text = await call.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        Assert.Contains("acc-42", text);

        var jiraCall = _factory.Atlassian.Requests.Last(r => r.RequestUri!.AbsolutePath.EndsWith("/myself"));
        Assert.Equal(AtlassianToken, jiraCall.Headers.Authorization!.Parameter);
        Assert.DoesNotContain(AtlassianToken, text);
    }

    [Fact]
    public async Task Callback_WithUnknownState_IsRejected()
    {
        var response = await _http.GetAsync("/oauth/callback?code=x&state=forged");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_WithUnregisteredRedirect_IsRejected()
    {
        var clientId = await RegisterAsync();
        var response = await _http.GetAsync(
            $"/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString("https://evil.example/cb")}" +
            "&code_challenge=abc&code_challenge_method=S256");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<string> RegisterAsync()
    {
        var response = await _http.PostAsJsonAsync("/oauth/register", new { redirect_uris = new[] { ClientRedirect } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("client_id").GetString()!;
    }

    private async Task<string> CompleteFlowAsync(string clientId, string verifier, string challenge)
    {
        var authorize = await _http.GetAsync(
            $"/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(ClientRedirect)}" +
            $"&code_challenge={challenge}&code_challenge_method=S256");
        var state = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query)["state"]!;
        var callback = await _http.GetAsync($"/oauth/callback?code=atl-code&state={Uri.EscapeDataString(state)}");
        var code = HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"]!;

        var token = await _http.PostAsync("/oauth/token", Form(code, clientId, verifier));
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        return (await token.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
    }

    private static FormUrlEncodedContent Form(string code, string clientId, string verifier) => new(new Dictionary<string, string>
    {
        ["grant_type"] = "authorization_code",
        ["code"] = code,
        ["client_id"] = clientId,
        ["redirect_uri"] = ClientRedirect,
        ["code_verifier"] = verifier
    });
}
