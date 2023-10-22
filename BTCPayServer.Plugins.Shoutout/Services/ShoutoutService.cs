using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.Shoutout.Controllers.API;
using LNURL;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Shoutout.Services;

public class ShoutoutService
{
    private const string CryptoCode = "BTC";
    internal const int CommentLength = 2000;
    internal static readonly LightMoney MinSendable = new(1, LightMoneyUnit.Satoshi);
    internal static readonly LightMoney MaxSendable = LightMoney.FromUnit(6.12m, LightMoneyUnit.BTC);

    private readonly LinkGenerator _linkGenerator;
    private readonly BTCPayNetworkProvider _networkProvider;

    public ShoutoutService(
        LinkGenerator linkGenerator,
        BTCPayNetworkProvider networkProvider)
    {
        _linkGenerator = linkGenerator;
        _networkProvider = networkProvider;
    }

    public BTCPayNetwork Network => _networkProvider.GetNetwork<BTCPayNetwork>(CryptoCode);

    public PaymentMethodId GetLnurlPaymentMethodId(StoreData store, out LNURLPaySupportedPaymentMethod lnurlSettings)
    {
        lnurlSettings = null;
        var pmi = new PaymentMethodId(CryptoCode, PaymentTypes.LNURLPay);
        var lnpmi = new PaymentMethodId(CryptoCode, PaymentTypes.LightningLike);
        var methods = store.GetSupportedPaymentMethods(_networkProvider);
        var lnUrlMethod = methods.FirstOrDefault(method => method.PaymentId == pmi) as LNURLPaySupportedPaymentMethod;
        var lnMethod = methods.FirstOrDefault(method => method.PaymentId == lnpmi);
        if (lnUrlMethod is null || lnMethod is null)
            return null;
        var blob = store.GetStoreBlob();
        if (blob.GetExcludedPaymentMethods().Match(pmi) || blob.GetExcludedPaymentMethods().Match(lnpmi))
            return null;
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
            Callback = new Uri(_linkGenerator.GetUriByAction(
                action: nameof(LnurlController.LnurlPayCallback),
                nameof(LnurlController).TrimEnd("Controller", StringComparison.InvariantCulture),
                values: new { appId }, request.Scheme, request.Host, request.PathBase) ?? string.Empty)
        };
    }

    public bool IsLnurlEnabled(StoreData store)
    {
        var pmi = GetLnurlPaymentMethodId(store, out var lnurlSettings);
        var isEnabled = pmi != null && lnurlSettings.LUD12Enabled;
        return isEnabled;
    }
}
