using System.Text.RegularExpressions;

// http://predicatet.blogspot.com/2009/04/improved-c-slug-generator-or-how-to.html
namespace BTCPayServer.Plugins.Shoutout.Extensions;

public static class StringExtensions
{
    public static string Slugify(this string phrase)
    {
        string str = phrase.ToLower();
        // invalid chars
        str = Regex.Replace(str, @"[^a-z0-9\s-]", "");
        // convert multiple spaces into one space
        str = Regex.Replace(str, @"\s+", " ").Trim();
        // cut and trim
        str = str[..(str.Length <= 45 ? str.Length : 45)].Trim();
        str = Regex.Replace(str, @"\s", "-"); // hyphens
        // convert multiple hyphens into one hyphen
        str = Regex.Replace(str, @"-+", "-");
        return str;
    }

    public static string StripHtml(this string str)
    {
        return Regex.Replace(str, "<.*?>", string.Empty);
    }
}
