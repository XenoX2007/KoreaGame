using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public enum Language
{
    Vietnamese,
    Korean,
    English,
}

// All game text in the language the players picked on the menu, remembered on the device.
// UXML and code keep the Vietnamese text; its Korean and English versions live in this table.
// The menu's other-name lines (a game's name in the other country) stay as they are in English.
// Chants stay in their own language, because they are what each player is singing along to.
public static class Localization
{
    const string LanguageKey = "language";

    // Each language's name, written in that language.
    public static readonly string[] NativeNames = { "Tiếng Việt", "한국어", "English" };

    // Vietnamese -> { Korean, English }
    static readonly Dictionary<string, string[]> Translations = new Dictionary<string, string[]>
    {
        // Menu
        { "Sân Chơi", new[] { "마당", "Playground" } },
        { "마당 · Madang · 같이 놀자", new[] { "Sân Chơi · Madang · Cùng chơi nào", "Sân Chơi · 마당 · Let's play together" } },
        { "Bao nhiêu người chơi?", new[] { "몇 명이 놀까요?", "How many players?" } },
        { "Người Gỗ", new[] { "무궁화 놀이", "Statues" } },
        { "Hai trò khác tên, một luật chơi", new[] { "이름은 달라도 규칙은 하나", "Two names, one game" } },
        { "무궁화 꽃이 피었습니다", new[] { "Một hai ba người gỗ", "무궁화 꽃이 피었습니다" } },
        { "Kéo Co", new[] { "줄다리기", "Tug of War" } },
        { "Một di sản chung", new[] { "함께 나누는 유산", "A shared heritage" } },
        { "줄다리기", new[] { "Kéo Co", "줄다리기" } },
        { "Đá Cầu", new[] { "제기차기", "Shuttlecock Kick" } },
        { "Chơi cùng nhau, chuyền cầu cho nhau", new[] { "함께 제기를 주고받아요", "Play together, pass it around" } },
        { "제기차기", new[] { "Đá Cầu", "제기차기" } },
        { "Sắp có", new[] { "준비 중", "Coming soon" } },
        { "Giải đấu", new[] { "토너먼트", "Tournament" } },
        { "Chơi cả 3 trò, tính điểm, lên bục vinh quang", new[] { "세 가지 놀이로 점수를 모아 시상대에 올라요", "Play all 3 games, score points, reach the podium" } },
        { "Ngôn ngữ", new[] { "언어", "Language" } },

        // Results and podium
        { "Về menu", new[] { "메뉴로", "Menu" } },
        { "Chơi lại", new[] { "다시 하기", "Play again" } },
        { "Tiếp tục", new[] { "계속하기", "Continue" } },
        { "Hòa!", new[] { "무승부!", "Draw!" } },
        { "Bục vinh quang", new[] { "시상대", "Podium" } },

        // Shared in-game
        { "Đổi sang 2 đấu 2", new[] { "2대2로 바꾸기", "Switch to 2 vs 2" } },
        { "Đổi sang 1 đấu 1", new[] { "1대1로 바꾸기", "Switch to 1 vs 1" } },
        { "Tạm dừng", new[] { "일시 정지", "Paused" } },
        { "Về trang chủ", new[] { "홈으로", "Home" } },

        // Kéo Co
        { "Kéo!", new[] { "당겨!", "Pull!" } },
        { "Đồng lòng!", new[] { "한마음!", "In sync!" } },

        // Người Gỗ
        { "Chạy!", new[] { "달려!", "Run!" } },
        { "Đứng im!", new[] { "멈춰!", "Freeze!" } },
        { "Bị bắt!", new[] { "잡혔다!", "Caught!" } },

        // Đá Cầu
        { "Đá!", new[] { "차!", "Kick!" } },
        { "Sớm quá!", new[] { "너무 빨라!", "Too early!" } },
        { "Trượt!", new[] { "놓쳤다!", "Missed!" } },
        { "Hết mạng!", new[] { "탈락!", "Out!" } },
        { "Gió xuôi!", new[] { "순풍!", "Tailwind!" } },
        { "Gió ngược!", new[] { "역풍!", "Headwind!" } },
    };

    static Language? _current;

    // Domain reload is disabled in this project, so statics survive between play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _current = null;

    // The saved choice, or the device's language the first time the game runs.
    public static Language Current
    {
        get
        {
            if (_current == null)
                _current = PlayerPrefs.HasKey(LanguageKey) ? (Language)PlayerPrefs.GetInt(LanguageKey) : FromSystem(Application.systemLanguage);
            return _current.Value;
        }
        set
        {
            _current = value;
            PlayerPrefs.SetInt(LanguageKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    public static Language FromSystem(SystemLanguage language) =>
        language == SystemLanguage.Vietnamese ? Language.Vietnamese
        : language == SystemLanguage.Korean ? Language.Korean
        : Language.English;

    public static bool HasTranslation(string text) => Translations.ContainsKey(text);

    // The text in the current language; text without a translation stays as it is.
    public static string Text(string text) => Text(text, Current);

    public static string Text(string text, Language language) =>
        language != Language.Vietnamese && Translations.TryGetValue(text, out var translated)
            ? translated[(int)language - 1]
            : text;

    // Picks this language's line from a { Vietnamese, Korean, English } set.
    public static string Pick(string[] lines) => lines[(int)Current];

    public static string Won(string names)
    {
        switch (Current)
        {
            case Language.Korean: return $"{names} 승리!";
            case Language.English: return names.Contains("+") ? $"{names} win!" : $"{names} wins!";
            default: return $"{names} thắng!";
        }
    }

    public static string Points(int points)
    {
        switch (Current)
        {
            case Language.Korean: return $"{points}점";
            case Language.English: return points == 1 ? "1 point" : $"{points} points";
            default: return $"{points} điểm";
        }
    }

    // Keeps every label and button in a loaded panel in the current language, even after it changes.
    public static void Bind(VisualElement root) =>
        root.Query<TextElement>().ForEach(label =>
        {
            string text = label.text;
            if (!string.IsNullOrEmpty(text) && HasTranslation(text)) Track(label, () => Text(text));
        });

    public static void Track(TextElement label, Func<string> text)
    {
        label.text = text();
        label.schedule.Execute(() => label.text = text()).Every(100);
    }
}
