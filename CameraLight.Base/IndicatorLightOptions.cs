namespace CameraLight.Base;

public class IndicatorLightOptions
{
    /// <summary>False leaves this light out entirely, for when it is broken and the other should carry on alone.</summary>
    public bool Enabled { get; set; } = true;
    public string? BaseUrl { get; set; }
    public string? On { get; set; }
    public string? Off { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public class HomeBridgeOptions
{
    /// <summary>False leaves this light out entirely, for when it is broken and the other should carry on alone.</summary>
    public bool Enabled { get; set; } = true;
    public string? Uuid { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set;}
    public string? BaseUrl { get; set; }
}