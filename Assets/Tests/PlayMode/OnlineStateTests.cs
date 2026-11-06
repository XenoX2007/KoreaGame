using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Online the host sends each game's state and the other phones copy it into their own model, so a
// copy must carry on exactly like the original.
public class OnlineStateTests
{
    const double Start = 10.0;
    const double Frame = 1.0 / 60.0;

    static void Copy(System.Action<System.IO.BinaryWriter> write, System.Action<System.IO.BinaryReader> read) =>
        StateIO.Unpack(StateIO.Pack(write), read);

    [Test]
    public void Bits_RoundTrip()
    {
        var bits = Enumerable.Range(0, 21).Select(i => i % 3 == 0 || i == 20).ToArray();
        var copy = new bool[bits.Length];
        StateIO.Unpack(StateIO.Pack(w => w.WriteBits(bits)), r => r.ReadBits(copy));
        CollectionAssert.AreEqual(bits, copy);
    }

    [Test]
    public void NguoiGo_SameSeed_GivesEveryPhoneTheSameCaller()
    {
        var host = new NguoiGoModel(4, new NguoiGoSettings(), Start, 42);
        var phone = new NguoiGoModel(4, new NguoiGoSettings(), Start, 42);
        host.SetHeld(1, true); // runners never change the caller's timeline

        for (double time = Start; time < Start + 40.0; time += Frame)
        {
            host.Tick(time, (float)Frame);
            phone.Tick(time, 0f);
            Assert.AreEqual(host.Phase, phone.Phase, $"at {time - Start:0.00} s");
            Assert.AreEqual(host.SungText(time), phone.SungText(time));
            if (host.IsOver) break;
        }
    }

    [Test]
    public void DaCau_CopiedState_MatchesTheHost()
    {
        var settings = new DaCauSettings();
        var host = new DaCauModel(4, settings, Start, 3);
        double time = Start;
        // Let the first serve drop, then kick the next one on time.
        while (host.LivesOf(host.To) == settings.startingLives && time < Start + 10.0) host.Tick(time += Frame);
        while (!host.IsFlying) host.Tick(time += Frame);
        Assert.AreNotEqual(KickResult.Ignored, host.Tap(host.To, host.Arrival));

        var phone = new DaCauModel(4, settings, Start, 99);
        Copy(host.WriteState, phone.ReadState);

        Assert.AreEqual(host.From, phone.From);
        Assert.AreEqual(host.To, phone.To);
        Assert.AreEqual(host.Arrival, phone.Arrival);
        Assert.AreEqual(host.ZoneStart, phone.ZoneStart);
        Assert.AreEqual(host.Rally, phone.Rally);
        Assert.AreEqual(host.Kind, phone.Kind);
        for (int i = 0; i < 4; i++) Assert.AreEqual(host.LivesOf(i), phone.LivesOf(i));
    }

    [Test]
    public void BanBi_CopyMidRoll_RollsOnExactlyLikeTheHost()
    {
        var settings = new BanBiSettings();
        var host = new BanBiModel(settings, 2, 7);
        host.Start(Start);
        double time = Start;
        while (host.Phase == BanBiPhase.DiceRoll) host.Advance(time += Frame);

        int player = host.ActivePlayer;
        host.Press(player, true, time);
        host.Press(player, false, time + settings.chargeSeconds);
        time += settings.chargeSeconds;
        host.Advance(time += 0.2);
        Assert.AreEqual(BanBiPhase.Rolling, host.Phase);

        var phone = new BanBiModel(settings, 2, 7);
        phone.Start(Start);
        Copy(host.WriteState, phone.ReadState);

        for (int frame = 0; frame < 600; frame++)
        {
            time += Frame;
            host.Advance(time);
            phone.Advance(time);
        }

        Assert.AreEqual(host.Phase, phone.Phase);
        Assert.AreEqual(host.ActivePlayer, phone.ActivePlayer);
        Assert.AreEqual(host.Marbles.Count, phone.Marbles.Count);
        for (int i = 0; i < host.Marbles.Count; i++)
        {
            Assert.AreEqual(host.Marbles[i].Position, phone.Marbles[i].Position, $"marble {i}");
            Assert.AreEqual(host.Marbles[i].InPlay, phone.Marbles[i].InPlay, $"marble {i}");
        }
        Assert.AreEqual(host.TeamCount(0), phone.TeamCount(0));
        Assert.AreEqual(host.TeamCount(1), phone.TeamCount(1));
    }

    [Test]
    public void BanBi_CopyHoldsFewerMarbles_DropsTheExtraOnes()
    {
        var settings = new BanBiSettings();
        var host = new BanBiModel(settings, 2, 7);
        var phone = new BanBiModel(settings, 2, 7);
        host.Start(Start);
        phone.Start(Start);
        double time = Start;
        while (phone.Phase == BanBiPhase.DiceRoll) phone.Advance(time += Frame);
        phone.Press(phone.ActivePlayer, true, time);
        phone.Press(phone.ActivePlayer, false, time + 0.5);
        Assert.Greater(phone.Marbles.Count, host.Marbles.Count);

        Copy(host.WriteState, phone.ReadState);

        Assert.AreEqual(host.Marbles.Count, phone.Marbles.Count);
        Assert.AreEqual(BanBiPhase.DiceRoll, phone.Phase);
    }

    [Test]
    public void TachKeo_AnotherPhonesCandy_ArrivesWhole_AndIsLeftAlone()
    {
        var settings = new TachKeoSettings();
        var owner = new TachKeoModel(settings, 2, 5);
        var watcher = new TachKeoModel(settings, 2, 5);
        owner.Start(Start);
        watcher.Start(Start);
        watcher.SetSimulated(0, false);

        // Push hard until the candy breaks.
        double time = Start;
        while (owner.StateOf(0) != CutState.Broken && time < Start + settings.roundSeconds)
            owner.SetStick(0, Vector2.right, time += Frame);
        Assert.AreEqual(CutState.Broken, owner.StateOf(0));

        Copy(w => owner.WritePlayer(0, w), r => watcher.ReadPlayer(0, r));
        Assert.AreEqual(CutState.Broken, watcher.StateOf(0));
        Assert.AreEqual(owner.NeedleOf(0), watcher.NeedleOf(0));
        Assert.AreEqual(owner.BreaksOf(0), watcher.BreaksOf(0));

        // Only the owner's phone restarts it.
        watcher.Advance(time + settings.restartDelay + 0.5);
        Assert.AreEqual(CutState.Broken, watcher.StateOf(0));
    }

    [Test]
    public void TachKeo_CarvedLine_ArrivesWhole()
    {
        var settings = new TachKeoSettings();
        var owner = new TachKeoModel(settings, 2, 5);
        var watcher = new TachKeoModel(settings, 2, 5);
        owner.Start(Start);
        watcher.Start(Start);

        // Follow the outline gently for a second.
        double time = Start;
        var outline = owner.OutlineOf(0);
        for (int frame = 0; frame < 60; frame++)
        {
            var needle = owner.NeedleOf(0);
            int next = Enumerable.Range(0, outline.Count).OrderBy(i => (outline[i] - needle).sqrMagnitude).First() + 3;
            var direction = (outline[next % outline.Count] - needle).normalized * 0.5f;
            owner.SetStick(0, direction, time += Frame);
        }
        Assert.Greater(owner.ProgressOf(0), 0f);

        Copy(w => owner.WritePlayer(0, w), r => watcher.ReadPlayer(0, r));
        Assert.AreEqual(owner.ProgressOf(0), watcher.ProgressOf(0));
        for (int i = 0; i < owner.SegmentCount; i++) Assert.AreEqual(owner.IsCarved(0, i), watcher.IsCarved(0, i));
    }

    [Test]
    public void LauTokbokki_CopiedState_IsTheSame_AndWalksOnTheSame()
    {
        var settings = new LauTokbokkiSettings();
        var host = new LauTokbokkiModel(settings, 4, 11);
        var phone = new LauTokbokkiModel(settings, 4, 11);
        host.Start(Start);
        phone.Start(Start);

        Vector2 Stick(int player) => new Vector2(player % 2 == 0 ? 1f : -1f, player < 2 ? 0.4f : -0.4f).normalized;
        double time = Start;
        for (int frame = 0; frame < 180; frame++)
        {
            time += Frame;
            for (int p = 0; p < 4; p++) host.SetStick(p, Stick(p), time);
        }

        byte[] state = StateIO.Pack(host.WriteState);
        StateIO.Unpack(state, phone.ReadState);
        CollectionAssert.AreEqual(state, StateIO.Pack(phone.WriteState));

        for (int frame = 0; frame < 30; frame++)
        {
            time += Frame;
            host.Advance(time);
            phone.Advance(time);
        }
        for (int p = 0; p < 4; p++)
        {
            Assert.AreEqual(host.PositionOf(p), phone.PositionOf(p), $"P{p + 1}");
            Assert.AreEqual(host.CarriedBy(p), phone.CarriedBy(p), $"P{p + 1}");
            Assert.AreEqual(host.CorrectCount(p), phone.CorrectCount(p), $"P{p + 1}");
        }
    }
}
