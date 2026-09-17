using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using CameraLight.Base;
using CameraLight.Tray;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Serilog;

namespace CameraLight;

public class Program
{
    // How long to wait for the lights to go off when the user exits from the tray menu.
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);

    [STAThread]
    public static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var builder = Host.CreateApplicationBuilder(args);

        // The user's settings sit on top of the shipped defaults, in AppData rather than beside the
        // executable, so deploying over the install folder never takes them with it.
        builder.Configuration.AddJsonFile(UserPaths.Settings, optional: true, reloadOnChange: true);

        var logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(logger);

        builder.Services.Configure<IndicatorLightOptions>(builder.Configuration.GetSection("IndicatorLight"));
        builder.Services.Configure<HomeBridgeOptions>(builder.Configuration.GetSection("HomeBridge"));
        builder.Services.Configure<DetectionOptions>(builder.Configuration.GetSection("Detection"));
        builder.Services.Configure<StateManagerOptions>(builder.Configuration.GetSection("StateManager"));
        builder.Services.Configure<DnsOptions>(builder.Configuration.GetSection("Dns"));

        // On the VPN the system resolver often can't answer for names outside the corporate
        // network, so both clients resolve through our own resolver instead of the socket layer's.
        builder.Services.AddSingleton<IDnsResolver, FallbackDnsResolver>();

        builder.Services.AddHttpClient(nameof(IndicatorLightService))
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<IndicatorLightOptions>>().Value;
                httpClient.BaseAddress = new Uri(config.BaseUrl ?? throw new Exception("BaseUrl is required"));
                var username = config.Username ?? throw new Exception("Username is required");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{config.Password}")));
            })
            .UseFallbackDns()
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

        builder.Services.AddHttpClient(nameof(HomeBridgeService))
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<HomeBridgeOptions>>().Value;
                httpClient.BaseAddress = new Uri(config.BaseUrl ?? throw new Exception("BaseUrl is required"));
            })
            .UseFallbackDns()
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

        // Lights:UseMock drives nothing real, for working on the tray UI without flashing the office.
        if (builder.Configuration.GetValue<bool>("Lights:UseMock"))
        {
            builder.Services.AddSingleton<IIndicatorLightService, MockLightService>();
        }
        else
        {
            builder.Services.AddSingleton<IIndicatorLightService, IndicatorLightService>();
            builder.Services.AddSingleton<IIndicatorLightService, HomeBridgeService>();
        }

        builder.Services.AddSingleton<IDeviceUsageDetector>(serviceProvider => new ConsentStoreDetector(
            DeviceKind.Camera, serviceProvider.GetRequiredService<ILogger<ConsentStoreDetector>>()));
        builder.Services.AddSingleton<IDeviceUsageDetector>(serviceProvider => new ConsentStoreDetector(
            DeviceKind.Microphone, serviceProvider.GetRequiredService<ILogger<ConsentStoreDetector>>()));

        builder.Services.AddSingleton<IEventLog, UsageEventLog>();
        builder.Services.AddSingleton<UserSettingsStore>();
        builder.Services.AddSingleton<IStateManager, StateManager>();
        builder.Services.AddSingleton<IDisplayIdleBlocker, DisplayIdleBlocker>();

        // One instance wearing both hats: the loop that polls, and the thing the UI reads from.
        builder.Services.AddSingleton<UsageMonitor>();
        builder.Services.AddSingleton<IUsageMonitor>(serviceProvider => serviceProvider.GetRequiredService<UsageMonitor>());
        builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<UsageMonitor>());

        builder.Services.AddSingleton<TrayApplicationContext>();

        var host = builder.Build();
        host.Start();

        try
        {
            // The message loop owns the main thread; the host keeps running behind it.
            Application.Run(host.Services.GetRequiredService<TrayApplicationContext>());
        }
        finally
        {
            TurnLightsOffOnExit(host.Services);
            host.StopAsync().GetAwaiter().GetResult();
            host.Dispose();
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Leaving a light on with nothing left to turn it off is the failure this app exists to avoid,
    /// so exiting drives the lights off and waits briefly for them to agree.
    /// </summary>
    private static void TurnLightsOffOnExit(IServiceProvider services)
    {
        var stateManager = services.GetRequiredService<IStateManager>();
        stateManager.SetForcedOff(true);

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ShutdownGrace && stateManager.Status.Applied != State.Off)
        {
            Thread.Sleep(100);
        }
    }
}
