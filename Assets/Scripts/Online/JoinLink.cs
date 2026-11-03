using System.Text;

// The link a host shares: madang://join/CODE opens the app and joins that room. Some chat apps don't
// make custom links tappable, so the share text always carries the plain code too.
public static class JoinLink
{
    public const string Scheme = "madang";
    const string Prefix = Scheme + "://join/";
    const int MinCodeLength = 4;
    const int MaxCodeLength = 10;

    public static string Build(string code) => Prefix + Normalize(code);

    public static string ShareText(string code) =>
        $"{Localization.Text("Vào chơi Kéo Co với mình trên Sân Chơi!")}\n" +
        $"{Localization.Text("Mã phòng")}: {Normalize(code)}\n{Build(code)}";

    // Accepts a full link or a code typed by hand, in any case and with stray spaces.
    public static bool TryParse(string text, out string code)
    {
        code = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text.Trim();
        if (value.StartsWith(Scheme + ":", System.StringComparison.OrdinalIgnoreCase))
        {
            int slash = value.LastIndexOf('/');
            value = slash >= 0 ? value.Substring(slash + 1) : "";
            int query = value.IndexOfAny(new[] { '?', '#' });
            if (query >= 0) value = value.Substring(0, query);
        }

        value = Normalize(value);
        if (value.Length < MinCodeLength || value.Length > MaxCodeLength) return false;
        code = value;
        return true;
    }

    static string Normalize(string code)
    {
        var letters = new StringBuilder();
        foreach (char c in code ?? "")
            if (char.IsLetterOrDigit(c) && c < 128) letters.Append(char.ToUpperInvariant(c));
        return letters.ToString();
    }
}
