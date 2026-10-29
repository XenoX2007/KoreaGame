using System;
using UnityEngine;

// Where each player sits around a landscape phone lying flat on a table.
// All rects/points are normalized screen coordinates, (0,0) = bottom-left.
// Player ids are chosen so that team = id % 2: even ids sit on the left half, odd ids on the right.
public readonly struct Seat
{
    public readonly int PlayerId;
    public readonly Rect Zone;       // touches starting here belong to this player (covers the full screen)
    public readonly Vector2 Anchor;  // where this player's button/HUD goes, relative to the safe area
    public readonly float Rotation;  // UI z-rotation so text faces the player

    public Seat(int playerId, Rect zone, Vector2 anchor, float rotation)
    {
        PlayerId = playerId;
        Zone = zone;
        Anchor = anchor;
        Rotation = rotation;
    }

    public int Team => PlayerId % 2;
}

public static class SeatLayout
{
    public const int MaxPlayers = 4;

    public static readonly Color[] PlayerColors =
    {
        new Color32(0xE9, 0x4F, 0x4F, 0xFF), // coral
        new Color32(0x3D, 0x8B, 0xFF, 0xFF), // blue
        new Color32(0x3C, 0xC5, 0x7A, 0xFF), // green
        new Color32(0xFF, 0xC2, 0x3D, 0xFF), // yellow
    };

    // 2 players sit at the short ends, facing each other.
    static readonly Seat[] TwoPlayers =
    {
        new Seat(0, new Rect(0f, 0f, 0.5f, 1f), new Vector2(0.08f, 0.5f), -90f),
        new Seat(1, new Rect(0.5f, 0f, 0.5f, 1f), new Vector2(0.92f, 0.5f), 90f),
    };

    // 4 players sit two per long side, one button per corner.
    static readonly Seat[] FourPlayers =
    {
        new Seat(0, new Rect(0f, 0f, 0.5f, 0.5f), new Vector2(0.12f, 0.15f), 0f),     // bottom-left
        new Seat(1, new Rect(0.5f, 0.5f, 0.5f, 0.5f), new Vector2(0.88f, 0.85f), 180f), // top-right
        new Seat(2, new Rect(0f, 0.5f, 0.5f, 0.5f), new Vector2(0.12f, 0.85f), 180f),   // top-left
        new Seat(3, new Rect(0.5f, 0f, 0.5f, 0.5f), new Vector2(0.88f, 0.15f), 0f),     // bottom-right
    };

    public static Seat[] Get(int playerCount)
    {
        switch (playerCount)
        {
            case 2: return TwoPlayers;
            case 4: return FourPlayers;
            default: throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "Only 2 or 4 players are supported.");
        }
    }
}
