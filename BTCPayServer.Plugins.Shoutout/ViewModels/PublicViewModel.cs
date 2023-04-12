using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Models;

namespace BTCPayServer.Plugins.Shoutout.ViewModels;

public class PublicViewModel : BasePagingViewModel
{
    public string LogoFileId { get; set; }
    public string CssFileId { get; set; }
    public string BrandColor { get; set; }
    public string StoreName { get; set; }
    public string Title { get; set; }
    public string AppId { get; set; }
    public string Description { get; set; }
    public string StoreId { get; set; }
    public string Currency { get; set; }
    public ShoutoutViewModel Shoutout { get; set; }
    public List<ShoutoutViewModel> Shoutouts { get; set; }
    public override int CurrentPageCount => Shoutouts.Count;
    public bool ShowHeader { get; set; }
    public bool ShowRelativeDate { get; set; }
    public string ButtonText { get; set; }
    public decimal MinAmount { get; set; }
}

public class ShoutoutViewModel
{
    public string Name { get; set; }

    [Required]
    public string Text { get; set; }

    [Required]
    [Range(0.01, 2100000000000)]
    public decimal Amount { get; set; }
    public string Currency { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
