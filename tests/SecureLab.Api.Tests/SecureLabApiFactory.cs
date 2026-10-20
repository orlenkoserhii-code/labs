using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class SecureLabApiFactory
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<LoginRateLimitOptions>(
                options => options.PermitLimit = 1000);
        });
    }
}