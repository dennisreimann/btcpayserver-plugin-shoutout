using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Shoutout.Hooks;
using BTCPayServer.Plugins.Shoutout.Services;
using BTCPayServer.Services.Apps;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Shoutout;

public class ShoutoutPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.0.0" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<AppBaseType, ShoutoutApp>();
        services.AddSingleton<ShoutoutService>();
        services.AddSingleton<ResolveLightningAddressHandler>();
        services.AddSingleton<IPluginHookFilter, ResolveLightningAddressHandler>();
        services.AddUIExtension("header-nav", "ShoutoutNavExtension");
    }
}
