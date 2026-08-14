using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraLight.Base;

public class HomeBridgeService(IOptions<HomeBridgeOptions> options, IHttpClientFactory httpClientFactory, ILogger<HomeBridgeService> logger)
    : IIndicatorLightService
{
 
    private async Task OnOff(bool state)
    {
        var accessToken = await Authenticate();
        var client = httpClientFactory.CreateClient(nameof(HomeBridgeService));
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        var response = await client.PutAsJsonAsync($"/api/accessories/{options.Value.Uuid}",
            new { characteristicType = "On", value = state.ToString().ToLower() });
        response.EnsureSuccessStatusCode();
    }

    public async Task TurnOn()
    {
        try
        {
            await OnOff(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to turn on");
            throw;
        }
    }

    public async Task TurnOff()
    {
        try
        {
            await OnOff(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to turn off");
            throw;
        }
    }

    public async Task<string> Authenticate()
    {
        var client = httpClientFactory.CreateClient(nameof(HomeBridgeService));
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = options.Value.Username,
            password = options.Value.Password
        });

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Unable to log into HomeBridge");
            throw new("Unable to log into HomeBridge");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var jsonResponse = JsonSerializer.Deserialize<AuthResponse>(responseBody);

        return jsonResponse?.access_token ?? throw new ("Access Token not returned");
    }
}