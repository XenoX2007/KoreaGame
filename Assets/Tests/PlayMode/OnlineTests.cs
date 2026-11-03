using NUnit.Framework;
using Unity.Services.Multiplayer;

public class OnlineTests
{
    [TestCase("madang://join/ABC123", "ABC123")]
    [TestCase("MADANG://join/abc123?from=zalo", "ABC123")]
    [TestCase("  abc 123 ", "ABC123")]
    [TestCase("WHF8K7", "WHF8K7")]
    public void JoinLink_ReadsLinksAndTypedCodes(string text, string code)
    {
        Assert.IsTrue(JoinLink.TryParse(text, out string parsed));
        Assert.AreEqual(code, parsed);
    }

    [TestCase("")]
    [TestCase("ab")]
    [TestCase("madang://join/")]
    [TestCase("ABCDEFGHIJKLMNOP")]
    public void JoinLink_RejectsWhatCannotBeACode(string text)
    {
        Assert.IsFalse(JoinLink.TryParse(text, out _));
    }

    [Test]
    public void JoinLink_RoundTripsThroughTheShareText()
    {
        string link = JoinLink.Build("abc123");
        Assert.AreEqual("madang://join/ABC123", link);
        StringAssert.Contains(link, JoinLink.ShareText("ABC123"));
        StringAssert.Contains("ABC123", JoinLink.ShareText("ABC123"));
    }

    [Test]
    public void Seats_HostIsP1_OthersByJoinOrder_TeamsAlternate()
    {
        var seats = OnlineSeats.Assign(0, new ulong[] { 7, 0, 3, 5 });

        Assert.AreEqual(0, seats[0]);
        Assert.AreEqual(1, seats[3]);
        Assert.AreEqual(2, seats[5]);
        Assert.AreEqual(3, seats[7]);
        Assert.AreNotEqual(SeatLayout.TeamOf(seats[0]), SeatLayout.TeamOf(seats[3]));
    }

    [TestCase(SessionError.SessionNotFound, "", "Không tìm thấy phòng")]
    [TestCase(SessionError.Unknown, "Lobby is full", "Phòng đã đủ người")]
    [TestCase(SessionError.Unknown, "Lobby is locked", "Phòng đã đủ người")]
    [TestCase(SessionError.NetworkSetupFailed, "timeout", "Không kết nối được")]
    public void Errors_BecomeLinesAPlayerUnderstands(SessionError error, string message, string expected)
    {
        string friendly = OnlineSession.FriendlyError(error, message);
        Assert.AreEqual(expected, friendly);
        Assert.IsTrue(Localization.HasTranslation(friendly));
    }

    // Online, the host judges a press by when it happened on the phone, not when it arrived.
    [Test]
    public void ATapArrivingLate_IsJudgedByWhenItHappened()
    {
        var onTime = new KeoCoModel(2, new KeoCoSettings(), 10.0);
        var late = new KeoCoModel(2, new KeoCoSettings(), 10.0);
        double beat = onTime.BeatTime(4);

        for (double now = 10.0; now < beat; now += 1.0 / 60.0) onTime.Tick(now, 1f / 60f);
        for (double now = 10.0; now < beat + 0.15; now += 1.0 / 60.0) late.Tick(now, 1f / 60f);

        Assert.AreEqual(TapJudgement.Perfect, onTime.Tap(1, beat).Judgement);
        Assert.AreEqual(TapJudgement.Perfect, late.Tap(1, beat).Judgement);
    }
}
