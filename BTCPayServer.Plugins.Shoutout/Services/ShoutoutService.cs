using System;
using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.Shoutout.Controllers.API;
using BTCPayServer.Services.Invoices;
using LNURL;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Shoutout.Services;

public class ShoutoutService(
    LinkGenerator linkGenerator,
    BTCPayNetworkProvider networkProvider,
    PaymentMethodHandlerDictionary pmHandlers)
{
    private const string CryptoCode = "BTC";
    internal const int CommentLength = 2000;
    internal static readonly LightMoney MinSendable = new(1, LightMoneyUnit.Satoshi);
    internal static readonly LightMoney MaxSendable = LightMoney.FromUnit(6.12m, LightMoneyUnit.BTC);

    public BTCPayNetwork Network => networkProvider.GetNetwork<BTCPayNetwork>(CryptoCode);

    public PaymentMethodId? GetLnurlPaymentMethodId(StoreData store, out LNURLPaymentMethodConfig? lnurlSettings)
    {
        lnurlSettings = null;
        var pmi = PaymentTypes.LNURL.GetPaymentMethodId(CryptoCode);
        var lnpmi = PaymentTypes.LN.GetPaymentMethodId(CryptoCode);
        var lnUrlMethod = store.GetPaymentMethodConfig<LNURLPaymentMethodConfig>(pmi, pmHandlers);
        var lnMethod = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(lnpmi, pmHandlers);
        if (lnUrlMethod is null || lnMethod is null) return null;
        var blob = store.GetStoreBlob();
        if (blob.GetExcludedPaymentMethods().Match(pmi) || blob.GetExcludedPaymentMethods().Match(lnpmi)) return null;
        lnurlSettings = lnUrlMethod;
        return pmi;
    }

    public LNURLPayRequest GetLnurlPayRequest(HttpRequest request, string appId, List<string[]> metadata)
    {
        return new LNURLPayRequest
        {
            Tag = "payRequest",
            MinSendable = MinSendable,
            MaxSendable = MaxSendable,
            CommentAllowed = CommentLength,
            Metadata = JsonConvert.SerializeObject(metadata),
            Callback = new Uri(linkGenerator.GetUriByAction(
                action: nameof(LnurlController.LnurlPayCallback),
                nameof(LnurlController).TrimEnd("Controller", StringComparison.InvariantCulture),
                values: new { appId }, request.Scheme, request.Host, request.PathBase) ?? string.Empty)
        };
    }

    public bool IsLnurlEnabled(StoreData store)
    {
        GetLnurlPaymentMethodId(store, out var lnurlSettings);
        return lnurlSettings?.LUD12Enabled is true;
    }
}
