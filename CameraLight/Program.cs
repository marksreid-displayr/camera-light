using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
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
        builder.Services.Configure<WindowDetectionOptions>(builder.Configuration.GetSection("WindowDetection"));
        builder.Services.Configure<StateManagerOptions>(builder.Configuration.GetSection("StateManager"));
        builder.Services.AddHttpClient<IndicatorLightService>()
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<IndicatorLightOptions>>().Value;
                httpClient.BaseAddress = new Uri(config.BaseUrl ?? throw new Exception("BaseUrl is required"));
                var username = config.Username ?? throw new Exception("Username is required");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{config.Password}")));
            });
        builder.Services.AddSingleton<IIndicatorLightService, IndicatorLightService>();
        builder.Services.AddSingleton<ICameraDetectionService, DetectCameraWithWindowsTitlesService>();
        builder.Services.AddSingleton<IStateManager, StateManager>();

        var host = builder.Build();
        host.Run();
    }
}