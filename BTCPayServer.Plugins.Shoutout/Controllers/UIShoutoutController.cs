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
using BTCPayServer.Plugins.Shoutout.Services;
using BTCPayServer.Plugins.Shoutout.ViewModels;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using NicolasDorier.RateLimits;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Shoutout.Controllers;

[AutoValidateAntiforgeryToken]
public class UIShoutoutController(
    AppService appService,
    ShoutoutService shoutoutService,
    CurrencyNameTable currencies,
    FormDataService formDataService,
    InvoiceRepository invoiceRepository,
    UIInvoiceController invoiceController)
    : Controller
{
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
            LnurlEnabled = shoutoutService.IsLnurlEnabled(store)
        };
        return View(vm);
    }

    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [HttpPost("{appId}/settings/shoutout")]
    public async Task<IActionResult> UpdateSettings(string appId, UpdateSettingsViewModel vm)
    {
        var app = GetCurrentApp();
        if (app == null)
            return NotFound();

        if (!ModelState.IsValid)
            return View(vm);

        var storeBlob = GetCurrentStore().GetStoreBlob();
        vm.Currency = GetStoreDefaultCurrentIfEmpty(storeBlob, vm.Currency);
        if (currencies.GetCurrencyData(vm.Currency, false) == null)
            ModelState.AddModelError(nameof(vm.Currency), "Invalid currency");

        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var settings = new ShoutoutSettings
        {
            Title = vm.Title,
            Currency = vm.Currency,
            Description = vm.Description,
            ShowHeader = vm.ShowHeader,
            ShowRelativeDate = vm.ShowRelativeDate,
            MinAmount = vm.MinAmount,
            ButtonText = vm.ButtonText,
            LightningAddressIdentifier = vm.LightningAddressIdentifier
        };

        app.Name = vm.AppName;
        app.Archived = vm.Archived;
        app.SetSettings(settings);
        await appService.UpdateOrCreateApp(app);
        TempData[WellKnownTempData.SuccessMessage] = "The settings have been updated";
        return RedirectToAction(nameof(UpdateSettings), new { appId });
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
            StoreId = new [] { store.Id },
            TextSearch = AppService.GetAppSearchTerm(app),
            StartDate = fs.GetFilterDate("startdate", 0),
            EndDate = fs.GetFilterDate("enddate", 0),
            Status = new[]
            {
                InvoiceState.ToString(InvoiceStatusLegacy.Paid),
                InvoiceState.ToString(InvoiceStatusLegacy.Confirmed),
                InvoiceState.ToString(InvoiceStatusLegacy.Complete)
            }
        };

        var invoices = await invoiceRepository.GetInvoices(invoiceQuery);
        var shoutouts = invoices
            .Select(i => i.Metadata.AdditionalData.TryGetValue("shoutout", out var shoutout)
                ? new ShoutoutViewModel
                {
                    Amount = i.PaidAmount.Net > 0 ? i.PaidAmount.Net : i.Price,
                    Currency = i.Currency,
                    Timestamp = i.InvoiceTime,
                    Name = shoutout["name"]?.ToString(),
                    Text = shoutout["text"]?.ToString()
                }
                : null)
            .Where(s => s != null && !string.IsNullOrEmpty(s.Text) && (settings.MinAmount == 0 ||
                                                                       (s.Amount >= settings.MinAmount && s.Currency.Equals(settings.Currency, StringComparison.OrdinalIgnoreCase))));

        var vm = GetPublicViewModel(app, new ShoutoutViewModel(), shoutouts.ToList());
        return View(vm);
    }

    [HttpPost("/")]
    [HttpPost("/apps/{appId}/shoutout")]
    [IgnoreAntiforgeryToken]
    [EnableCors(CorsPolicies.All)]
    [DomainMappingConstraint(ShoutoutApp.AppType)]
    [RateLimitsFilter(ZoneLimits.PublicInvoices, Scope = RateLimitsScope.RemoteAddress)]
    [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
    public async Task<IActionResult> Public(string appId, ShoutoutViewModel shoutout)
    {
        var app = await appService.GetApp(appId, ShoutoutApp.AppType, true);
        if (app == null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            var vm = GetPublicViewModel(app, shoutout);
            return View(vm);
        }

        var form = GetForm(shoutout);
        if (!formDataService.Validate(form, ModelState))
        {
            var vm = GetPublicViewModel(app, shoutout);
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
            request.AdditionalSearchTerms = new[] { AppService.GetAppSearchTerm(app) };
            var invoice = await invoiceController.CreateInvoiceCoreRaw(request, store,Request.GetAbsoluteRoot(), new List<string> { AppService.GetAppInternalTag(appId) });

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

    private Form GetForm(ShoutoutViewModel shoutout)
    {
        var shoutoutFields = Field.CreateFieldset();
        shoutoutFields.Name = "shoutout";
        shoutoutFields.Label = "Shoutout";
        shoutoutFields.Fields = new List<Field>
        {
            Field.Create("Name", "name", shoutout.Name, false, null),
            Field.Create("Text", "text", shoutout.Text, true, null),
        };
        return new Form
        {
            Fields = new List<Field>
            {
                shoutoutFields,
                Field.Create("Amount", "invoice_amount", shoutout.Amount.ToString(CultureInfo.InvariantCulture), true, null)
            }
        };
    }

    private PublicViewModel GetPublicViewModel(AppData app, ShoutoutViewModel shoutout = null, List<ShoutoutViewModel> shoutouts = null)
    {
        var store = app.StoreData;
        var storeBlob = store.GetStoreBlob();
        var settings = app.GetSettings<ShoutoutSettings>();
        return new PublicViewModel
        {
            AppId = app.Id,
            StoreName = store.StoreName,
            BrandColor = storeBlob.BrandColor,
            CssFileId = storeBlob.CssFileId,
            LogoFileId = storeBlob.LogoFileId,
            StoreId = store.Id,
            Title = settings.Title,
            Currency = settings.Currency,
            Description = settings.Description,
            ShowHeader = settings.ShowHeader,
            ShowRelativeDate = settings.ShowRelativeDate,
            ButtonText = settings.ButtonText,
            MinAmount = settings.MinAmount,
            LightningAddress = settings.LightningAddressIdentifier,
            LnurlEnabled = shoutoutService.IsLnurlEnabled(store),
            Shoutout = shoutout,
            Shoutouts = shoutouts
        };
    }

    private static string GetStoreDefaultCurrentIfEmpty(StoreBlob storeBlob, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            currency = storeBlob.DefaultCurrency;
        }
        return currency.Trim().ToUpperInvariant();
    }

    private static string GetSearchTerm(AppData app) => $"appid:{app.Id}";

    private StoreData GetCurrentStore() => HttpContext.GetStoreData();

    private AppData GetCurrentApp() => HttpContext.GetAppData();
}
