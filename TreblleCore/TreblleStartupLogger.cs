using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Treblle.Net.Core;

/// <summary>
/// Reports the SDK's configuration state once, at host startup.
/// </summary>
/// <remarks>
/// Resolving <see cref="IOptions{TOptions}.Value"/> in the constructor is the point of this
/// service: it forces every Configure/PostConfigure delegate to run during startup. Those
/// delegates used to run lazily on the first request that reached the middleware, so a missing
/// credential or a malformed ingress URL let the application boot green and then threw on every
/// single request. Configuration problems now disable the SDK and are reported here instead.
/// </remarks>
internal sealed class TreblleStartupLogger : IHostedService
{
    private readonly TreblleOptions _options;
    private readonly ILogger<TreblleStartupLogger> _logger;

    public TreblleStartupLogger(IOptions<TreblleOptions> options, ILogger<TreblleStartupLogger> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsEnabled)
        {
            _logger.LogError(
                "[TREBLLE]: SDK is disabled - {Reason} No API requests will be reported until this is corrected.",
                _options.DisabledReason);
        }
        else if (_options.DebugMode)
        {
            _logger.LogDebug(
                "[TREBLLE]: SDK enabled. Ingress endpoint: {Endpoint}",
                _options.CustomIngressEndpoint ?? "default");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
