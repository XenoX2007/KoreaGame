using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class LocalizationTests
{
    Language _before;

    [SetUp]
    public void RememberLanguage() => _before = Localization.Current;

    // Choosing a language saves it, so put back the player's own choice.
    [TearDown]
    public void RestoreLanguage() => Localization.Current = _before;

    [TestCase(Language.Vietnamese, "Giải đấu")]
    [TestCase(Language.Korean, "토너먼트")]
    [TestCase(Language.English, "Tournament")]
    public void TextIsInTheChosenLanguage(Language language, string expected)
    {
        Localization.Current = language;
        Assert.AreEqual(expected, Localization.Text("Giải đấu"));
    }

    [Test]
    public void TheChoiceIsSaved()
    {
        Localization.Current = Language.Korean;
        Assert.AreEqual((int)Language.Korean, PlayerPrefs.GetInt("language"));
    }

    [TestCase(SystemLanguage.Vietnamese, Language.Vietnamese)]
    [TestCase(SystemLanguage.Korean, Language.Korean)]
    [TestCase(SystemLanguage.French, Language.English)]
    public void FirstRunFollowsTheDevice(SystemLanguage device, Language expected)
    {
        Assert.AreEqual(expected, Localization.FromSystem(device));
    }

    [Test]
    public void TextWithoutATranslationStaysAsItIs()
    {
        Localization.Current = Language.English;
        Assert.IsFalse(Localization.HasTranslation("Perfect!"));
        Assert.AreEqual("Perfect!", Localization.Text("Perfect!"));
    }

    [Test]
    public void EveryLanguageHasANativeName()
    {
        Assert.AreEqual(System.Enum.GetValues(typeof(Language)).Length, Localization.NativeNames.Length);
    }

    [Test]
    public void EveryFunFactComesInEveryLanguage()
    {
        foreach (var facts in new[] { KeoCoGame.Facts, NguoiGoGame.Facts, DaCauGame.Facts })
        {
            Assert.AreEqual(3, facts.Length);
            StringAssert.StartsWith("Bạn có biết?", facts[(int)Language.Vietnamese]);
            StringAssert.StartsWith("알고 있나요?", facts[(int)Language.Korean]);
            StringAssert.StartsWith("Did you know?", facts[(int)Language.English]);
        }
    }

#if UNITY_EDITOR
    // A new line of UI text that is missing from the table would silently stay in Vietnamese.
    [Test]
    public void EveryTextInTheScreensHasATranslation()
    {
        var pattern = new Regex("\\btext=\"([^\"]+)\"");
        var missing = AssetDatabase.FindAssets("t:VisualTreeAsset", new[] { "Assets/UI" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .SelectMany(path => pattern.Matches(System.IO.File.ReadAllText(path)).Cast<Match>())
            .Select(match => System.Net.WebUtility.HtmlDecode(match.Groups[1].Value))
            .Where(text => !Localization.HasTranslation(text))
            .ToArray();

        CollectionAssert.IsEmpty(missing);
    }

    // The Korean font fills its atlas at runtime, and letters that spill onto a second atlas page are
    // silently dropped from the text. So every Hangul letter in the game must fit on one page: this
    // fills a fresh copy of the font with the same settings, leaving the real asset untouched.
    [Test]
    public void EveryKoreanLetterFitsOnOneAtlasPage()
    {
        var jua = AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>("Assets/UI/Fonts/JuaSDF.asset");
        var sources = System.IO.Directory.GetFiles("Assets/Scripts", "*.cs", System.IO.SearchOption.AllDirectories)
            .Concat(System.IO.Directory.GetFiles("Assets/UI", "*.uxml", System.IO.SearchOption.AllDirectories));
        var letters = sources
            .SelectMany(System.IO.File.ReadAllText)
            .Where(letter => letter >= 0xAC00 && letter <= 0xD7A3)
            .Distinct()
            .ToArray();

        var fresh = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(
            jua.sourceFontFile, (int)jua.faceInfo.pointSize, jua.atlasPadding, jua.atlasRenderMode,
            jua.atlasWidth, jua.atlasHeight, UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic, true);
        try
        {
            Assert.IsTrue(fresh.TryAddCharacters(new string(letters), out string missing), $"Not in the font: {missing}");
            Assert.AreEqual(1, fresh.atlasTextures.Length, $"{letters.Length} Hangul letters need a bigger atlas");
        }
        finally
        {
            foreach (var texture in fresh.atlasTextures) Object.DestroyImmediate(texture);
            Object.DestroyImmediate(fresh.material);
            Object.DestroyImmediate(fresh);
        }
    }
#endif
}
