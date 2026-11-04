using System;
using System.Collections.Generic;
using UnityEngine;

public enum TachKeoShape
{
    Umbrella,
    Square,
    Oval,
    Star,
}

// The stamped outlines, in candy units (candy radius 1, +y away from the player). Every shape is
// scaled to the same perimeter, so no shape is quicker to cut than another.
public static class TachKeoShapes
{
    public const float DefaultPerimeter = 3.4f;
    public const float MaxRadius = 0.85f;

    public static readonly TachKeoShape[] All = { TachKeoShape.Umbrella, TachKeoShape.Square, TachKeoShape.Oval, TachKeoShape.Star };

    // A closed polyline (the last point joins the first), evenly spaced by arc length,
    // starting at the shape's topmost point and going clockwise.
    public static Vector2[] Outline(TachKeoShape shape, int samples, float perimeter = DefaultPerimeter)
    {
        var points = Resample(Path(shape), samples);
        // Scaled after resampling, which cuts sharp corners a little short.
        float scale = perimeter / Length(points);
        for (int i = 0; i < points.Length; i++) points[i] *= scale;
        return points;
    }

    // Each path starts at its topmost point and runs clockwise, before scaling.
    static List<Vector2> Path(TachKeoShape shape)
    {
        switch (shape)
        {
            case TachKeoShape.Square: return Square();
            case TachKeoShape.Oval: return Oval();
            case TachKeoShape.Star: return Star();
            case TachKeoShape.Umbrella: return Umbrella();
            default: throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    // Side 1, starting at the middle of the top edge.
    static List<Vector2> Square() => new List<Vector2>
    {
        new Vector2(0f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, -0.5f),
        new Vector2(-0.5f, -0.5f),
        new Vector2(-0.5f, 0.5f),
    };

    // Width 1.3, height 0.9.
    static List<Vector2> Oval()
    {
        var points = new List<Vector2>();
        const int count = 256;
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.PI / 2f - i * 2f * Mathf.PI / count;
            points.Add(new Vector2(0.65f * Mathf.Cos(angle), 0.45f * Mathf.Sin(angle)));
        }
        return points;
    }

    // Five points, outer radius 1, inner 0.42, a point at the top.
    static List<Vector2> Star()
    {
        var points = new List<Vector2>();
        for (int i = 0; i < 10; i++)
        {
            float angle = Mathf.PI / 2f - i * Mathf.PI / 5f;
            float radius = i % 2 == 0 ? 1f : 0.42f;
            points.Add(new Vector2(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle)));
        }
        return points;
    }

    const float DomeHalfWidth = 0.65f;
    const float DomeHeight = 0.65f;
    const float ScallopDepth = 0.08f;
    const float ShaftHalfWidth = 0.06f;
    const float ShaftBottom = -0.55f;
    const float HookCenterX = -0.12f;
    const float HookTipTop = -0.47f;

    // A dome with three shallow scallops along its bottom edge, a shaft down from the middle and
    // a J-hook curling left, traced as one outline around the whole silhouette (shaft and hook 0.12 wide).
    static List<Vector2> Umbrella()
    {
        var points = new List<Vector2>();
        const int arc = 48;

        // The right half of the dome, from the top down to its right corner.
        for (int i = 0; i < arc; i++)
        {
            float angle = Mathf.PI / 2f - i * (Mathf.PI / 2f) / arc;
            points.Add(new Vector2(DomeHalfWidth * Mathf.Cos(angle), DomeHeight * Mathf.Sin(angle)));
        }

        // The scallops right to left, as far as the shaft.
        AddScallops(points, DomeHalfWidth, ShaftHalfWidth);

        // Down the shaft's right edge and around the outside of the hook.
        float outer = ShaftHalfWidth - HookCenterX;
        float inner = -ShaftHalfWidth - HookCenterX;
        for (int i = 0; i <= arc; i++)
        {
            float angle = -i * Mathf.PI / arc;
            points.Add(new Vector2(HookCenterX + outer * Mathf.Cos(angle), ShaftBottom + outer * Mathf.Sin(angle)));
        }

        // Up to the hook's tip, across it and back down its inside.
        points.Add(new Vector2(HookCenterX - outer, HookTipTop));
        points.Add(new Vector2(HookCenterX - inner, HookTipTop));
        for (int i = 0; i <= arc; i++)
        {
            float angle = Mathf.PI + i * Mathf.PI / arc;
            points.Add(new Vector2(HookCenterX + inner * Mathf.Cos(angle), ShaftBottom + inner * Mathf.Sin(angle)));
        }

        // Up the shaft's left edge, the rest of the scallops, then the left half of the dome.
        AddScallops(points, -ShaftHalfWidth, -DomeHalfWidth);
        for (int i = 0; i < arc; i++)
        {
            float angle = Mathf.PI - i * (Mathf.PI / 2f) / arc;
            points.Add(new Vector2(DomeHalfWidth * Mathf.Cos(angle), DomeHeight * Mathf.Sin(angle)));
        }

        // Center the silhouette on the candy.
        float bottom = ShaftBottom - outer;
        var middle = new Vector2(0f, (DomeHeight + bottom) / 2f);
        for (int i = 0; i < points.Count; i++) points[i] -= middle;
        return points;
    }

    // Three arcs bowing up into the dome across its full width, walked from x = from to x = to.
    static void AddScallops(List<Vector2> points, float from, float to)
    {
        const int count = 60;
        for (int i = 0; i <= count; i++)
        {
            float x = Mathf.Lerp(from, to, i / (float)count);
            float width = 2f * DomeHalfWidth / 3f;
            float along = (x + DomeHalfWidth) % width / width;
            points.Add(new Vector2(x, ScallopDepth * Mathf.Sin(along * Mathf.PI)));
        }
    }

    static float Length(IReadOnlyList<Vector2> path)
    {
        float length = 0f;
        for (int i = 0; i < path.Count; i++) length += Vector2.Distance(path[i], path[(i + 1) % path.Count]);
        return length;
    }

    static Vector2[] Resample(List<Vector2> path, int samples)
    {
        float step = Length(path) / samples;
        var result = new Vector2[samples];
        int edge = 0;
        float edgeStart = 0f;
        for (int i = 0; i < samples; i++)
        {
            float at = i * step;
            float edgeLength = Vector2.Distance(path[edge], path[(edge + 1) % path.Count]);
            while (edgeStart + edgeLength < at && edge < path.Count - 1)
            {
                edgeStart += edgeLength;
                edge++;
                edgeLength = Vector2.Distance(path[edge], path[(edge + 1) % path.Count]);
            }
            float t = edgeLength > 0f ? (at - edgeStart) / edgeLength : 0f;
            result[i] = Vector2.Lerp(path[edge], path[(edge + 1) % path.Count], t);
        }
        return result;
    }
}
