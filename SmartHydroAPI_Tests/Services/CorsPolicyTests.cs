using System.Text.Json;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace SmartHydroAPI_Tests.Services;

public class CorsPolicyTests
{
    [Theory]
    [InlineData("https://smarthydromvc-eddch7ajapdyb8f4.southafricanorth-01.azurewebsites.net", true)]
    [InlineData("https://unrelated.example", false)]
    [InlineData("http://smarthydromvc-eddch7ajapdyb8f4.southafricanorth-01.azurewebsites.net", false)]
    [InlineData("https://smarthydromvc-eddch7ajapdyb8f4.southafricanorth-01.azurewebsites.net.unrelated.example", false)]
    public void ConfiguredOriginsPermitOnlyTheExplicitMvcOrigin(string origin, bool allowed)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SmartHydroAPI", "Program.cs")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "SmartHydroAPI", "appsettings.json")));
        var origins = settings.RootElement.GetProperty("Cors").GetProperty("AllowedOrigins")
            .EnumerateArray().Select(x => x.GetString()!).ToArray();
        var policy = new CorsPolicyBuilder().WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials().Build();
        using var services = new ServiceCollection().AddLogging().AddCors().BuildServiceProvider();
        var context = new DefaultHttpContext();
        context.Request.Method = "OPTIONS";
        context.Request.Path = "/hubs/actuators/negotiate";
        context.Request.Headers.Origin = origin;
        context.Request.Headers.AccessControlRequestMethod = "POST";
        context.Request.Headers.AccessControlRequestHeaders = "authorization,content-type";
        var result = services.GetRequiredService<ICorsService>().EvaluatePolicy(context, policy);
        Assert.Equal(allowed, result.IsOriginAllowed);
        Assert.False(policy.AllowAnyOrigin);
        if (allowed) {
            Assert.Equal(origin, result.AllowedOrigin);
            Assert.True(result.SupportsCredentials);
            Assert.Contains("POST", result.AllowedMethods);
            Assert.Contains("authorization", result.AllowedHeaders);
        }
    }
}
