using NUnit.Framework;
using UnityEngine;

public class GameAudioTests
{
    [TestCase(GameAudio.MusicClip)]
    [TestCase(GameAudio.VictoryClip)]
    public void ClipIsInResources(string clip)
    {
        Assert.IsNotNull(Resources.Load<AudioClip>(clip));
    }

    [Test]
    public void MusicStartsByItself_AndThereIsSomethingToHearIt()
    {
        Assert.IsNotNull(GameAudio.Instance);
        Assert.IsTrue(GameAudio.Instance.IsMusicPlaying);
        Assert.IsNotNull(GameAudio.Instance.GetComponent<AudioListener>());
    }

    [Test]
    public void TheFanfarePlaysOnceAndStops()
    {
        GameAudio.PlayVictory();
        Assert.IsTrue(GameAudio.Instance.IsVictoryPlaying);

        GameAudio.StopVictory();
        Assert.IsFalse(GameAudio.Instance.IsVictoryPlaying);
    }

    [Test]
    public void MusicDipsForTheFanfare_ThenComesBack()
    {
        float volume = GameAudio.MusicVolume;
        for (int i = 0; i < 100; i++) volume = GameAudio.NextMusicVolume(volume, true, 0.016f);
        Assert.AreEqual(GameAudio.DuckedVolume, volume);

        for (int i = 0; i < 100; i++) volume = GameAudio.NextMusicVolume(volume, false, 0.016f);
        Assert.AreEqual(GameAudio.MusicVolume, volume);
    }
}
