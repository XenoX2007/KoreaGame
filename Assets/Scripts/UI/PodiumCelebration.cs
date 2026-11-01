using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// The end of a tournament: once the columns have landed, the winner lifts the cup over their head,
// everyone sways left and right, and confetti bursts from the cup and keeps raining down.
public class PodiumCelebration
{
    public const float StartSeconds = 1.3f;  // the last column finishes landing here
    public const float RaiseSeconds = 0.8f;

    const float SwayDegrees = 7f;
    const float SwaySpeed = 4.5f;            // radians per second
    const float ChampionHop = 16f;
    const float RaySpin = 12f;               // degrees per second
    const float RayRadius = 1300f;  // past the screen edges, so the rays never show their tips
    const int RayCount = 14;
    const int BurstSize = 46;

    readonly VisualElement _rayLayer;
    readonly List<CharacterView> _everyone;
    readonly List<CharacterView> _champions;
    readonly Confetti _confetti;
    readonly List<VisualElement> _rays = new List<VisualElement>();
    float _elapsed;
    bool _burst;

    public PodiumCelebration(VisualElement rayLayer, VisualElement confettiLayer,
        List<CharacterView> everyone, List<CharacterView> champions)
    {
        _rayLayer = rayLayer;
        _everyone = everyone;
        _champions = champions;
        _confetti = new Confetti(confettiLayer);
    }

    // How far the cup is lifted, 0 at the chest to 1 over the head.
    public static float RaiseAt(float elapsed) => Mathf.SmoothStep(0f, 1f, (elapsed - StartSeconds) / RaiseSeconds);

    // Each place sways a little behind the one before, so the row looks like a wave.
    public static float SwayAt(float elapsed, int place)
    {
        float time = elapsed - StartSeconds;
        if (time <= 0f) return 0f;
        return Mathf.Sin(time * SwaySpeed + place * 0.9f) * SwayDegrees * Mathf.Clamp01(time * 2f);
    }

    public void Update(float deltaTime)
    {
        _elapsed += deltaTime;
        float raised = RaiseAt(_elapsed);

        for (int i = 0; i < _everyone.Count; i++) _everyone[i].Tilt(SwayAt(_elapsed, i));

        foreach (var champion in _champions)
        {
            champion.Raise(raised);
            float jump = _elapsed - StartSeconds - RaiseSeconds;
            champion.Lift(jump > 0f ? Mathf.Abs(Mathf.Sin(jump * SwaySpeed)) * ChampionHop : 0f);
        }

        if (!_burst && raised >= 1f)
        {
            _burst = true;
            GameAudio.PlayVictory();
            foreach (var champion in _champions)
            {
                var cup = _rayLayer.WorldToLocal(champion.Cup.worldBound.center);
                _confetti.Burst(cup, BurstSize / _champions.Count);
                _rays.Add(AddRays(cup));
            }
        }

        if (_burst) _confetti.Rain(deltaTime);
        _confetti.Update(deltaTime);

        float glow = Mathf.Clamp01((_elapsed - StartSeconds - RaiseSeconds) * 2f);
        foreach (var rays in _rays)
        {
            rays.style.rotate = new Rotate(new Angle(_elapsed * RaySpin));
            rays.style.opacity = glow;
        }
    }

    VisualElement AddRays(Vector2 center)
    {
        var rays = new VisualElement { pickingMode = PickingMode.Ignore };
        rays.AddToClassList("podium-rays");
        rays.style.translate = new Translate(center.x - RayRadius, center.y - RayRadius);
        rays.style.opacity = 0f;
        rays.generateVisualContent += DrawRays;
        _rayLayer.Add(rays);
        return rays;
    }

    static void DrawRays(MeshGenerationContext context)
    {
        var painter = context.painter2D;
        var center = new Vector2(RayRadius, RayRadius);
        painter.fillColor = new Color(1f, 0.9f, 0.55f, 0.18f);

        float wedge = Mathf.PI / RayCount;
        for (int i = 0; i < RayCount; i++)
        {
            float angle = i * 2f * wedge;
            painter.BeginPath();
            painter.MoveTo(center);
            painter.LineTo(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * RayRadius);
            painter.LineTo(center + new Vector2(Mathf.Cos(angle + wedge), Mathf.Sin(angle + wedge)) * RayRadius);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
