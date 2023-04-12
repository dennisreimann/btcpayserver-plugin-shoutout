using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Shoutout.ViewModels
{
    public class UpdateSettingsViewModel
    {
        public string StoreId { get; set; }
        public string StoreName { get; set; }
        public string StoreDefaultCurrency { get; set; }

        [Required]
        [MaxLength(50)]
        [MinLength(1)]
        [Display(Name = "App Name")]
        public string AppName { get; set; }

        [Required]
        [MaxLength(30)]
        [Display(Name = "Display Title")]
        public string Title { get; set; }
        [MaxLength(5)]
        public string Currency { get; set; }

        public string Id { get; set; }
        public string AppId { get; set; }
        public string SearchTerm { get; set; }
        public string Description { get; set; }
        public bool Archived { get; set; }

        [Display(Name = "Show the store header")]
        public bool ShowHeader { get; set; }

        [Display(Name = "Show dates in relative format")]
        public bool ShowRelativeDate { get; set; }

        [Required]
        [Display(Name = "Button Text")]
        public string ButtonText { get; set; }

        [Display(Name = "Minimum amount required for displaying the shoutout")]
        public decimal MinAmount { get; set; }
    }
}
