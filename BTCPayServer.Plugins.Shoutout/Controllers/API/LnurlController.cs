using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.Shoutout.Services;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using LNURL;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Shoutout.Controllers.API;

[ApiController]
[Route("~/api/v1/shoutout/lnurl")]
public class LnurlController : ControllerBase
{
    private readonly AppService _appService;
    private readonly ShoutoutService _shoutoutService;
    private readonly UIInvoiceController _invoiceController;
    private readonly InvoiceRepository _invoiceRepository;
    private readonly EventAggregator _eventAggregator;
    private readonly LightningLikePaymentHandler _lightningLikePaymentHandler;
    private readonly LinkGenerator _linkGenerator;
    private readonly IPluginHookService _pluginHookService;
    private readonly InvoiceActivator _invoiceActivator;

    public LnurlController(
        AppService appService,
        ShoutoutService shoutoutService,
        UIInvoiceController invoiceController,
        InvoiceRepository invoiceRepository,
        EventAggregator eventAggregator,
        LightningLikePaymentHandler lightningLikePaymentHandler,
        LinkGenerator linkGenerator,
        IPluginHookService pluginHookService,
        InvoiceActivator invoiceActivator)
    {
        _appService = appService;
        _shoutoutService = shoutoutService;
        _invoiceController = invoiceController;
        _invoiceRepository = invoiceRepository;
        _eventAggregator = eventAggregator;
        _lightningLikePaymentHandler = lightningLikePaymentHandler;
        _linkGenerator = linkGenerator;
        _pluginHookService = pluginHookService;
        _invoiceActivator = invoiceActivator;
    }

    [HttpGet("{appId}/pay")]
    public async Task<IActionResult> LnurlPay(string appId)
    {
        var app = await _appService.GetApp(appId, ShoutoutApp.AppType, true);
        if (app == null)
            return BadRequest(GetError("The app was not found"));

        var store = app.StoreData;
        if (!_shoutoutService.IsLnurlEnabled(store))
            return NotFound("Lightning and LNURL must be enabled");

        var settings = app.GetSettings<ShoutoutSettings>();
        var title = settings.Title ?? app.Name;
        var metadata = new List<string[]> { new[] { "text/plain", title } };
        var payRequest = _shoutoutService.GetLnurlPayRequest(Request, app.Id, metadata);

        return Ok(payRequest);
    }

    [HttpGet("{appId}/pay-callback")]
    public async Task<IActionResult> LnurlPayCallback(string appId, [FromQuery] long? amount = null, string comment = null, CancellationToken cancellationToken = default)
    {
        var app = await _appService.GetApp(appId, ShoutoutApp.AppType, true);
        if (app == null)
            return BadRequest(GetError("The app was not found"));

        var store = app.StoreData;
        if (!_shoutoutService.IsLnurlEnabled(store))
            return NotFound("Lightning and LNURL (including LUD-12 comment support) must be enabled");

        var settings = app.GetSettings<ShoutoutSettings>();
        var title = settings.Title ?? app.Name;
        var metadata = new List<string[]> { new[] { "text/plain", title } };
        string lnAddress = null;
        if (!string.IsNullOrEmpty(settings.LightningAddressIdentifier))
        {
            lnAddress = $"{settings.LightningAddressIdentifier}@{Request.Host}";
            metadata.Add(new[] { "text/identifier", lnAddress });
        }
        var payRequest = _shoutoutService.GetLnurlPayRequest(Request, app.Id, metadata);
        if (amount is null)
        {
            return Ok(payRequest);
        }

        var lightMoney = new LightMoney(amount.Value);
        if (lightMoney < ShoutoutService.MinSendable || lightMoney > ShoutoutService.MaxSendable)
        {
            return BadRequest(GetError("Amount is out of bounds"));
        }

        comment = comment?.Truncate(ShoutoutService.CommentLength);
        try
        {
            var orderUrl = _linkGenerator.GetUriByAction(
                nameof(UIShoutoutController.Public),
                nameof(UIShoutoutController).TrimEnd("Controller", StringComparison.InvariantCulture),
                new { appId = app.Id }, Request.Scheme, Request.Host, Request.PathBase);
            var invoiceMetadata = new InvoiceMetadata
            {
                ItemCode = ShoutoutApp.ItemCode,
                ItemDesc = title,
                OrderId = AppService.GetRandomOrderId(),
                OrderUrl = orderUrl,
                AdditionalData = new JObject
                {
                    { "shoutout", new JObject { { "text", comment } } }
                }
            }.ToJObject();
            var pmi = _shoutoutService.GetLnurlPaymentMethodId(store, out var lnurlSettings);
            var request = new CreateInvoiceRequest
            {
                Checkout = new InvoiceDataBase.CheckoutOptions
                {
                    PaymentMethods = new[] { pmi.ToStringNormalized() },
                    RedirectURL = orderUrl
                },
                Currency = "SATS",
                Amount = lightMoney.ToUnit(LightMoneyUnit.Satoshi),
                Metadata = invoiceMetadata,
                AdditionalSearchTerms = new[] { AppService.GetAppSearchTerm(app) }
            };

            var invoice = await _invoiceController.CreateInvoiceCoreRaw(request, store,Request.GetAbsoluteRoot(), new List<string> { AppService.GetAppInternalTag(appId) }, cancellationToken);
            var lightningPaymentMethod = invoice.GetPaymentMethod(pmi);
            var paymentMethodDetails = lightningPaymentMethod?.GetPaymentMethodDetails() as LNURLPayPaymentMethodDetails;
            if (paymentMethodDetails?.LightningSupportedPaymentMethod is null)
                return NotFound("Lightning and LNURL must be enabled");

            paymentMethodDetails.PayRequest = payRequest;
            paymentMethodDetails.ProvidedComment = comment;

            if (await _pluginHookService.ApplyFilter("modify-lnurlp-description", paymentMethodDetails.PayRequest.Metadata) is not string description)
                return NotFound(GetError("LNURL pay request metadata is not valid"));

            var storeBlob = store.GetStoreBlob();
            try
            {
                var network = _shoutoutService.Network;
                var client = _lightningLikePaymentHandler.CreateLightningClient(paymentMethodDetails.LightningSupportedPaymentMethod, network);
                var expiry = invoice.ExpirationTime.ToUniversalTime() - DateTimeOffset.UtcNow;
                var param = new CreateInvoiceParams(lightMoney, description, expiry)
                {
                    PrivateRouteHints = storeBlob.LightningPrivateRouteHints,
                    DescriptionHashOnly = true
                };

                var lnInvoice = await client.CreateInvoice(param, cancellationToken);
                if (!BOLT11PaymentRequest.Parse(lnInvoice.BOLT11, network.NBitcoinNetwork).VerifyDescriptionHash(description))
                {
                    return BadRequest(GetError("Could not generate invoice with a valid description hash"));
                }

                paymentMethodDetails.InvoiceId = lnInvoice.Id;
                paymentMethodDetails.BOLT11 = lnInvoice.BOLT11;
                paymentMethodDetails.PaymentHash = string.IsNullOrEmpty(lnInvoice.PaymentHash) ? null : uint256.Parse(lnInvoice.PaymentHash);
                paymentMethodDetails.Preimage = string.IsNullOrEmpty(lnInvoice.Preimage) ? null : uint256.Parse(lnInvoice.Preimage);
                paymentMethodDetails.GeneratedBoltAmount = lightMoney;
                paymentMethodDetails.ConsumedLightningAddress = lnAddress;
            }
            catch (Exception ex)
            {
                var msg = string.IsNullOrEmpty(ex.Message) ? "" : $": {ex.Message}";
                return BadRequest(GetError($"Could not generate invoice with description hash{msg}"));
            }

            lightningPaymentMethod.SetPaymentMethodDetails(paymentMethodDetails);
            await _invoiceRepository.UpdateInvoicePaymentMethod(invoice.Id, lightningPaymentMethod);
            _eventAggregator.Publish(new InvoiceNewPaymentDetailsEvent(invoice.Id, paymentMethodDetails, pmi));

            LNURLPayRequest.LNURLPayRequestCallbackResponse.ILNURLPayRequestSuccessAction successAction = null;
            if ((invoice.ReceiptOptions?.Enabled ?? storeBlob.ReceiptOptions.Enabled) is true)
            {
                successAction = new LNURLPayRequest.LNURLPayRequestCallbackResponse.LNURLPayRequestSuccessActionUrl
                {
                    Tag = "url",
                    Description = "Thanks for your shoutout! Here is your receipt",
                    Url = _linkGenerator.GetUriByAction(
                        nameof(UIInvoiceController.InvoiceReceipt),
                        nameof(UIInvoiceController).TrimEnd("Controller", StringComparison.InvariantCulture),
                        new { invoiceId = invoice.Id }, Request.Scheme, Request.Host, Request.PathBase)
                };
            }
            return Ok(new LNURLPayRequest.LNURLPayRequestCallbackResponse
            {
                Disposable = true,
                Routes = Array.Empty<string>(),
                Pr = paymentMethodDetails.BOLT11,
                SuccessAction = successAction
            });
        }
        catch (Exception)
        {
            return BadRequest(GetError("Invoice not in a valid payable state"));
        }
    }

    private static LNUrlStatusResponse GetError(string reason) => new()
    {
        Status = "ERROR",
        Reason = reason
    };
}
