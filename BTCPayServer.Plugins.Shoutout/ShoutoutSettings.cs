namespace BTCPayServer.Plugins.Shoutout;

public class ShoutoutSettings
{
    public string? Title { get; init; }
    public string? Currency { get; init; }
    public string? Description { get; init; }
    public string? LightningAddressIdentifier { get; init; }
    public bool ShowHeader { get; init; } = true;
    public bool ShowRelativeDate { get; init; } = true;
    public string? ButtonText { get; init; } = "Shoutout!";
    public decimal MinAmount { get; init; }
}
