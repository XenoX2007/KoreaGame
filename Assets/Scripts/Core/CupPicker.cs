using System;
using System.Collections.Generic;
using System.Linq;

public enum CupKind
{
    Mini,
    Grand,
}

public readonly struct GameInfo
{
    public readonly string Scene;
    public readonly string NameVi;    // localized through Localization, like the menu cards
    public readonly string NameKo;
    public readonly string IconClass; // a card-icon--* class from Madang.uss

    public GameInfo(string scene, string nameVi, string nameKo, string iconClass)
    {
        Scene = scene;
        NameVi = nameVi;
        NameKo = nameKo;
        IconClass = iconClass;
    }
}

// The tournament's games and the cup screen's dice. Pure, so `playable` is passed in
// (Application.CanStreamedLevelBeLoaded in the game, a fake in tests).
public static class CupPicker
{
    // Story order, which is also the Grand Cup's order until the dice shuffles it.
    public static readonly GameInfo[] AllGames =
    {
        new GameInfo("NguoiGo", "Người Gỗ", "무궁화 꽃이 피었습니다", "card-icon--mugunghwa"),
        new GameInfo("KeoCo", "Kéo Co", "줄다리기", "card-icon--rope"),
        new GameInfo("DaCau", "Đá Cầu", "제기차기", "card-icon--shuttlecock"),
        new GameInfo("BanBi", "Bắn Bi", "구슬치기", "card-icon--marble"),
        new GameInfo("TachKeo", "Tách Kẹo", "뽑기", "card-icon--candy"),
        new GameInfo("LauTokbokki", "Lẩu Tokbokki", "즉석떡볶이", "card-icon--hotpot"),
    };

    public static string[] StoryOrder => AllGames.Select(game => game.Scene).ToArray();

    public static int GameCount(CupKind kind) => kind == CupKind.Mini ? 3 : 6;

    public static GameInfo Find(string scene) => AllGames.First(game => game.Scene == scene);

    public static bool IsKnown(string scene) => AllGames.Any(game => game.Scene == scene);

    // `count` distinct scenes from `playable`, in random order.
    public static string[] Roll(Random random, IReadOnlyList<string> playable, int count)
    {
        var pool = playable.Distinct().ToArray();
        if (pool.Length < count) throw new ArgumentException($"Only {pool.Length} playable games, {count} needed");

        // A partial Fisher-Yates shuffle: the first `count` slots end up a uniform random pick.
        for (int i = 0; i < count; i++)
        {
            int j = random.Next(i, pool.Length);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.Take(count).ToArray();
    }

    public static string[] Shuffle(Random random, IReadOnlyList<string> scenes) => Roll(random, scenes, scenes.Count);

    // The right number of distinct, known and playable games for the cup.
    public static bool IsValid(CupKind kind, IReadOnlyList<string> scenes, Func<string, bool> playable) =>
        scenes != null
        && scenes.Count == GameCount(kind)
        && scenes.Distinct().Count() == scenes.Count
        && scenes.All(scene => IsKnown(scene) && playable(scene));
}
