using EducationHub.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Builds the real API host in-process for endpoint tests.
/// </summary>
/// <remarks>
/// Every machine-dependent setting (connection string, signing key, seeded administrator
/// credentials, rate-limit ceiling) is supplied through environment variables by
/// <see cref="ApiTestFixture"/> before the host is first built, because the API reads those
/// values at startup - before <see cref="ConfigureWebHost"/> overrides would take effect.
/// This factory pins the environment name and swaps the WhatsApp provider for a recorder, so
/// tests can read what would have been sent (a reset code, a set of credentials).
/// </remarks>
internal sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public CapturingWhatsAppSender WhatsApp { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development so the database initializer (migrate + seed) runs and Swagger is enabled,
        // matching the E2E configuration the fixes were verified against.
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWhatsAppSender>();
            services.AddSingleton<IWhatsAppSender>(WhatsApp);
        });
    }
}

/// <summary>Records every WhatsApp message instead of sending it; always "accepts".</summary>
public sealed class CapturingWhatsAppSender : IWhatsAppSender
{
    private readonly List<WhatsAppMessage> _messages = [];

    public bool IsEnabled => true;

    public Task<bool> SendAsync(WhatsAppMessage message, CancellationToken cancellationToken = default)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }

        return Task.FromResult(true);
    }

    /// <summary>Messages sent to the number (canonical form), oldest first.</summary>
    public IReadOnlyList<WhatsAppMessage> To(string canonicalPhone)
    {
        lock (_messages)
        {
            return [.. _messages.Where(m => m.ToPhone == canonicalPhone)];
        }
    }
}
