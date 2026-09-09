using System.Net.Http.Headers;
using System.Text;
using CameraLight.Base;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Serilog;

namespace CameraLight;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        var logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(logger);
        builder.Services.AddHostedService<Worker>();
        builder.Services.Configure<IndicatorLightOptions>(builder.Configuration.GetSection("IndicatorLight"));
        builder.Services.Configure<HomeBridgeOptions>(builder.Configuration.GetSection("HomeBridge"));
        builder.Services.Configure<WebcamDetectionOptions>(builder.Configuration.GetSection("WebcamDetection"));
        builder.Services.Configure<StateManagerOptions>(builder.Configuration.GetSection("StateManager"));
        builder.Services.AddHttpClient<IndicatorLightService>()
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<IndicatorLightOptions>>().Value;
                httpClient.BaseAddress = new Uri(config.BaseUrl ?? throw new Exception("BaseUrl is required"));
                var username = config.Username ?? throw new Exception("Username is required");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{config.Password}")));
            })
            .AddResilienceHandler("IndicatorLightRetry", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 4,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true
                });
                // The bulb is on the LAN: if it hasn't answered in 5s it isn't going to.
                pipeline.AddTimeout(TimeSpan.FromSeconds(5));
            });

        builder.Services.AddHttpClient<HomeBridgeService>()
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<HomeBridgeOptions>>().Value;
                httpClient.BaseAddress = new Uri(config.BaseUrl ?? throw new Exception("BaseUrl is required"));
            })
            .AddResilienceHandler("HomeBridgeRetry", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 4,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(10));
            });

        builder.Services.AddSingleton<IIndicatorLightService, IndicatorLightService>();
        builder.Services.AddSingleton<IIndicatorLightService, HomeBridgeService>();
        builder.Services.AddSingleton<ICameraDetectionService, DetectCameraWithConsentStoreService>();
        builder.Services.AddSingleton<IStateManager, StateManager>();

        var host = builder.Build();
        host.Run();
    }
}