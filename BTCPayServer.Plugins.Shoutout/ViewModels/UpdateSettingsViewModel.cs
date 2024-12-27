using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Shoutout.ViewModels
{
    public class UpdateSettingsViewModel
    {
        public string? StoreId { get; init; }
        public string? StoreName { get; init; }
        public string? StoreDefaultCurrency { get; init; }

        [Required]
        [MaxLength(50)]
        [MinLength(1)]
        [Display(Name = "App Name")]
        public string? AppName { get; init; }

        [Required]
        [MaxLength(30)]
        [Display(Name = "Display Title")]
        public string? Title { get; init; }
        [MaxLength(5)]
        [Display(Name = "Currency")]
        public string? Currency { get; set; }

        public string? Id { get; init; }
        public string? AppId { get; init; }
        public string? SearchTerm { get; init; }
        [Display(Name = "Description")]
        public string? Description { get; init; }

        [Display(Name = "Lightning Address Identifier")]
        public string? LightningAddressIdentifier { get; init; }
        [Display(Name = "Enable LNURL")]
        public bool LnurlEnabled { get; init; }
        [Display(Name = "Archived")]
        public bool Archived { get; init; }

        [Display(Name = "Show the store header")]
        public bool ShowHeader { get; init; }

        [Display(Name = "Show dates in relative format")]
        public bool ShowRelativeDate { get; init; }

        [Required]
        [Display(Name = "Button Text")]
        public string? ButtonText { get; init; }

        [Display(Name = "Minimum amount required for displaying the shoutout")]
        public decimal MinAmount { get; init; }

        [Display(Name = "Exclude invoices by ID")]
        public string? ExcludeInvoiceId { get; init; }
    }
}
