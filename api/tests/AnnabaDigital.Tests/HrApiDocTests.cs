using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>
/// docs/28-hr-api-routes.md is generated from the real routing table, so the list of HR endpoints and the permission each one needs can never drift from the code.
/// If this test fails after you added or changed a route, regenerate the file: <c>UPDATE_API_DOC=1 dotnet test --filter HrApiDocTests</c> and commit it.
/// </summary>
[Collection("db")]
public sealed class HrApiDocTests(PostgresFixture fx)
{
    private static string RepoFile(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "README.md"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repository root not found"), rel.Replace('/', Path.DirectorySeparatorChar));
    }

    private string Render()
    {
        var routes = fx.Factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/hr") == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (Method: m, Path: Regex.Replace(e.RoutePattern.RawText!, @"\{(\w+):\w+\}", "{$1}"),
                Perms: e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Where(p => p?.StartsWith(PermissionPolicy.Prefix) == true).Select(p => p![PermissionPolicy.Prefix.Length..]).Distinct().Order().ToList())))
            .Distinct().OrderBy(r => r.Path, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder();
        sb.Append("# 28 — HR API routes\n\n");
        sb.Append("Generated from the running application's routing table by `HrApiDocTests` — do not edit by hand (`UPDATE_API_DOC=1 dotnet test api/AnnabaDigital.sln --filter HrApiDocTests` regenerates it).\n");
        sb.Append("Every route needs a signed-in session. \"Permission\" is the extra permission the route requires; routes marked *self-service* only ever touch the caller's own data (the service checks it). ");
        sb.Append("Errors use the envelope `{ error: { code, message, fields?, requestId } }`; see `08-api-spec.md`. Mutating calls need the `X-CSRF-Token` header like every other API call, and payroll, protected-data and document routes additionally ask for a password check in the last five minutes (`reauth_required`).\n\n");
        foreach (var g in routes.GroupBy(r => r.Path.Split('/').Length > 3 ? r.Path.Split('/')[3] : "").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.Append($"## /api/hr/{g.Key}\n\n| Method | Path | Permission |\n|---|---|---|\n");
            foreach (var r in g) sb.Append($"| {r.Method} | `{r.Path}` | {(r.Perms.Count == 0 ? "*self-service*" : string.Join(" + ", r.Perms.Select(p => $"`{p}`")))} |\n");
            sb.Append('\n');
        }
        sb.Append($"Total: {routes.Count} routes.\n");
        return sb.ToString();
    }

    [Fact]
    public void The_hr_api_route_document_matches_the_code()
    {
        var path = RepoFile("docs/28-hr-api-routes.md"); var now = Render();
        if (Environment.GetEnvironmentVariable("UPDATE_API_DOC") == "1") { File.WriteAllText(path, now); return; }
        Assert.True(File.Exists(path), "docs/28-hr-api-routes.md is missing: run UPDATE_API_DOC=1 dotnet test --filter HrApiDocTests");
        Assert.Equal(now.ReplaceLineEndings("\n"), File.ReadAllText(path).ReplaceLineEndings("\n"));
    }
}
