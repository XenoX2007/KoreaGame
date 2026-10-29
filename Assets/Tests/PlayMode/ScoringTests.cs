using NUnit.Framework;

public class ScoringTests
{
    // Exposes the protected ranking helper.
    abstract class RankProbe : MiniGameBase
    {
        public static int[] Ranks(params float[] scores) => RanksFromScores(scores);
    }

    [Test]
    public void RanksFromScores_HigherScoreRanksFirst()
    {
        CollectionAssert.AreEqual(new[] { 3, 1, 4, 2 }, RankProbe.Ranks(10f, 40f, 5f, 20f));
    }

    [Test]
    public void RanksFromScores_TiedPlayersShareRankAndSkipTheNext()
    {
        CollectionAssert.AreEqual(new[] { 1, 1, 3, 4 }, RankProbe.Ranks(9f, 9f, 4f, 1f));
    }

    [TestCase(1, 3)]
    [TestCase(2, 2)]
    [TestCase(3, 1)]
    [TestCase(4, 0)]
    public void PointsForRank_FollowsTournamentTable(int rank, int points)
    {
        Assert.AreEqual(points, GameManager.PointsForRank(rank));
    }
}
