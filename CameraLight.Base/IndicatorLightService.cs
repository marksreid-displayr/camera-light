using Microsoft.Extensions.Options;

namespace CameraLight.Base;

public class IndicatorLightService(IOptions<IndicatorLightOptions> options, IHttpClientFactory httpClientFactory) : IIndicatorLightService
{
    private readonly string _off = options.Value.Off ?? throw new Exception("Off is required");
    private readonly string _on = options.Value.On ?? throw new Exception("On is required");

    public async Task TurnOn()
    {
        var client = httpClientFactory.CreateClient(nameof(IndicatorLightService));
        var response = await client.GetAsync(_on);
        response.EnsureSuccessStatusCode();
    }

    public async Task TurnOff()
    {
        var client = httpClientFactory.CreateClient(nameof(IndicatorLightService));
        var response = await client.GetAsync(_off);
        response.EnsureSuccessStatusCode();
    }
}

public class AuthResponse
{
    // ReSharper disable once InconsistentNaming
    public string? access_token { get; set; }
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
}