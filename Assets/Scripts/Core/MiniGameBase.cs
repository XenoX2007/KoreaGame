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

    protected virtual void Start()
    {
        int playerCount = GameManager.Instance != null ? GameManager.Instance.PlayerCount : editorPlayerCount;
        var router = PlayerInputRouter.Instance;
        router.Configure(playerCount);
        router.PlayerInput += OnPlayerInput;
        Setup(playerCount);
    }

    protected virtual void OnDestroy()
    {
        var router = PlayerInputRouter.Existing;
        if (router != null) router.PlayerInput -= OnPlayerInput;
    }

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
