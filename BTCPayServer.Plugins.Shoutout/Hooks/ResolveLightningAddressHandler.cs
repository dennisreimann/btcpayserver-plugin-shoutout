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

public class ResolveLightningAddressHandler : IPluginHookFilter
{
    public string Hook { get; } = "resolve-lnurlp-request-for-lightning-address";

    private readonly AppService _appService;
    private readonly ShoutoutService _shoutoutService;
    private readonly LinkGenerator _linkGenerator;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ResolveLightningAddressHandler(
        AppService appService,
        ShoutoutService shoutoutService,
        LinkGenerator linkGenerator,
        IHttpContextAccessor httpContextAccessor)
    {
        _appService = appService;
        _shoutoutService = shoutoutService;
        _linkGenerator = linkGenerator;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<object> Execute(object args)
    {
        var obj = (LightningAddressResolver)args;
        var username = obj.Username;
        var request = _httpContextAccessor.HttpContext.Request;

        AppData app;
        if (request.RouteValues.TryGetValue("appId", out var vAppId) && vAppId is string appId)
        {
            app = await _appService.GetApp(appId, ShoutoutApp.AppType);
        }
        else
        {
            var apps = await _appService.GetApps(ShoutoutApp.AppType);
            app = apps.FirstOrDefault(a => a.GetSettings<ShoutoutSettings>().LightningAddressIdentifier == username);
        }
        if (app == null) return obj;

        // Check prerequisites
        var settings = app.GetSettings<ShoutoutSettings>();
        if (string.IsNullOrEmpty(settings.LightningAddressIdentifier)) return obj;

        var store = await _appService.GetStore(app);
        if (!_shoutoutService.IsLnurlEnabled(store)) return obj;

        // Success
        var metadata = new List<string[]> { new[] { "text/identifier", $"{username}@{request.Host}" } };
        obj.LNURLPayRequest = _shoutoutService.GetLnurlPayRequest(request, app.Id, metadata);

        return obj;
    }
}


