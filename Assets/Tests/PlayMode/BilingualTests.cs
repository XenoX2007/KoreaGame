using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class BilingualTests
{
    [TestCase(0.0, false)]
    [TestCase(4.9, false)]
    [TestCase(5.0, true)]
    [TestCase(9.9, true)]
    [TestCase(10.0, false)]
    public void LanguageSwitchesEveryFiveSeconds(double time, bool korean)
    {
        Assert.AreEqual(korean, Bilingual.IsKoreanAt(time));
    }

    [Test]
    public void TextWithoutAPartnerStaysAsItIs()
    {
        Assert.IsFalse(Bilingual.HasAlternate("Perfect!"));
        Assert.AreEqual("Perfect!", Bilingual.Text("Perfect!"));
    }

#if UNITY_EDITOR
    // A new line of UI text that is missing from the table would silently stay in one language.
    [Test]
    public void EveryTextInTheScreensHasAPartner()
    {
        var pattern = new Regex("\\btext=\"([^\"]+)\"");
        var missing = AssetDatabase.FindAssets("t:VisualTreeAsset", new[] { "Assets/UI" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .SelectMany(path => pattern.Matches(System.IO.File.ReadAllText(path)).Cast<Match>())
            .Select(match => System.Net.WebUtility.HtmlDecode(match.Groups[1].Value))
            .Where(text => !Bilingual.HasAlternate(text))
            .ToArray();

        CollectionAssert.IsEmpty(missing);
    }

    // Letters that spill onto a second atlas are silently dropped from the text, so new Korean text
    // needs the font rebuilt: see Korean text in CLAUDE.md.
    [Test]
    public void EveryKoreanLetterIsInTheFontsOnlyAtlas()
    {
        var font = AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>("Assets/UI/Fonts/JuaSDF.asset");
        var sources = System.IO.Directory.GetFiles("Assets/Scripts", "*.cs", System.IO.SearchOption.AllDirectories)
            .Concat(System.IO.Directory.GetFiles("Assets/UI", "*.uxml", System.IO.SearchOption.AllDirectories));
        var letters = sources
            .SelectMany(System.IO.File.ReadAllText)
            .Where(letter => letter >= 0xAC00 && letter <= 0xD7A3)
            .Distinct()
            .ToArray();

        font.HasCharacters(new string(letters), out uint[] missing, false, false);

        CollectionAssert.IsEmpty(missing ?? new uint[0]);
        Assert.AreEqual(1, font.atlasTextures.Length);
    }
#endif
}
