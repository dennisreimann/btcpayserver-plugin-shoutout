namespace BTCPayServer.Plugins.Shoutout;

public class ShoutoutSettings
{
    public string Title { get; set; }
    public string Currency { get; set; }
    public string Description { get; set; }
    public bool ShowHeader { get; set; } = true;
    public bool ShowRelativeDate { get; set; } = true;
    public string ButtonText { get; set; } = "Shoutout!";
    public decimal MinAmount { get; set; }
}
