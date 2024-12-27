using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Plugins.Shoutout.ViewModels;

public class PublicViewModel : BasePagingViewModel
{
    public string? LogoUrl { get; init; }
    public string? CssUrl { get; init; }
    public string? BrandColor { get; init; }
    public string? StoreName { get; init; }
    public string? Title { get; init; }
    public string? AppId { get; init; }
    public string? Description { get; init; }
    public string? StoreId { get; init; }
    public string? Currency { get; init; }
    public string? LightningAddress { get; init; }
    public bool LnurlEnabled { get; init; }
    public override int CurrentPageCount => Shoutouts?.Count ?? 0;
    public bool ShowHeader { get; init; }
    public bool ShowRelativeDate { get; init; }
    public string? ButtonText { get; init; }
    public decimal MinAmount { get; init; }
    public bool CanManage { get; set; }
    public StoreBrandingViewModel? StoreBranding { get; init; }
    public List<ShoutoutViewModel>? Shoutouts { get; init; }
    public ShoutoutViewModel Shoutout { get; init; } = null!;
}

public class ShoutoutViewModel
{
    [Display(Name = "Name")]
    public string? Name { get; init; }

    [Required]
    [Display(Name = "Text")]
    public string? Text { get; init; }

    [Required]
    [Range(0.01, 2100000000000)]
    [Display(Name = "Amount")]
    public decimal Amount { get; init; }
    [Display(Name = "Currency")]
    public string? Currency { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    [Display(Name = "Invoice ID")]
    public string? InvoiceId { get; init; }
    public bool Hidden { get; set; }
}
