using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Plugins.Shoutout.Hooks;
using BTCPayServer.Plugins.Shoutout.Services;
using BTCPayServer.Services.Apps;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Shoutout;

public class ShoutoutPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } = {
        new() { Identifier = nameof(BTCPayServer), Condition = ">=1.11.7" }
    };

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<AppBaseType, ShoutoutApp>();
        services.AddSingleton<ShoutoutService>();
        services.AddSingleton<ResolveLightningAddressHandler>();
        services.AddSingleton<IPluginHookFilter, ResolveLightningAddressHandler>();
        services.AddSingleton<IUIExtension>(new UIExtension("ShoutoutNavExtension", "header-nav"));
    }
}
