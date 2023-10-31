using Microsoft.Extensions.Options;

namespace CameraLight;

public class IndicatorLightService(IOptions<IndicatorLightOptions> options, IHttpClientFactory httpClientFactory) : IIndicatorLightService
{
    private readonly string _off = options.Value.Off ?? throw new Exception("Off is required");
    private readonly string _on = options.Value.On ?? throw new Exception("On is required");

    public async Task TurnOn()
    {
        var client = httpClientFactory.CreateClient(nameof(IndicatorLightService));
        await client.GetAsync(_on);
    }

    public async Task TurnOff()
    {
        var client = httpClientFactory.CreateClient(nameof(IndicatorLightService));
        await client.GetAsync(_off);
    }
}