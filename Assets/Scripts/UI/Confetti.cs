using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Confetti drawn as UI Toolkit elements, because a particle system in the world would sit behind
// the panel. Pieces come from a fixed pool, so the rain never grows; a burst takes whatever is free.
public class Confetti
{
    const int PoolSize = 90;
    const float Gravity = 620f;
    const float Drag = 1.1f;       // per second, slows the burst's sideways speed
    const float Flutter = 46f;     // panel units per second of side-to-side drift
    const float BurstSeconds = 2.4f;
    const float RainPerSecond = 16f;

    static readonly Color Gold = new Color32(0xF2, 0xB2, 0x33, 0xFF);

    class Piece
    {
        public VisualElement Element;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Angle;
        public float Spin;
        public float Age;
        public float FadeAfter;  // seconds until it fades out; infinity for rain, which falls off screen
        public bool Active;
    }

    readonly VisualElement _layer;
    readonly List<Piece> _pieces = new List<Piece>();
    float _rainDue;

    public Confetti(VisualElement layer)
    {
        _layer = layer;
        for (int i = 0; i < PoolSize; i++)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList("confetti");
            element.EnableInClassList("confetti--round", i % 3 == 0);
            element.style.backgroundColor = i % 5 == 4 ? Gold : SeatLayout.PlayerColors[i % SeatLayout.PlayerColors.Length];
            element.style.display = DisplayStyle.None;
            layer.Add(element);
            _pieces.Add(new Piece { Element = element });
        }
    }

    public int ActiveCount { get; private set; }

    public void Burst(Vector2 origin, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            var velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(180f, 720f) + Vector2.down * 260f;
            if (!Launch(origin, velocity, BurstSeconds)) return;
        }
    }

    public void Rain(float deltaTime)
    {
        _rainDue += deltaTime * RainPerSecond;
        while (_rainDue >= 1f)
        {
            _rainDue -= 1f;
            var origin = new Vector2(Random.Range(0f, _layer.layout.width), -30f);
            Launch(origin, new Vector2(0f, Random.Range(90f, 220f)), float.PositiveInfinity);
        }
    }

    public void Update(float deltaTime)
    {
        ActiveCount = 0;
        float bottom = _layer.layout.height + 40f;

        foreach (var piece in _pieces)
        {
            if (!piece.Active) continue;

            piece.Age += deltaTime;
            piece.Velocity.x *= Mathf.Exp(-Drag * deltaTime);
            piece.Velocity.y += Gravity * deltaTime;
            piece.Velocity.y = Mathf.Min(piece.Velocity.y, 340f); // paper reaches a terminal speed
            piece.Position += piece.Velocity * deltaTime;
            piece.Angle += piece.Spin * deltaTime;

            float fade = Mathf.Clamp01(1f - (piece.Age - piece.FadeAfter) / 0.5f);
            if (piece.Position.y > bottom || fade <= 0f)
            {
                piece.Active = false;
                piece.Element.style.display = DisplayStyle.None;
                continue;
            }

            float drift = Mathf.Sin(piece.Age * 5f + piece.Spin) * Flutter * deltaTime;
            piece.Position.x += drift;

            piece.Element.style.translate = new Translate(piece.Position.x, piece.Position.y);
            piece.Element.style.rotate = new Rotate(new Angle(piece.Angle));
            piece.Element.style.opacity = fade;
            ActiveCount++;
        }
    }

    bool Launch(Vector2 origin, Vector2 velocity, float fadeAfter)
    {
        var piece = _pieces.Find(candidate => !candidate.Active);
        if (piece == null) return false;

        piece.Active = true;
        piece.Position = origin;
        piece.Velocity = velocity;
        piece.Angle = Random.Range(0f, 360f);
        piece.Spin = Random.Range(-420f, 420f);
        piece.Age = 0f;
        piece.FadeAfter = fadeAfter;
        piece.Element.style.opacity = 1f;
        piece.Element.style.display = DisplayStyle.Flex;
        return true;
    }
}
