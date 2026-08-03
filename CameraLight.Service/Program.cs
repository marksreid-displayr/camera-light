using System.Net.Http.Headers;
using System.Text;
using CameraLight.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;
using Serilog;

namespace CameraLight.Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureLogging((context, logging) =>
                {
                    var logger = new LoggerConfiguration().ReadFrom.Configuration(context.Configuration).CreateLogger();
                    logging.ClearProviders();
                    logging.AddSerilog(logger);
                })
                .ConfigureServices((builder, services) =>
                {
                    services.AddHostedService<CameraLightService>();
                    services.Configure<WindowDetectionOptions>(builder.Configuration.GetSection("WindowDetection"));
                    services.Configure<WindowDetectionOptions>(builder.Configuration.GetSection("WindowDetection"));
                    services.Configure<IndicatorLightOptions>(builder.Configuration.GetSection("IndicatorLight"));
                    services.Configure<StateManagerOptions>(builder.Configuration.GetSection("StateManager"));
                    services.AddHttpClient<IndicatorLightService>()
                        .ConfigureHttpClient((serviceProvider, httpClient) =>
                        {
                            var config = serviceProvider.GetRequiredService<IOptions<IndicatorLightOptions>>().Value;
                            httpClient.BaseAddress = new Uri(config.BaseUrl ?? throw new Exception("BaseUrl is required"));
                            var username = config.Username ?? throw new Exception("Username is required");
                            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{config.Password}")));
                        });
                    services.AddSingleton<IIndicatorLightService, IndicatorLightService>();
                    services.AddSingleton<ICameraDetectionService, EscalatedDetectCameraWithWindowsTitlesService>();
                    services.AddSingleton<IStateManager, StateManager>();
                });
        }
    }

    public class CameraLightService(ILogger<CameraLightService> logger, ICameraDetectionService cameraDetectionService, IStateManager stateManager) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("CameraLightService is starting.");

            stoppingToken.Register(() =>
                logger.LogInformation("CameraLightService is stopping."));

            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Still running");
                var isActive = await cameraDetectionService.IsActive();
                stateManager.ChangeState(isActive ? State.On : State.Off);
                await Task.Delay(1000, stoppingToken);
            }

            logger.LogInformation("CameraLightService has stopped.");
        }
    }
}