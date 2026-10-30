using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum SessionMode
{
    Single,
    Tournament,
}

// Lives across scenes; owns the player count, the session mode and tournament scores.
public class GameManager : MonoBehaviour
{
    [SerializeField] string menuScene = "Menu";
    [SerializeField] string podiumScene = "Podium";
    [SerializeField] string[] tournamentScenes = { "KeoCo", "NguoiGo", "DaCau" };

    public static GameManager Instance { get; private set; }

    public int PlayerCount { get; private set; } = 2;
    public SessionMode Mode { get; private set; }
    public int[] Scores { get; private set; } = Array.Empty<int>();
    public int[] LastRanking { get; private set; } = Array.Empty<int>();

    int _stage;

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

    public void StartTournament(int playerCount)
    {
        BeginSession(SessionMode.Tournament, playerCount);
        SceneManager.LoadScene(tournamentScenes[0]);
    }

    // Called by MiniGameBase.Finish().
    public void ReportResult(int[] ranking)
    {
        LastRanking = ranking;
        if (Mode != SessionMode.Tournament) return;

        for (int i = 0; i < ranking.Length; i++)
            Scores[i] += PointsForRank(ranking[i]);
    }

    // True once every tournament mini game scene is in the build.
    public bool TournamentReady => Array.TrueForAll(tournamentScenes, Application.CanStreamedLevelBeLoaded);

    public void ReturnToMenu() => SceneManager.LoadScene(menuScene);

    // Called by the result screen once players are ready to move on.
    public void Continue()
    {
        if (Mode == SessionMode.Single)
        {
            ReturnToMenu();
            return;
        }

        _stage++;
        SceneManager.LoadScene(_stage < tournamentScenes.Length ? tournamentScenes[_stage] : podiumScene);
    }

    void BeginSession(SessionMode mode, int playerCount)
    {
        SeatLayout.Get(playerCount); // validates the count
        Mode = mode;
        PlayerCount = playerCount;
        Scores = new int[playerCount];
        LastRanking = Array.Empty<int>();
        _stage = 0;
    }
}
