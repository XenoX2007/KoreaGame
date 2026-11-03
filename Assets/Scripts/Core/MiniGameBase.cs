using System;
using UnityEngine;

// Shared lifecycle for every mini game. A subclass sets itself up in Setup(),
// calls Begin() when play starts (e.g. after a countdown) and Finish() when a winner is decided.
public abstract class MiniGameBase : MonoBehaviour
{
    [Tooltip("Player count (2 or 4) used when this scene is played directly, without a GameManager.")]
    [SerializeField] int editorPlayerCount = 4;

    public event Action<MiniGameBase> Finished;

    public int PlayerCount { get; private set; }
    public bool IsRunning { get; private set; }
    public virtual bool IsPaused => false;

    protected virtual void Start()
    {
        var game = GameManager.Instance;
        int playerCount = game != null ? game.PlayerCount : editorPlayerCount;
        var router = PlayerInputRouter.Instance;
        if (game != null && game.Mode == SessionMode.Online) router.ConfigureSolo(game.LocalPlayerId);
        else router.Configure(playerCount);
        router.PlayerInput += RouteInput;
        Setup(playerCount);
    }

    protected virtual void OnDestroy()
    {
        var router = PlayerInputRouter.Existing;
        if (router != null) router.PlayerInput -= RouteInput;
    }

    // Releases still pass while paused, so a finger lifted then doesn't count as held afterwards.
    void RouteInput(int playerId, bool pressed)
    {
        if (!pressed || !IsPaused) HandlePlayerInput(playerId, pressed);
    }

    // Where a press from this device goes; online, a joining phone sends it to the host instead.
    protected virtual void HandlePlayerInput(int playerId, bool pressed) => OnPlayerInput(playerId, pressed);

    public virtual void Setup(int playerCount)
    {
        PlayerCount = playerCount;
        IsRunning = false;
    }

    public abstract void OnPlayerInput(int playerId, bool pressed);

    // Rank per player id: 1 = best. Tied players share a rank (1, 1, 3, ...).
    public abstract int[] GetRanking();

    protected void Begin() => IsRunning = true;

    protected void Finish()
    {
        if (!IsRunning) return;
        IsRunning = false;
        if (GameManager.Instance != null) GameManager.Instance.ReportResult(GetRanking());
        Finished?.Invoke(this);
    }

    // Higher score = better rank. Works for distance run, lives left, team rope position, etc.
    public static int[] RanksFromScores(float[] scores)
    {
        var ranks = new int[scores.Length];
        for (int i = 0; i < scores.Length; i++)
        {
            ranks[i] = 1;
            for (int j = 0; j < scores.Length; j++)
                if (scores[j] > scores[i]) ranks[i]++;
        }
        return ranks;
    }
}
