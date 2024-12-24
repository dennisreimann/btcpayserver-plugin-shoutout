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
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.Shoutout.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using LNURL;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Shoutout.Controllers.API;

[ApiController]
[Route("~/api/v1/shoutout/lnurl")]
public class LnurlController(
    AppService appService,
    ShoutoutService shoutoutService,
    UIInvoiceController invoiceController,
    InvoiceRepository invoiceRepository,
    EventAggregator eventAggregator,
    LinkGenerator linkGenerator,
    IPluginHookService pluginHookService,
    PaymentMethodHandlerDictionary pmHandlers)
    : ControllerBase
{
    [HttpGet("{appId}/pay")]
    public async Task<IActionResult> LnurlPay(string appId)
    {
        var app = await appService.GetApp(appId, ShoutoutApp.AppType, true);
        if (app == null)
            return BadRequest(GetError("The app was not found"));

        var store = app.StoreData;
        if (!shoutoutService.IsLnurlEnabled(store))
            return NotFound("Lightning and LNURL must be enabled");

        var settings = app.GetSettings<ShoutoutSettings>();
        var title = settings.Title ?? app.Name;
        var metadata = new List<string[]> { new[] { "text/plain", title } };
        var payRequest = shoutoutService.GetLnurlPayRequest(Request, app.Id, metadata);

        return Ok(payRequest);
    }

    [HttpGet("{appId}/pay-callback")]
    public async Task<IActionResult> LnurlPayCallback(string appId, [FromQuery] long? amount = null, string? comment = null, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetApp(appId, ShoutoutApp.AppType, true);
        if (app == null)
            return BadRequest(GetError("The app was not found"));

        var store = app.StoreData;
        if (!shoutoutService.IsLnurlEnabled(store))
            return NotFound("Lightning and LNURL (including LUD-12 comment support) must be enabled");

        var settings = app.GetSettings<ShoutoutSettings>();
        var title = settings.Title ?? app.Name;
        var metadata = new List<string[]> { new[] { "text/plain", title } };
        string? lnAddress = null;
        if (!string.IsNullOrEmpty(settings.LightningAddressIdentifier))
        {
            lnAddress = $"{settings.LightningAddressIdentifier}@{Request.Host}";
            metadata.Add(["text/identifier", lnAddress]);
        }
        var payRequest = shoutoutService.GetLnurlPayRequest(Request, app.Id, metadata);
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
            var orderUrl = linkGenerator.GetUriByAction(
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
            var network = shoutoutService.Network;
            var pmi = shoutoutService.GetLnurlPaymentMethodId(store, out var lnurlSettings);
            var lnConfig = pmHandlers.GetLightningConfig(store, network);
            if (pmi is null || lnConfig is null || lnurlSettings is null) return NotFound("Lightning and LNURL must be enabled");

            var request = new CreateInvoiceRequest
            {
                Checkout = new InvoiceDataBase.CheckoutOptions
                {
                    PaymentMethods = [pmi.ToString()],
                    RedirectURL = orderUrl
                },
                Currency = "SATS",
                Amount = lightMoney.ToUnit(LightMoneyUnit.Satoshi),
                Metadata = invoiceMetadata,
                AdditionalSearchTerms = [AppService.GetAppSearchTerm(app)]
            };

            var invoice = await invoiceController.CreateInvoiceCoreRaw(request, store,Request.GetAbsoluteRoot(),
                [AppService.GetAppInternalTag(appId)], cancellationToken);
            var lightningPaymentMethod = invoice.GetPaymentPrompt(pmi);
            var handler = (LNURLPayPaymentHandler)pmHandlers[pmi];
            var promptDetails = handler.ParsePaymentPromptDetails(lightningPaymentMethod!.Details);
            promptDetails.PayRequest = payRequest;
            promptDetails.ProvidedComment = comment;

            if (await pluginHookService.ApplyFilter("modify-lnurlp-description", promptDetails.PayRequest.Metadata) is not string description)
                return NotFound(GetError("LNURL pay request metadata is not valid"));

            var storeBlob = store.GetStoreBlob();
            try
            {
                var client = pmHandlers.GetLightningHandler(network).CreateLightningClient(lnConfig);
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

                promptDetails.InvoiceId = lnInvoice.Id;
                promptDetails.PaymentHash = string.IsNullOrEmpty(lnInvoice.PaymentHash) ? null : uint256.Parse(lnInvoice.PaymentHash);
                promptDetails.Preimage = string.IsNullOrEmpty(lnInvoice.Preimage) ? null : uint256.Parse(lnInvoice.Preimage);
                promptDetails.GeneratedBoltAmount = lightMoney;
                promptDetails.ConsumedLightningAddress = lnAddress;
                lightningPaymentMethod.Destination = lnInvoice.BOLT11;
                lightningPaymentMethod.Details = JToken.FromObject(promptDetails, handler.Serializer);
            }
            catch (Exception ex)
            {
                var msg = string.IsNullOrEmpty(ex.Message) ? "" : $": {ex.Message}";
                return BadRequest(GetError($"Could not generate invoice with description hash{msg}"));
            }

            await invoiceRepository.UpdatePrompt(invoice.Id, lightningPaymentMethod);
            eventAggregator.Publish(new InvoiceNewPaymentDetailsEvent(invoice.Id, promptDetails, pmi));

            LNURLPayRequest.LNURLPayRequestCallbackResponse.ILNURLPayRequestSuccessAction? successAction = null;
            if ((invoice.ReceiptOptions?.Enabled ?? storeBlob.ReceiptOptions.Enabled) is true)
            {
                successAction = new LNURLPayRequest.LNURLPayRequestCallbackResponse.LNURLPayRequestSuccessActionUrl
                {
                    Tag = "url",
                    Description = "Thanks for your shoutout! Here is your receipt",
                    Url = linkGenerator.GetUriByAction(
                        nameof(UIInvoiceController.InvoiceReceipt),
                        nameof(UIInvoiceController).TrimEnd("Controller", StringComparison.InvariantCulture),
                        new { invoiceId = invoice.Id }, Request.Scheme, Request.Host, Request.PathBase)
                };
            }
            return Ok(new LNURLPayRequest.LNURLPayRequestCallbackResponse
            {
                Disposable = true,
                Routes = [],
                Pr = lightningPaymentMethod.Destination,
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
