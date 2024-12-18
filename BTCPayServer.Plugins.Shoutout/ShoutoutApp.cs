using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Shoutout.Controllers;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.Shoutout;

public class ShoutoutApp : AppBaseType, IHasSaleStatsAppType, IHasItemStatsAppType
{
    private readonly LinkGenerator _linkGenerator;
    private readonly DisplayFormatter _displayFormatter;
    private readonly IOptions<BTCPayServerOptions> _btcPayServerOptions;
    public const string AppType = "Shoutout";
    public const string ItemCode = "shoutout";

    private static readonly AppItem[] Items =
    [
        new()
        {
            Id = ItemCode,
            Title = "Shoutout",
            PriceType = AppItemPriceType.Topup
        }
    ];

    public ShoutoutApp(
        LinkGenerator linkGenerator,
        DisplayFormatter displayFormatter,
        IOptions<BTCPayServerOptions> btcPayServerOptions)
    {
        Type = AppType;
        Description = "Shoutout";
        _linkGenerator = linkGenerator;
        _displayFormatter = displayFormatter;
        _btcPayServerOptions = btcPayServerOptions;
    }

    public override Task<string> ConfigureLink(AppData app)
    {
        return Task.FromResult(_linkGenerator.GetPathByAction(
            nameof(UIShoutoutController.UpdateSettings),
            nameof(UIShoutoutController).TrimEnd("Controller", StringComparison.InvariantCulture),
            new { appId = app.Id }, _btcPayServerOptions.Value.RootPath)!);
    }

    public override Task<object?> GetInfo(AppData appData)
    {
        return Task.FromResult<object?>(null);
    }

    public override Task SetDefaultSettings(AppData appData, string defaultCurrency)
    {
        var empty = new ShoutoutSettings { Currency = defaultCurrency };
        appData.SetSettings(empty);
        return Task.CompletedTask;
    }

    public override Task<string> ViewLink(AppData app)
    {
        return Task.FromResult(_linkGenerator.GetPathByAction(
            nameof(UIShoutoutController.Public),
            nameof(UIShoutoutController).TrimEnd("Controller", StringComparison.InvariantCulture),
            new { appId = app.Id }, _btcPayServerOptions.Value.RootPath)!);
    }

    public Task<AppSalesStats> GetSalesStats(AppData app, InvoiceEntity[] paidInvoices, int numberOfDays)
    {
        return AppService.GetSalesStatswithPOSItems(Items, paidInvoices, numberOfDays);
    }

    public Task<IEnumerable<AppItemStats>> GetItemStats(AppData appData, InvoiceEntity[] paidInvoices)
    {
        var item = Items.First();
        var settings = appData.GetSettings<ShoutoutSettings>();
        var itemCount = paidInvoices
            .Where(entity => entity.Metadata.ItemCode == ItemCode &&
                             entity.Currency.Equals(settings.Currency, StringComparison.OrdinalIgnoreCase))
            .Aggregate([], AppService.AggregateInvoiceEntitiesForStats(Items))
            .GroupBy(entity => entity.ItemCode)
            .Select(entities =>
            {
                var total = entities.Sum(entity => entity.FiatPrice);
                var itemCode = entities.Key;
                return new AppItemStats
                {
                    ItemCode = itemCode,
                    Title = item.Title ?? itemCode,
                    SalesCount = entities.Count(),
                    Total = total,
                    TotalFormatted = _displayFormatter.Currency(total, settings.Currency)
                };
            })
            .OrderByDescending(stats => stats.SalesCount);
        return Task.FromResult<IEnumerable<AppItemStats>>(itemCount);
    }
}
