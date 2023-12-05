using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Shoutout.Services;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Plugins.Shoutout.Hooks;

public class ResolveLightningAddressHandler(
    AppService appService,
    ShoutoutService shoutoutService,
    IHttpContextAccessor httpContextAccessor)
    : IPluginHookFilter
{
    public string Hook { get; } = "resolve-lnurlp-request-for-lightning-address";

    public async Task<object> Execute(object args)
    {
        var obj = (LightningAddressResolver)args;
        var username = obj.Username;
        var request = httpContextAccessor.HttpContext.Request;

        AppData app;
        if (request.RouteValues.TryGetValue("appId", out var vAppId) && vAppId is string appId)
        {
            app = await appService.GetApp(appId, ShoutoutApp.AppType);
        }
        else
        {
            var apps = await appService.GetApps(ShoutoutApp.AppType);
            app = apps.FirstOrDefault(a => a.GetSettings<ShoutoutSettings>().LightningAddressIdentifier == username);
        }
        if (app == null) return obj;

        // Check prerequisites
        var settings = app.GetSettings<ShoutoutSettings>();
        if (string.IsNullOrEmpty(settings.LightningAddressIdentifier)) return obj;

        var store = await appService.GetStore(app);
        if (!shoutoutService.IsLnurlEnabled(store)) return obj;

        // Success
        var metadata = new List<string[]> { new[] { "text/identifier", $"{username}@{request.Host}" } };
        obj.LNURLPayRequest = shoutoutService.GetLnurlPayRequest(request, app.Id, metadata);

        return obj;
    }
}


