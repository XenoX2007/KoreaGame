using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class BanBiModelTests
{
    const double Start = 10.0;
    const double Frame = 1.0 / 60.0;
    const int Seed = 7;

    static BanBiModel NewModel(int players, BanBiSettings settings = null, int seed = Seed)
    {
        var model = new BanBiModel(settings ?? new BanBiSettings(), players, seed);
        model.Start(Start);
        return model;
    }

    // Lets every die stop on its own and returns when the first turn starts.
    static double RollToFirstTurn(BanBiModel model)
    {
        double time = Start;
        while (model.Phase == BanBiPhase.DiceRoll && time < Start + 60.0)
        {
            time += Frame;
            model.Advance(time);
        }
        Assert.AreEqual(BanBiPhase.Aiming, model.Phase);
        return model.PhaseStart;
    }

    // Presses as aiming starts, so the hand is at x = 0, holds for this long, releases, then lets
    // everything roll to a stop. Returns the time it stopped.
    static double Shoot(BanBiModel model, double hold)
    {
        double time = model.PhaseStart;
        int player = model.ActivePlayer;
        model.Press(player, true, time);
        model.Press(player, false, time + hold);
        time += hold;
        while (model.Phase == BanBiPhase.Rolling)
        {
            time += Frame;
            model.Advance(time);
        }
        return time;
    }

    static double ShootFullPower(BanBiModel model, BanBiSettings settings) => Shoot(model, settings.chargeSeconds);

    // Moves every marble except these out of the way. They stay in play, so team counts don't change.
    static void ClearTheZone(BanBiModel model, params MarbleState[] keep)
    {
        int i = 0;
        foreach (var marble in model.Marbles)
            if (marble.InPlay && !keep.Contains(marble)) marble.Position = new Vector2(20f + 3f * i++, 20f);
    }

    // Puts one of this team's marbles straight in the shot's path and clears everything else.
    static MarbleState PlaceInPath(BanBiModel model, int team, float y = 0.7f)
    {
        var target = model.Marbles.First(marble => marble.InPlay && marble.Team == team);
        ClearTheZone(model, target);
        target.Position = new Vector2(0f, y);
        return target;
    }

    static void SkipTurns(BanBiModel model, BanBiSettings settings, int turns)
    {
        double time = model.PhaseStart;
        for (int turn = 0; turn < turns; turn++)
        {
            time += settings.turnSeconds;
            model.Advance(time);
        }
    }

    // A time during the first roll, before the dice stop on their own, when this player's die shows this face.
    static double TimeShowing(BanBiModel model, int player, int face)
    {
        for (double time = Start + 0.001; time < Start + 2.9; time += 0.01)
            if (model.DiceFace(player, time) == face) return time;
        Assert.Fail($"P{player + 1}'s die never showed {face}");
        return 0;
    }

    // Stops each player's die on the given face, pressing in time order.
    static void RollFaces(BanBiModel model, params int[] faces)
    {
        foreach (var (player, time) in faces.Select((face, player) => (player, TimeShowing(model, player, face))).OrderBy(press => press.Item2))
            model.Press(player, true, time);
    }

    // ---- Start ----

    [Test]
    public void Start_EachTeamHasItsMarbles_SpreadInsideTheStartArea()
    {
        var settings = new BanBiSettings();
        var model = NewModel(4, settings);

        Assert.AreEqual(settings.startMarblesPerTeam, model.TeamCount(0));
        Assert.AreEqual(settings.startMarblesPerTeam, model.TeamCount(1));
        foreach (var marble in model.Marbles)
        {
            Assert.LessOrEqual(Mathf.Abs(marble.Position.x), settings.startArea);
            Assert.LessOrEqual(Mathf.Abs(marble.Position.y), settings.startArea);
            Assert.AreEqual(-1, marble.ShotBy);
            foreach (var other in model.Marbles)
                if (other != marble)
                    Assert.GreaterOrEqual((other.Position - marble.Position).magnitude, settings.marbleRadius * 2.5f);
        }
    }

    [Test]
    public void Start_TheSameSeedGivesTheSameLayout()
    {
        var first = NewModel(2, seed: 3);
        var second = NewModel(2, seed: 3);
        var other = NewModel(2, seed: 4);

        CollectionAssert.AreEqual(first.Marbles.Select(m => m.Position), second.Marbles.Select(m => m.Position));
        CollectionAssert.AreNotEqual(first.Marbles.Select(m => m.Position), other.Marbles.Select(m => m.Position));
    }

    // ---- Dice ----

    [Test]
    public void Dice_PressStopsOnTheShownFace()
    {
        var model = NewModel(2);
        double press = Start + 0.73;
        int shown = model.DiceFace(0, press);

        model.Press(0, true, press);

        Assert.IsFalse(model.IsDieSpinning(0));
        Assert.AreEqual(shown, model.DiceFace(0, press + 1.0));
        Assert.IsTrue(model.IsDieSpinning(1));
    }

    [Test]
    public void Dice_StopOnTheirOwnAfterAWhile()
    {
        var settings = new BanBiSettings();
        var model = NewModel(4, settings);
        var stopped = new HashSet<int>();
        model.Event += e => { if (e.Kind == BanBiEventKind.DiceStopped) stopped.Add(e.Player); };

        model.Advance(Start + settings.diceAutoStopSeconds - 0.01);
        CollectionAssert.IsEmpty(stopped);

        model.Advance(Start + settings.diceAutoStopSeconds);
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, stopped);
    }

    [Test]
    public void Dice_ATieRollsAgain()
    {
        var model = NewModel(2);

        RollFaces(model, 4, 4);

        Assert.IsTrue(model.IsDieSpinning(0));
        Assert.IsTrue(model.IsDieSpinning(1));
        Assert.AreEqual(BanBiPhase.DiceRoll, model.Phase);
    }

    [Test]
    public void Dice_InFourPlayers_OnlyThePlayersTiedForTheTopRollAgain()
    {
        var model = NewModel(4);

        RollFaces(model, 6, 6, 2, 3);

        Assert.IsTrue(model.IsDieSpinning(0));
        Assert.IsTrue(model.IsDieSpinning(1));
        Assert.IsFalse(model.IsDieSpinning(2));
        Assert.IsFalse(model.IsDieSpinning(3));
    }

    [Test]
    public void Dice_InFourPlayers_TiedTeammatesRollAgain()
    {
        var model = NewModel(4);

        RollFaces(model, 3, 6, 3, 1);

        Assert.IsTrue(model.IsDieSpinning(0));
        Assert.IsTrue(model.IsDieSpinning(2));
        Assert.IsFalse(model.IsDieSpinning(1));
        Assert.IsFalse(model.IsDieSpinning(3));
    }

    [Test]
    public void Dice_TheHighestRollerGoesFirst()
    {
        var model = NewModel(2);

        RollFaces(model, 2, 6);

        CollectionAssert.AreEqual(new[] { 1, 0 }, model.TurnOrder);
    }

    [Test]
    public void Dice_InFourPlayers_TheTopTeamStarts_HigherTeammateFirst()
    {
        var model = NewModel(4);

        RollFaces(model, 2, 4, 5, 6);

        CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, model.TurnOrder);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void Dice_InFourPlayers_TurnsStrictlyAlternateTeams(int seed)
    {
        var model = NewModel(4, seed: seed);
        RollToFirstTurn(model);

        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, model.TurnOrder);
        for (int i = 1; i < 4; i++)
            Assert.AreNotEqual(SeatLayout.TeamOf(model.TurnOrder[i - 1]), SeatLayout.TeamOf(model.TurnOrder[i]));
        Assert.AreEqual(model.TurnOrder[0], model.ActivePlayer);
    }

    [Test]
    public void PressesDuringTheDiceRoll_NeverShoot()
    {
        var model = NewModel(2);
        int marbles = model.Marbles.Count;
        model.Press(0, true, Start + 0.2);
        model.Press(1, true, Start + 0.3);

        RollToFirstTurn(model);

        Assert.AreEqual(BanBiPhase.Aiming, model.Phase);
        Assert.AreEqual(marbles, model.Marbles.Count);
    }

    // ---- Sliding and charging ----

    [Test]
    public void OtherPlayersCannotPressDuringATurn()
    {
        var model = NewModel(4);
        double time = RollToFirstTurn(model);
        int active = model.ActivePlayer;

        foreach (int other in Enumerable.Range(0, 4).Where(id => id != active))
        {
            model.Press(other, true, time + 0.1);
            model.Press(other, false, time + 0.5);
        }

        Assert.AreEqual(BanBiPhase.Aiming, model.Phase);
        Assert.AreEqual(active, model.ActivePlayer);
    }

    [Test]
    public void TheHandSlidesWithinItsRange_AndStopsOnPress()
    {
        var settings = new BanBiSettings();
        var model = NewModel(2, settings);
        double start = RollToFirstTurn(model);
        float widest = 0f;

        for (double time = start; time < start + 3.0; time += 0.01)
        {
            Assert.LessOrEqual(Mathf.Abs(model.HandX(time)), settings.slideRange + 1e-5f);
            widest = Mathf.Max(widest, Mathf.Abs(model.HandX(time)));
        }
        Assert.Greater(widest, settings.slideRange - 0.01f);

        double press = start + 0.37;
        float locked = model.HandX(press);
        model.Press(model.ActivePlayer, true, press);
        Assert.AreEqual(locked, model.HandX(press + 1.0));
    }

    [Test]
    public void Power_RisesThenFalls()
    {
        var settings = new BanBiSettings();
        var model = NewModel(2, settings);
        double time = RollToFirstTurn(model);
        model.Press(model.ActivePlayer, true, time);

        Assert.AreEqual(0f, model.Power(time), 1e-4f);
        Assert.AreEqual(0.5f, model.Power(time + settings.chargeSeconds / 2), 1e-4f);
        Assert.AreEqual(1f, model.Power(time + settings.chargeSeconds), 1e-4f);
        Assert.AreEqual(0f, model.Power(time + 2 * settings.chargeSeconds), 1e-4f);
    }

    [Test]
    public void HoldingTooLong_ReleasesOnItsOwn()
    {
        var settings = new BanBiSettings();
        var model = NewModel(2, settings);
        double time = RollToFirstTurn(model);
        model.Press(model.ActivePlayer, true, time);

        model.Advance(time + settings.maxHoldSeconds - 0.01);
        Assert.AreEqual(BanBiPhase.Charging, model.Phase);

        model.Advance(time + settings.maxHoldSeconds + 0.01);
        Assert.AreEqual(BanBiPhase.Rolling, model.Phase);
    }

    [Test]
    public void ChargingPausesTheTurnTimer()
    {
        var settings = new BanBiSettings();
        var model = NewModel(2, settings);
        double time = RollToFirstTurn(model);
        model.Press(model.ActivePlayer, true, time + 2.0);

        Assert.AreEqual(settings.turnSeconds - 2.0, model.TurnTimeLeft(time + 3.5), 1e-6);
    }

    [Test]
    public void NotPressingInTime_SkipsTheTurnAndCountsIt()
    {
        var settings = new BanBiSettings();
        var model = NewModel(2, settings);
        double time = RollToFirstTurn(model);
        int first = model.ActivePlayer;

        model.Advance(time + settings.turnSeconds + 0.01);

        Assert.AreEqual(1, model.ShotsTaken(SeatLayout.TeamOf(first)));
        Assert.AreNotEqual(first, model.ActivePlayer);
        Assert.AreEqual(BanBiPhase.Aiming, model.Phase);
    }

    // ---- Launch ----

    [Test]
    public void Releasing_LaunchesANewMarbleStraightUpFromTheHand()
    {
        var settings = new BanBiSettings();
        var model = NewModel(4, settings);
        double time = RollToFirstTurn(model);
        int active = model.ActivePlayer;
        int before = model.Marbles.Count;
        double press = time + 0.4;
        float x = model.HandX(press);

        model.Press(active, true, press);
        model.Press(active, false, press + 0.6);

        Assert.AreEqual(before + 1, model.Marbles.Count);
        var shot = model.Marbles[before];
        Assert.AreEqual(new Vector2(x, settings.launchY), shot.Position);
        Assert.AreEqual(0f, shot.Velocity.x);
        Assert.AreEqual(model.ShotSpeed(0.5f), shot.Velocity.y, 1e-4f);
        Assert.AreEqual(SeatLayout.TeamOf(active), shot.Team);
        Assert.AreEqual(active, shot.ShotBy);
    }

    [Test]
    public void AWeakestShot_StopsInsideNearTheBottomEdge_AndJoinsItsTeam()
    {
        var settings = new BanBiSettings();
        var model = NewModel(2, settings);
        RollToFirstTurn(model);
        int team = model.ActiveTeam;
        ClearTheZone(model);

        Shoot(model, 0.0);

        var shot = model.Marbles.Last();
        Assert.IsTrue(shot.InPlay);
        Assert.That(shot.Position.y, Is.InRange(-1f, -0.6f));
        Assert.AreEqual(settings.startMarblesPerTeam + 1, model.TeamCount(team));
    }

    [Test]
    public void AStrongestShot_WithNothingInTheWay_GoesOutTheTopAsAMiss()
    {
        var settings = new BanBiSettings();
        var model = NewModel(2, settings);
        RollToFirstTurn(model);
        int team = model.ActiveTeam;
        var missed = new List<int>();
        model.Event += e => { if (e.Kind == BanBiEventKind.Missed) missed.Add(e.Player); };
        int shooter = model.ActivePlayer;
        ClearTheZone(model);

        ShootFullPower(model, settings);

        Assert.IsFalse(model.Marbles.Last().InPlay);
        Assert.AreEqual(settings.startMarblesPerTeam, model.TeamCount(team));
        CollectionAssert.AreEqual(new[] { shooter }, missed);
    }

    // ---- Knockouts ----

    [Test]
    public void KnockingARivalMarbleOut_RemovesItAndGivesABonusShot()
    {
        var settings = new BanBiSettings();
        var model = NewModel(2, settings);
        RollToFirstTurn(model);
        int active = model.ActivePlayer;
        int rival = 1 - model.ActiveTeam;
        var target = PlaceInPath(model, rival);

        ShootFullPower(model, settings);

        Assert.IsFalse(target.InPlay);
        Assert.AreEqual(settings.startMarblesPerTeam - 1, model.TeamCount(rival));
        Assert.AreEqual(active, model.ActivePlayer);
        Assert.AreEqual(BanBiPhase.Aiming, model.Phase);
        Assert.AreEqual(0, model.ShotsTaken(model.ActiveTeam), "Bonus shots don't count");
    }

    [Test]
    public void BonusShots_StopAtTheLimit()
    {
        var settings = new BanBiSettings { maxBonusShots = 1 };
        var model = NewModel(2, settings);
        RollToFirstTurn(model);
        int active = model.ActivePlayer;
        int team = model.ActiveTeam;

        for (int shot = 0; shot < 2; shot++)
        {
            Assert.AreEqual(active, model.ActivePlayer);
            PlaceInPath(model, 1 - team);
            ShootFullPower(model, settings);
        }

        Assert.AreEqual(settings.startMarblesPerTeam - 2, model.TeamCount(1 - team));
        Assert.AreNotEqual(active, model.ActivePlayer);
        Assert.AreEqual(1, model.ShotsTaken(team));
    }

    [Test]
    public void KnockingYourOwnMarbleOut_RemovesIt_WithNoBonus()
    {
        var settings = new BanBiSettings();
        var model = NewModel(2, settings);
        RollToFirstTurn(model);
        int active = model.ActivePlayer;
        int team = model.ActiveTeam;
        var target = PlaceInPath(model, team);

        ShootFullPower(model, settings);

        Assert.IsFalse(target.InPlay);
        Assert.AreNotEqual(active, model.ActivePlayer);
        Assert.AreEqual(1, model.ShotsTaken(team));
    }

    // ---- Finish ----

    [Test]
    public void ATeamLeftWithNoMarbles_LosesAtOnce()
    {
        var settings = new BanBiSettings();
        var model = NewModel(2, settings);
        RollToFirstTurn(model);
        int team = model.ActiveTeam;
        var last = PlaceInPath(model, 1 - team);
        foreach (var marble in model.Marbles.Where(m => m.Team != team && m != last)) marble.InPlay = false;

        ShootFullPower(model, settings);

        Assert.AreEqual(BanBiPhase.Finished, model.Phase);
        Assert.AreEqual(team, model.WinnerTeam);
    }

    [Test]
    public void BothTeamsEmptied_IsADraw()
    {
        var settings = new BanBiSettings();
        var model = NewModel(2, settings);
        RollToFirstTurn(model);
        int team = model.ActiveTeam;
        // The rival's last marble sits past the top edge in the shot's path, so both end up out.
        var last = PlaceInPath(model, 1 - team, y: 1.5f);
        foreach (var marble in model.Marbles.Where(m => m != last)) marble.InPlay = false;

        ShootFullPower(model, settings);

        Assert.AreEqual(BanBiPhase.Finished, model.Phase);
        Assert.AreEqual(-1, model.WinnerTeam);
    }

    static BanBiModel PlayOutTheShots(System.Action<BanBiModel> arrange, int players = 2)
    {
        var settings = new BanBiSettings { shotsPerTeam = 1 };
        var model = NewModel(players, settings);
        RollToFirstTurn(model);
        arrange(model);
        SkipTurns(model, settings, 2);
        Assert.AreEqual(BanBiPhase.Finished, model.Phase);
        return model;
    }

    [Test]
    public void AfterTheLastShot_MoreMarblesWins()
    {
        var model = PlayOutTheShots(m => m.Marbles.First(marble => marble.Team == 0).InPlay = false);
        Assert.AreEqual(1, model.WinnerTeam);
    }

    // Each team's marbles go in a row from the center, at these distances.
    static void Line(BanBiModel model, float redClosest, float blueClosest)
    {
        int red = 0, blue = 0;
        foreach (var marble in model.Marbles)
            marble.Position = marble.Team == 0
                ? new Vector2(redClosest + 0.2f * red++, 0f)
                : new Vector2(-(blueClosest + 0.2f * blue++), 0f);
    }

    [Test]
    public void EqualCounts_TheMarbleClosestToTheCenterWins()
    {
        var model = PlayOutTheShots(m => Line(m, 0.3f, 0.1f));
        Assert.AreEqual(1, model.WinnerTeam);
    }

    [Test]
    public void EqualCountsAndDistance_IsADraw()
    {
        var model = PlayOutTheShots(m => Line(m, 0.2f, 0.2f));
        Assert.AreEqual(-1, model.WinnerTeam);
        CollectionAssert.AreEqual(new[] { 1, 1 }, MiniGameBase.RanksFromScores(model.PlayerScores()));
    }

    [Test]
    public void InTwoVersusTwo_TeamsAlternate_AndEachPlayerTakesHalfTheirTeamsShots()
    {
        var settings = new BanBiSettings();
        var model = NewModel(4, settings);
        var turns = new List<int>();
        model.Event += e => { if (e.Kind == BanBiEventKind.TurnStarted) turns.Add(e.Player); };
        RollToFirstTurn(model);

        SkipTurns(model, settings, settings.shotsPerTeam * 2);

        Assert.AreEqual(BanBiPhase.Finished, model.Phase);
        Assert.AreEqual(settings.shotsPerTeam * 2, turns.Count);
        for (int i = 1; i < turns.Count; i++)
            Assert.AreNotEqual(SeatLayout.TeamOf(turns[i - 1]), SeatLayout.TeamOf(turns[i]));
        foreach (int id in Enumerable.Range(0, 4))
            Assert.AreEqual(settings.shotsPerTeam / 2, turns.Count(player => player == id));
    }

    [Test]
    public void Ranking_TeammatesShareTheirRank()
    {
        var model = PlayOutTheShots(m => m.Marbles.First(marble => marble.Team == 0).InPlay = false, players: 4);

        // Competition ranking, as in every mini game: two share 1st, so the next rank is 3rd.
        CollectionAssert.AreEqual(new[] { 3, 1, 3, 1 }, MiniGameBase.RanksFromScores(model.PlayerScores()));
    }

    // ---- Physics ----

    [Test]
    public void HeadOnCollision_WithFullRestitution_SwapsVelocities()
    {
        var settings = new BanBiSettings { friction = 0f, restitution = 1f };
        var a = new MarbleState(0, new Vector2(-0.2f, 0f)) { Velocity = new Vector2(1f, 0f) };
        var b = new MarbleState(1, new Vector2(0.2f, 0f));
        var marbles = new[] { a, b };

        for (int i = 0; i < 240; i++) BanBiPhysics.Step(marbles, settings, settings.physicsStep);

        Assert.AreEqual(0f, a.Velocity.magnitude, 1e-4f);
        Assert.AreEqual(1f, b.Velocity.x, 1e-4f);
    }

    [Test]
    public void Collisions_KeepMomentum()
    {
        var settings = new BanBiSettings { friction = 0f };
        var a = new MarbleState(0, new Vector2(-0.3f, 0.03f)) { Velocity = new Vector2(2f, 0f) };
        var b = new MarbleState(1, new Vector2(0f, 0f)) { Velocity = new Vector2(-0.5f, 0.4f) };
        var marbles = new[] { a, b };
        var before = a.Velocity + b.Velocity;
        int hits = 0;

        for (int i = 0; i < 120; i++) BanBiPhysics.Step(marbles, settings, settings.physicsStep, (x, y, impulse) => hits++);

        Assert.Greater(hits, 0);
        Assert.AreEqual(before.x, (a.Velocity + b.Velocity).x, 1e-4f);
        Assert.AreEqual(before.y, (a.Velocity + b.Velocity).y, 1e-4f);
    }

    [Test]
    public void AMarble_StopsAfterTheFrictionDistance()
    {
        var settings = new BanBiSettings();
        const float speed = 1.5f;
        var marble = new MarbleState(0, Vector2.zero) { Velocity = new Vector2(speed, 0f) };

        while (marble.Velocity != Vector2.zero) BanBiPhysics.Step(new[] { marble }, settings, settings.physicsStep);

        Assert.AreEqual(speed * speed / (2f * settings.friction), marble.Position.x, speed * settings.physicsStep);
    }

    [Test]
    public void TheSameShots_EndTheSameAtAnyFrameRate()
    {
        var settings = new BanBiSettings();
        var slow = Play(settings, 1.0 / 30.0);
        var fast = Play(settings, 1.0 / 144.0);

        Assert.AreEqual(slow.Count, fast.Count);
        for (int i = 0; i < slow.Count; i++)
        {
            Assert.AreEqual(slow[i].x, fast[i].x, 1e-4f);
            Assert.AreEqual(slow[i].y, fast[i].y, 1e-4f);
        }
    }

    // Every turn presses 0.37 s into aiming and releases 0.83 s later.
    static List<Vector2> Play(BanBiSettings settings, double frame)
    {
        var model = NewModel(4, settings);
        double time = Start;
        double pressedFor = double.NaN;
        while (time < Start + 40.0 && model.Phase != BanBiPhase.Finished)
        {
            time += frame;
            if (model.Phase == BanBiPhase.Aiming && model.PhaseStart != pressedFor)
            {
                pressedFor = model.PhaseStart;
                double press = model.PhaseStart + 0.37;
                model.Advance(press);
                model.Press(model.ActivePlayer, true, press);
                model.Advance(press + 0.83);
                model.Press(model.ActivePlayer, false, press + 0.83);
            }
            model.Advance(time);
        }
        return model.Marbles.Select(marble => marble.Position).ToList();
    }
}
