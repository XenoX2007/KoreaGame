using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum SessionMode
{
    Single,
    Tournament,
    Online,
}

// Lives across scenes; owns the player count, the session mode and tournament scores.
public class GameManager : MonoBehaviour
{
    [SerializeField] string menuScene = "Menu";
    [SerializeField] string podiumScene = "Podium";

    public static GameManager Instance { get; private set; }

    public int PlayerCount { get; private set; } = 2;
    public SessionMode Mode { get; private set; }
    public int[] Scores { get; private set; } = Array.Empty<int>();
    public int[] LastRanking { get; private set; } = Array.Empty<int>();

    // The tournament's cup and its games in play order, chosen on the cup screen.
    public CupKind Cup { get; private set; }
    public IReadOnlyList<string> TournamentScenes { get; private set; } = Array.Empty<string>();
    // Which of TournamentScenes is being played, from 0.
    public int Stage { get; private set; }

    // Online, which player this phone is (each phone has one player).
    public int LocalPlayerId { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    // Tournament points: 1st = 3, 2nd = 2, 3rd = 1, otherwise 0.
    public static int PointsForRank(int rank) => rank >= 1 && rank <= 3 ? 4 - rank : 0;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    public void StartSingle(string scene, int playerCount)
    {
        BeginSession(SessionMode.Single, playerCount);
        SceneManager.LoadScene(scene);
    }

    public void StartTournament(int playerCount, CupKind kind, IReadOnlyList<string> scenes)
    {
        if (!CupPicker.IsValid(kind, scenes, Application.CanStreamedLevelBeLoaded))
            throw new ArgumentException($"Not a playable {kind} cup: {string.Join(", ", scenes ?? Array.Empty<string>())}");

        BeginSession(SessionMode.Tournament, playerCount);
        Cup = kind;
        TournamentScenes = scenes.ToArray();
        SceneManager.LoadScene(TournamentScenes[0]);
    }

    // Called by OnlineSession on every phone when the host starts the game.
    public void StartOnline(string scene, int playerCount, int localPlayerId)
    {
        BeginSession(SessionMode.Online, playerCount);
        LocalPlayerId = localPlayerId;
        SceneManager.LoadScene(scene);
    }

    // Called by MiniGameBase.Finish().
    public void ReportResult(int[] ranking)
    {
        LastRanking = ranking;
        if (Mode != SessionMode.Tournament) return;

        for (int i = 0; i < ranking.Length; i++)
            Scores[i] += PointsForRank(ranking[i]);
    }

    // True once enough mini game scenes are in the build: 3 for the Mini Cup, all 6 for the Grand Cup.
    public static bool CupReady(CupKind kind) =>
        CupPicker.AllGames.Count(game => Application.CanStreamedLevelBeLoaded(game.Scene)) >= CupPicker.GameCount(kind);

    public void ReturnToMenu() => SceneManager.LoadScene(menuScene);

    // Called by the result screen once players are ready to move on.
    public void Continue()
    {
        if (Mode == SessionMode.Single)
        {
            ReturnToMenu();
            return;
        }

        Stage++;
        SceneManager.LoadScene(Stage < TournamentScenes.Count ? TournamentScenes[Stage] : podiumScene);
    }

    void BeginSession(SessionMode mode, int playerCount)
    {
        SeatLayout.Get(playerCount); // validates the count
        Mode = mode;
        PlayerCount = playerCount;
        Scores = new int[playerCount];
        LastRanking = Array.Empty<int>();
        Cup = CupKind.Mini;
        TournamentScenes = Array.Empty<string>();
        Stage = 0;
        LocalPlayerId = 0;
    }
}
