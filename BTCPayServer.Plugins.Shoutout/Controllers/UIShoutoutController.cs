using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Forms;
using BTCPayServer.Models;
using BTCPayServer.Plugins.Shoutout.Services;
using BTCPayServer.Plugins.Shoutout.ViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NicolasDorier.RateLimits;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Shoutout.Controllers;

[AutoValidateAntiforgeryToken]
public class UIShoutoutController(
    AppService appService,
    UriResolver uriResolver,
    CurrencyNameTable currencies,
    ShoutoutService shoutoutService,
    FormDataService formDataService,
    IStringLocalizer stringLocalizer,
    InvoiceRepository invoiceRepository,
    UIInvoiceController invoiceController,
    IAuthorizationService authorizationService)
    : Controller
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [HttpGet("{appId}/settings/shoutout")]
    public IActionResult UpdateSettings(string appId)
    {
        var app = GetCurrentApp();
        if (app == null)
            return NotFound();

        var store = GetCurrentStore();
        if (store == null)
            return NotFound();

        var storeBlob = store.GetStoreBlob();
        var settings = app.GetSettings<ShoutoutSettings>();
        var vm = new UpdateSettingsViewModel
        {
            Id = appId,
            Archived = app.Archived,
            StoreId = app.StoreDataId,
            StoreName = app.StoreData?.StoreName,
            StoreDefaultCurrency = GetStoreDefaultCurrentIfEmpty(storeBlob, settings.Currency),
            SearchTerm = GetSearchTerm(app),
            AppName = app.Name,
            Title = settings.Title ?? app.Name,
            Currency = settings.Currency,
            Description = settings.Description,
            ShowHeader = settings.ShowHeader,
            ShowRelativeDate = settings.ShowRelativeDate,
            MinAmount = settings.MinAmount,
            ButtonText = settings.ButtonText,
            LightningAddressIdentifier = settings.LightningAddressIdentifier,
            LnurlEnabled = shoutoutService.IsLnurlEnabled(store),
            ExcludeInvoiceId = ListToString(settings.ExcludeInvoiceId)
        };
        return View(vm);
    }

    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [HttpPost("{appId}/settings/shoutout")]
    public async Task<IActionResult> UpdateSettings(string appId, UpdateSettingsViewModel vm)
    {
        var app = GetCurrentApp();
        var store = GetCurrentStore();
        if (app == null || store == null)
            return NotFound();

        if (!ModelState.IsValid)
            return View(vm);

        var storeBlob = store.GetStoreBlob();
        vm.Currency = GetStoreDefaultCurrentIfEmpty(storeBlob, vm.Currency);
        if (currencies.GetCurrencyData(vm.Currency, false) == null)
            ModelState.AddModelError(nameof(vm.Currency), StringLocalizer["Invalid currency"]);

        if (!ModelState.IsValid)
            return View(vm);

        var settings = new ShoutoutSettings
        {
            Title = vm.Title,
            Currency = vm.Currency,
            Description = vm.Description,
            ShowHeader = vm.ShowHeader,
            ShowRelativeDate = vm.ShowRelativeDate,
            MinAmount = vm.MinAmount,
            ButtonText = vm.ButtonText,
            LightningAddressIdentifier = vm.LightningAddressIdentifier,
            ExcludeInvoiceId = StringToList(vm.ExcludeInvoiceId)
        };

        app.Name = vm.AppName;
        app.Archived = vm.Archived;
        app.SetSettings(settings);
        await appService.UpdateOrCreateApp(app);
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The settings have been updated"].Value;
        return RedirectToAction(nameof(UpdateSettings), new { appId });
    }

    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [HttpGet("{appId}/settings/shoutout/toggle/{invoiceId}")]
    public async Task<IActionResult> ToggleExclude(string appId, string invoiceId, int? skip, int? count)
    {
        var app = GetCurrentApp();
        var store = GetCurrentStore();
        if (app == null || store == null)
            return NotFound();

        var settings = app.GetSettings<ShoutoutSettings>();
        var exclude = !settings.ExcludeInvoiceId.Contains(invoiceId);
        settings.ExcludeInvoiceId = exclude
            ? settings.ExcludeInvoiceId.Prepend(invoiceId).ToArray()
            : settings.ExcludeInvoiceId.Where(i => i != invoiceId).ToArray();
        app.SetSettings(settings);
        await appService.UpdateOrCreateApp(app);
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The shoutout {0} has been {1}",invoiceId, exclude ? StringLocalizer["hidden"] : StringLocalizer["shown"]].Value;
        return RedirectToAction(nameof(Public), new { appId, skip, count });
    }

    [HttpGet("/")]
    [HttpGet("/apps/{appId}/shoutout")]
    [DomainMappingConstraint(ShoutoutApp.AppType)]
    [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
    public async Task<IActionResult> Public(string appId, int skip = 0, int count = 50)
    {
        var app = await appService.GetApp(appId, ShoutoutApp.AppType, true);
        if (app == null)
            return NotFound();

        var settings = app.GetSettings<ShoutoutSettings>();
        var store = app.StoreData;
        var fs = new SearchString(GetSearchTerm(app));
        var invoiceQuery = new InvoiceQuery
        {
            Take = count,
            Skip = skip,
            IncludeArchived = true,
            StoreId = [store.Id],
            TextSearch = AppService.GetAppSearchTerm(app),
            StartDate = fs.GetFilterDate("startdate", 0),
            EndDate = fs.GetFilterDate("enddate", 0),
            Status = [InvoiceStatus.Settled.ToString(), InvoiceStatus.Processing.ToString()]
        };

        var invoices = await invoiceRepository.GetInvoices(invoiceQuery);
        var shoutouts = invoices
            .Select(i => i.Metadata.AdditionalData.TryGetValue("shoutout", out var shoutout)
                ? new ShoutoutViewModel
                {
                    Amount = i.PaidAmount.Net > 0 ? i.PaidAmount.Net : i.Price,
                    Currency = i.Currency,
                    Timestamp = i.InvoiceTime,
                    InvoiceId = i.Id,
                    Name = shoutout["name"]?.ToString(),
                    Text = shoutout["text"]?.ToString(),
                    Hidden = settings.ExcludeInvoiceId.Contains(i.Id)
                }
                : null)
            .OfType<ShoutoutViewModel>()
            .Where(s => !string.IsNullOrEmpty(s.Text) && (settings.MinAmount == 0 ||
                    (s.Amount >= settings.MinAmount && s.Currency?.Equals(settings.Currency, StringComparison.OrdinalIgnoreCase) is true)));

        var vm = await GetPublicViewModel(app, new ShoutoutViewModel(), shoutouts.ToList());
        vm.CanManage = (await authorizationService.AuthorizeAsync(HttpContext.User, null,
            new PolicyRequirement(Policies.CanModifyStoreSettings))).Succeeded;
        vm.Count = count;
        vm.Skip = skip;
        return View(vm);
    }

    [HttpPost("/")]
    [HttpPost("/apps/{appId}/shoutout")]
    [IgnoreAntiforgeryToken]
    [EnableCors(CorsPolicies.All)]
    [DomainMappingConstraint(ShoutoutApp.AppType)]
    [RateLimitsFilter(ZoneLimits.PublicInvoices, Scope = RateLimitsScope.RemoteAddress)]
    [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
    public async Task<IActionResult> Public(string appId, ShoutoutViewModel? shoutout)
    {
        var app = await appService.GetApp(appId, ShoutoutApp.AppType, true);
        if (app == null)
            return NotFound();

        shoutout ??= new ShoutoutViewModel();
        if (!ModelState.IsValid)
        {
            var vm = await GetPublicViewModel(app, shoutout);
            return View(vm);
        }

        var form = GetForm(shoutout);
        if (!formDataService.Validate(form, ModelState))
        {
            var vm = await GetPublicViewModel(app, shoutout);
            return View(vm);
        }

        var store = app.StoreData;
        var settings = app.GetSettings<ShoutoutSettings>();

        try
        {
            var orderUrl = Request.GetDisplayUrl();
            var request = formDataService.GenerateInvoiceParametersFromForm(form);
            var metadata = new InvoiceMetadata
            {
                ItemCode = ShoutoutApp.ItemCode,
                ItemDesc = settings.Title,
                OrderId = AppService.GetRandomOrderId(),
                OrderUrl = orderUrl
            }.ToJObject();
            metadata.Merge(formDataService.GetValues(form));

            request.Metadata = metadata;
            request.Currency = settings.Currency;
            request.Checkout = new InvoiceDataBase.CheckoutOptions
            {
                RedirectURL = orderUrl
            };
            request.AdditionalSearchTerms = [AppService.GetAppSearchTerm(app)];
            var invoice = await invoiceController.CreateInvoiceCoreRaw(request, store,Request.GetAbsoluteRoot(), [AppService.GetAppInternalTag(appId)]);

            return RedirectToAction(nameof(UIInvoiceController.Checkout), "UIInvoice", new { invoiceId = invoice.Id });
        }
        catch (Exception e)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Html = e.Message.Replace("\n", "<br />", StringComparison.OrdinalIgnoreCase),
                Severity = StatusMessageModel.StatusSeverity.Error,
                AllowDismiss = true
            });
            return RedirectToAction(nameof(Public), new { appId });
        }
    }

    private static Form GetForm(ShoutoutViewModel shoutout)
    {
        var shoutoutFields = Field.CreateFieldset();
        shoutoutFields.Name = "shoutout";
        shoutoutFields.Label = "Shoutout";
        shoutoutFields.Fields =
        [
            Field.Create("Name", "name", shoutout.Name, false, null),
            Field.Create("Text", "text", shoutout.Text, true, null)
        ];
        return new Form
        {
            Fields =
            [
                shoutoutFields,
                Field.Create("Amount", "invoice_amount", shoutout.Amount.ToString(CultureInfo.InvariantCulture), true, null)
            ]
        };
    }

    private async Task<PublicViewModel> GetPublicViewModel(AppData app, ShoutoutViewModel shoutout, List<ShoutoutViewModel>? shoutouts = null)
    {
        var store = app.StoreData;
        var storeBlob = store.GetStoreBlob();
        var settings = app.GetSettings<ShoutoutSettings>();
        return new PublicViewModel
        {
            AppId = app.Id,
            StoreName = store.StoreName,
            BrandColor = storeBlob.BrandColor,
            CssUrl = storeBlob.CssUrl == null ? null : await uriResolver.Resolve(Request.GetAbsoluteRootUri(), storeBlob.CssUrl),
            LogoUrl = storeBlob.LogoUrl == null ? null : await uriResolver.Resolve(Request.GetAbsoluteRootUri(), storeBlob.LogoUrl),
            StoreId = store.Id,
            Title = settings.Title,
            Currency = settings.Currency,
            Description = settings.Description,
            ShowHeader = settings.ShowHeader,
            ShowRelativeDate = settings.ShowRelativeDate,
            ButtonText = settings.ButtonText,
            MinAmount = settings.MinAmount,
            LightningAddress = settings.LightningAddressIdentifier,
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, storeBlob),
            LnurlEnabled = shoutoutService.IsLnurlEnabled(store),
            Shoutout = shoutout,
            Shoutouts = shoutouts
        };
    }

    private static string GetStoreDefaultCurrentIfEmpty(StoreBlob storeBlob, string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            currency = storeBlob.DefaultCurrency;
        }
        return currency.Trim().ToUpperInvariant();
    }

    private static string GetSearchTerm(AppData app) => $"appid:{app.Id}";

    private static string ListToString(string[] list) => string.Join(',', list);

    private static string[] StringToList(string? str) => !string.IsNullOrEmpty(str)
        ? str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : [];

    private StoreData? GetCurrentStore() => HttpContext.GetStoreData();

    private AppData? GetCurrentApp() => HttpContext.GetAppData();
}
