using System;
using System.Linq;
using NUnit.Framework;

public class CupPickerTests
{
    static readonly string[] Story = { "NguoiGo", "KeoCo", "DaCau", "BanBi", "TachKeo", "LauTokbokki" };

    static bool AllPlayable(string scene) => true;

    [Test]
    public void AllGames_AreTheSixGamesInStoryOrder()
    {
        CollectionAssert.AreEqual(Story, CupPicker.AllGames.Select(game => game.Scene));
        CollectionAssert.AllItemsAreUnique(CupPicker.AllGames.Select(game => game.Scene));
        CollectionAssert.AreEqual(Story, CupPicker.StoryOrder);
    }

    [TestCase(CupKind.Mini, 3)]
    [TestCase(CupKind.Grand, 6)]
    public void GameCount_PerCup(CupKind kind, int count)
    {
        Assert.AreEqual(count, CupPicker.GameCount(kind));
    }

    [Test]
    public void Roll_PicksDistinctPlayableGames()
    {
        var playable = new[] { "KeoCo", "DaCau", "BanBi", "TachKeo" };
        for (int seed = 0; seed < 50; seed++)
        {
            var picked = CupPicker.Roll(new Random(seed), playable, 3);
            Assert.AreEqual(3, picked.Length);
            CollectionAssert.AllItemsAreUnique(picked);
            CollectionAssert.IsSubsetOf(picked, playable);
        }
    }

    [Test]
    public void Roll_SameSeedSameResult()
    {
        CollectionAssert.AreEqual(CupPicker.Roll(new Random(7), Story, 3), CupPicker.Roll(new Random(7), Story, 3));
    }

    [Test]
    public void Roll_IsFair()
    {
        var seen = Story.ToDictionary(scene => scene, scene => 0);
        var first = Story.ToDictionary(scene => scene, scene => 0);
        for (int seed = 0; seed < 600; seed++)
        {
            var picked = CupPicker.Roll(new Random(seed), Story, 3);
            foreach (var scene in picked) seen[scene]++;
            first[picked[0]]++;
        }

        foreach (var scene in Story)
        {
            Assert.Greater(seen[scene], 0, $"{scene} is never rolled");
            Assert.That(first[scene], Is.InRange(60, 140), $"{scene} comes first {first[scene]} times out of 600");
        }
    }

    [Test]
    public void Roll_WithTooFewPlayableGames_Throws()
    {
        Assert.Throws<ArgumentException>(() => CupPicker.Roll(new Random(1), new[] { "KeoCo", "DaCau" }, 3));
    }

    [Test]
    public void Shuffle_IsAPermutation()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var shuffled = CupPicker.Shuffle(new Random(seed), Story);
            CollectionAssert.AreEquivalent(Story, shuffled);
        }
    }

    [Test]
    public void IsValid_MiniNeedsExactlyThreeDistinctPlayableGames()
    {
        Assert.IsTrue(CupPicker.IsValid(CupKind.Mini, new[] { "BanBi", "KeoCo", "LauTokbokki" }, AllPlayable));
        Assert.IsFalse(CupPicker.IsValid(CupKind.Mini, new[] { "BanBi", "KeoCo" }, AllPlayable));
        Assert.IsFalse(CupPicker.IsValid(CupKind.Mini, new[] { "BanBi", "KeoCo", "DaCau", "NguoiGo" }, AllPlayable));
        Assert.IsFalse(CupPicker.IsValid(CupKind.Mini, new[] { "BanBi", "KeoCo", "KeoCo" }, AllPlayable));
        Assert.IsFalse(CupPicker.IsValid(CupKind.Mini, new[] { "BanBi", "KeoCo", "Podium" }, AllPlayable));
        Assert.IsFalse(CupPicker.IsValid(CupKind.Mini, new[] { "BanBi", "KeoCo", "DaCau" }, scene => scene != "DaCau"));
    }

    [Test]
    public void IsValid_GrandNeedsAllSixOnce()
    {
        Assert.IsTrue(CupPicker.IsValid(CupKind.Grand, Story, AllPlayable));
        Assert.IsTrue(CupPicker.IsValid(CupKind.Grand, Story.Reverse().ToArray(), AllPlayable));
        Assert.IsFalse(CupPicker.IsValid(CupKind.Grand, Story.Take(5).ToArray(), AllPlayable));
        Assert.IsFalse(CupPicker.IsValid(CupKind.Grand, Story.Take(5).Append("KeoCo").ToArray(), AllPlayable));
        Assert.IsFalse(CupPicker.IsValid(CupKind.Grand, Story.Take(5).Append("Unknown").ToArray(), AllPlayable));
        Assert.IsFalse(CupPicker.IsValid(CupKind.Grand, Story, scene => scene != "TachKeo"));
    }
}
