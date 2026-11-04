using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class TachKeoModelTests
{
    const double Start = 5.0;
    const double Frame = 1.0 / 120.0;

    static TachKeoModel NewModel(int players, TachKeoSettings settings = null, int seed = 1)
    {
        var model = new TachKeoModel(settings ?? new TachKeoSettings(), players, seed);
        model.Start(Start);
        return model;
    }

    // Steers a needle along its outline, always toward the next outline point ahead of it.
    sealed class Tracer
    {
        int _next = 1;

        public Vector2 Stick(TachKeoModel model, int player, float tilt)
        {
            var outline = model.OutlineOf(player);
            var needle = model.NeedleOf(player);
            for (int guard = 0; guard < outline.Count && Vector2.Distance(outline[_next % outline.Count], needle) < 0.01f; guard++) _next++;
            return (outline[_next % outline.Count] - needle).normalized * tilt;
        }
    }

    // Plays frames from `from` to `to` seconds after Start, asking the script for each player's stick
    // (player, seconds since Start).
    static void Run(TachKeoModel model, double from, double to, Func<int, double, Vector2> stick, double frame = Frame)
    {
        long first = (long)Math.Round(from / frame);
        long last = (long)Math.Round(to / frame);
        for (long f = first; f < last; f++)
        {
            double time = Start + f * frame;
            for (int p = 0; p < model.PlayerCount; p++) model.SetStick(p, stick(p, time - Start), time);
        }
        model.Advance(Start + last * frame);
    }

    static Func<int, double, Vector2> TraceAt(TachKeoModel model, params float[] tilts)
    {
        var tracers = tilts.Select(_ => new Tracer()).ToArray();
        return (p, t) => tilts[p] > 0f && model.StateOf(p) == CutState.Cutting ? tracers[p].Stick(model, p, tilts[p]) : Vector2.zero;
    }

    static float Perimeter(IReadOnlyList<Vector2> outline)
    {
        float length = 0f;
        for (int i = 0; i < outline.Count; i++) length += Vector2.Distance(outline[i], outline[(i + 1) % outline.Count]);
        return length;
    }

    [Test]
    public void Shapes_HaveTheSamePerimeter_StayInside_AndStartAtTheTop([Values] TachKeoShape shape)
    {
        var outline = TachKeoShapes.Outline(shape, 160);

        Assert.AreEqual(160, outline.Length);
        Assert.AreEqual(TachKeoShapes.DefaultPerimeter, Perimeter(outline), TachKeoShapes.DefaultPerimeter * 0.01f);
        Assert.LessOrEqual(outline.Max(point => point.magnitude), TachKeoShapes.MaxRadius);
        Assert.GreaterOrEqual(outline[0].y, outline.Max(point => point.y) - 1e-5f, "Starts at the topmost point");
        for (int i = 0; i < outline.Length; i++)
            Assert.Greater(Vector2.Distance(outline[i], outline[(i + 1) % outline.Length]), 1e-4f, $"Point {i} repeats");
    }

    [Test]
    public void Shapes_GoClockwise([Values] TachKeoShape shape)
    {
        var outline = TachKeoShapes.Outline(shape, 160);
        float area = 0f;
        for (int i = 0; i < outline.Length; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Length];
            area += a.x * b.y - b.x * a.y;
        }
        Assert.Less(area, 0f, "A clockwise loop has a negative signed area");
    }

    [Test]
    public void FourPlayers_GetEveryShapeOnce()
    {
        var model = NewModel(4);
        var shapes = Enumerable.Range(0, 4).Select(model.ShapeOf).ToArray();
        CollectionAssert.AreEquivalent(TachKeoShapes.All, shapes);
    }

    [Test]
    public void TwoPlayers_GetDifferentShapes_AndTheSeedDecidesWhich()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var model = NewModel(2, seed: seed);
            Assert.AreNotEqual(model.ShapeOf(0), model.ShapeOf(1));

            var again = NewModel(2, seed: seed);
            Assert.AreEqual(model.ShapeOf(0), again.ShapeOf(0));
            Assert.AreEqual(model.ShapeOf(1), again.ShapeOf(1));
        }

        var firstShapes = Enumerable.Range(0, 20).Select(seed => NewModel(2, seed: seed).ShapeOf(0)).Distinct().Count();
        Assert.Greater(firstShapes, 1, "Different rounds hand out different shapes");
    }

    [Test]
    public void BeforeStart_NothingMoves()
    {
        var model = new TachKeoModel(new TachKeoSettings(), 2, 1);
        model.SetStick(0, Vector2.right, 1.0);
        model.Advance(3.0);

        Assert.AreEqual(CutState.Waiting, model.StateOf(0));
        Assert.AreEqual(model.OutlineOf(0)[0], model.NeedleOf(0));
    }

    [Test]
    public void ACarefulTrace_FinishesEveryShapeWithoutBreaking_InTheExpectedTime()
    {
        var settings = new TachKeoSettings();
        var model = NewModel(4, settings);
        float tilt = settings.pushThreshold;
        Run(model, 0, 10, TraceAt(model, tilt, tilt, tilt, tilt));

        double expected = settings.outlinePerimeter / (settings.needleSpeed * tilt);
        for (int p = 0; p < 4; p++)
        {
            Assert.AreEqual(CutState.Done, model.StateOf(p), model.ShapeOf(p).ToString());
            Assert.AreEqual(0, model.BreaksOf(p), model.ShapeOf(p).ToString());
            Assert.AreEqual(expected, model.FinishTimeOf(p), expected * 0.1, model.ShapeOf(p).ToString());
            Assert.AreEqual(1f, model.ProgressOf(p));
        }
        Assert.IsTrue(model.IsOver, "The round ends early once everyone is done");
        Assert.Less(model.SimulatedTime - Start, 10.0 - 1.0);
    }

    [Test]
    public void HoldingTheStickOffTheLine_BreaksInUnderASecond_ThenAFreshCandyArrives()
    {
        var settings = new TachKeoSettings();
        var model = NewModel(2, settings);
        double brokeAt = double.NaN;
        var events = new List<TachKeoEvent>();
        model.Event += e =>
        {
            events.Add(e);
            if (e.Kind == TachKeoEventKind.Broke && e.Player == 0) brokeAt = model.SimulatedTime - Start;
        };

        // Straight up, away from the topmost point where the needle starts.
        Run(model, 0, 1, (p, t) => p == 0 && double.IsNaN(brokeAt) ? new Vector2(0f, settings.pushThreshold) : Vector2.zero);

        Assert.Less(brokeAt, 1.0);
        Assert.AreEqual(1, model.BreaksOf(0));
        Assert.That(events.Where(e => e.Kind == TachKeoEventKind.Cracking).Select(e => e.Level), Is.EqualTo(new[] { 0.5f, 0.8f }));
        Assert.AreEqual(CutState.Broken, model.StateOf(0));
        Assert.AreEqual(0f, model.ProgressOf(0));

        Run(model, 1, brokeAt + settings.restartDelay + 0.05, (p, t) => Vector2.zero);

        Assert.AreEqual(CutState.Cutting, model.StateOf(0));
        Assert.AreEqual(0f, model.ProgressOf(0));
        Assert.AreEqual(0f, model.CrackOf(0));
        Assert.AreEqual(model.OutlineOf(0)[0], model.NeedleOf(0));
        Assert.IsTrue(events.Any(e => e.Kind == TachKeoEventKind.Restarted && e.Player == 0));
        Assert.AreEqual(Start, model.StartTime, "The round clock keeps running");
        Assert.AreEqual(settings.roundSeconds - (brokeAt + settings.restartDelay + 0.05), model.TimeLeft(model.SimulatedTime), 0.02);
    }

    [Test]
    public void FullTiltAlongTheLine_CracksSlowly()
    {
        var model = NewModel(4);
        double[] brokeAt = { double.NaN, double.NaN, double.NaN, double.NaN };
        model.Event += e =>
        {
            if (e.Kind == TachKeoEventKind.Broke && double.IsNaN(brokeAt[e.Player])) brokeAt[e.Player] = model.SimulatedTime - Start;
        };

        Run(model, 0, 3.5, TraceAt(model, 1f, 1f, 1f, 1f));

        for (int p = 0; p < 4; p++)
            Assert.That(brokeAt[p], Is.InRange(1.5, 3.0), model.ShapeOf(p).ToString());
    }

    [Test]
    public void AGentleTiltAlongTheLine_NeverBreaks()
    {
        var model = NewModel(4);
        Run(model, 0, 12, TraceAt(model, 0.6f, 0.6f, 0.6f, 0.6f));

        for (int p = 0; p < 4; p++)
        {
            Assert.AreEqual(0, model.BreaksOf(p), model.ShapeOf(p).ToString());
            Assert.AreEqual(CutState.Done, model.StateOf(p), model.ShapeOf(p).ToString());
        }
    }

    [Test]
    public void CarefulCutting_HealsTheCrack_ButNeverBelowZero()
    {
        var settings = new TachKeoSettings();
        var model = NewModel(2, settings);
        var tracer = new Tracer();
        Func<float, Func<int, double, Vector2>> at = tilt => (p, t) => p == 0 ? tracer.Stick(model, 0, tilt) : Vector2.zero;

        Run(model, 0, 1, at(1f));
        float cracked = model.CrackOf(0);
        Assert.AreEqual(settings.pushRate, cracked, 0.05f, "Full tilt for a second");

        Run(model, 1, 3, at(0.5f));
        Assert.AreEqual(cracked - 2f * settings.healRate, model.CrackOf(0), 0.02f);

        Run(model, 3, 6, at(0.1f));
        Assert.AreEqual(0f, model.CrackOf(0));
        Assert.AreEqual(CutState.Cutting, model.StateOf(0));
    }

    [Test]
    public void ALetGoStick_DoesntCrack_EvenOffTheLine()
    {
        var model = NewModel(2);
        Run(model, 0, 0.2, (p, t) => p == 0 ? new Vector2(0f, 0.7f) : Vector2.zero);
        float crack = model.CrackOf(0);
        var needle = model.NeedleOf(0);
        Assert.Greater(crack, 0f);

        Run(model, 0.2, 2.2, (p, t) => Vector2.zero);

        Assert.AreEqual(crack, model.CrackOf(0));
        Assert.AreEqual(needle, model.NeedleOf(0));
        Assert.AreEqual(CutState.Cutting, model.StateOf(0));
    }

    [Test]
    public void NobodySteering_CarvesNothing()
    {
        var model = NewModel(2);
        Run(model, 0, 2, (p, t) => Vector2.zero);

        Assert.AreEqual(0f, model.ProgressOf(0));
        Assert.AreEqual(0f, model.ProgressOf(1));
    }

    [Test]
    public void ASingleGapBetweenCarvedSegments_IsCarvedToo()
    {
        var carved = new[] { false, true, false, true, false, false };
        TachKeoModel.FillSingleGaps(carved);
        CollectionAssert.AreEqual(new[] { false, true, true, true, false, false }, carved);

        var aroundTheLoop = new[] { false, true, false, false, true };
        TachKeoModel.FillSingleGaps(aroundTheLoop);
        CollectionAssert.AreEqual(new[] { true, true, false, false, true }, aroundTheLoop);
    }

    // Changes only on the half second, which both frame rates land on.
    static Vector2 Scripted(int player, double seconds)
    {
        Vector2[] moves = { new Vector2(0.6f, 0f), new Vector2(0.3f, -0.9f), new Vector2(-0.8f, 0f), new Vector2(0f, 0.65f) };
        int part = (int)Math.Floor(seconds * 2.0 + 1e-6);
        return part < moves.Length ? moves[(part + player) % moves.Length] : Vector2.zero;
    }

    [Test]
    public void TheSameInput_GivesTheSameResult_At30And144Fps()
    {
        var slow = NewModel(4);
        var fast = NewModel(4);
        Run(slow, 0, 2.5, Scripted, 1.0 / 30.0);
        Run(fast, 0, 2.5, Scripted, 1.0 / 144.0);

        for (int p = 0; p < 4; p++)
        {
            Assert.AreEqual(slow.ProgressOf(p), fast.ProgressOf(p), 1e-4f);
            Assert.AreEqual(slow.CrackOf(p), fast.CrackOf(p), 1e-4f);
            Assert.AreEqual(slow.StateOf(p), fast.StateOf(p));
            Assert.AreEqual(0f, Vector2.Distance(slow.NeedleOf(p), fast.NeedleOf(p)), 1e-4f);
        }
        Assert.IsTrue(Enumerable.Range(0, 4).Any(p => slow.ProgressOf(p) > 0f && slow.CrackOf(p) > 0f), "The script carves and cracks");
    }

    [Test]
    public void Ranking_FinishersByTime_ThenEveryoneElseByProgress_AndTheRoundEndsOnTime()
    {
        var settings = new TachKeoSettings { roundSeconds = 8f };
        var model = NewModel(4, settings);
        int timeUps = 0;
        model.Event += e => timeUps += e.Kind == TachKeoEventKind.TimeUp ? 1 : 0;
        var script = TraceAt(model, 0.7f, 0.55f, 0.2f, 0.1f);

        Run(model, 0, 7.9, script);
        Assert.IsFalse(model.IsOver);
        Run(model, 7.9, 9, script);

        Assert.IsTrue(model.IsOver);
        Assert.AreEqual(1, timeUps);
        Assert.AreEqual(CutState.Done, model.StateOf(0));
        Assert.AreEqual(CutState.Done, model.StateOf(1));
        Assert.Less(model.FinishTimeOf(0), model.FinishTimeOf(1));
        Assert.Greater(model.ProgressOf(2), model.ProgressOf(3));
        Assert.Less(model.ProgressOf(2), 1f);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, model.Ranking());
        Assert.IsTrue(model.HasWinner);
        Assert.AreEqual(Start + settings.roundSeconds, model.SimulatedTime, settings.step);
    }

    [Test]
    public void Ranking_ACandyBrokenAtTimeUp_CountsAsNothing()
    {
        var settings = new TachKeoSettings { roundSeconds = 3f };
        var model = NewModel(2, settings);
        var tracer = new Tracer();
        var slow = new Tracer();

        Run(model, 0, 3, (p, t) =>
        {
            if (p == 1) return slow.Stick(model, 1, 0.1f);
            if (model.StateOf(0) != CutState.Cutting) return Vector2.zero;
            // Carve a long way, then dig toward the middle of the candy.
            return t < 2.5 ? tracer.Stick(model, 0, 0.6f) : -model.NeedleOf(0).normalized;
        });

        Assert.IsTrue(model.IsOver);
        Assert.AreEqual(CutState.Broken, model.StateOf(0));
        Assert.Greater(model.ProgressOf(1), 0f);
        CollectionAssert.AreEqual(new[] { 2, 1 }, model.Ranking());
    }

    [Test]
    public void Ranking_EqualProgressSharesARank()
    {
        var settings = new TachKeoSettings { roundSeconds = 3f };
        var model = NewModel(4, settings);
        Run(model, 0, 3, TraceAt(model, 0.5f, 0f, 0f, 0.3f));

        CollectionAssert.AreEqual(new[] { 1, 3, 3, 2 }, model.Ranking());
        Assert.IsTrue(model.HasWinner);
    }

    [Test]
    public void Ranking_NobodyCarvedAnything_HasNoWinner()
    {
        var settings = new TachKeoSettings { roundSeconds = 2f };
        var model = NewModel(2, settings);
        Run(model, 0, 2.5, (p, t) => Vector2.zero);

        Assert.IsTrue(model.IsOver);
        CollectionAssert.AreEqual(new[] { 1, 1 }, model.Ranking());
        Assert.IsFalse(model.HasWinner);
    }
}
