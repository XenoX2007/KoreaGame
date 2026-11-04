using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Final tournament standings. Columns show 2nd, 1st, 3rd, 4th from the left, the classic podium shape.
[RequireComponent(typeof(PanelRenderer))]
public class PodiumScreen : MonoBehaviour
{
    [Tooltip("Scores shown when this scene is played directly, without a GameManager.")]
    [SerializeField] int[] previewScores = { 6, 7, 3, 2 };

    PodiumCelebration _celebration;
    // Read by MenuFlowTests.
    VisualElement Root { get; set; }

    void Awake() => GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUiLoaded);

    void OnDestroy() => GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUiLoaded);

    void Start() => UiBuilder.EnsureEventSystem();

    void Update()
    {
        if (_celebration != null) _celebration.Update(Time.unscaledDeltaTime);
    }

    void OnUiLoaded(PanelRenderer panel, VisualElement root)
    {
        Root = root;
        SeatPanels.FitSafeArea(root.Q("safeArea"));
        root.Q<Button>("backToMenu").clicked += BackToMenu;

        var game = GameManager.Instance;
        var scores = game != null ? game.Scores : previewScores;
        var ranks = MiniGameBase.RanksFromScores(scores.Select(s => (float)s).ToArray());
        var order = StandingsOrder(ranks);
        var everyone = new List<CharacterView>();
        var champions = new List<CharacterView>();

        for (int i = 0; i < SeatLayout.MaxPlayers; i++)
        {
            var column = root.Q($"column{i}");
            int place = i == 0 ? 1 : i == 1 ? 0 : i; // column index -> position in the standings

            if (place >= order.Length)
            {
                column.AddToClassList("is-empty");
                continue;
            }

            int player = order[place];
            column.AddToClassList($"podium-column--rank{Mathf.Min(ranks[player], 4)}");
            column.AddToClassList($"podium-column--p{player + 1}");
            var character = new CharacterView(player);
            column.Q(className: "podium-token").Add(character.Root);
            everyone.Add(character);
            if (ranks[player] == 1)
            {
                character.HoldCup();
                champions.Add(character);
            }

            int points = scores[player];
            Localization.Track(column.Q<Label>(className: "podium-score"), () => Localization.Points(points));
            column.Q<Label>(className: "podium-rank").text = ranks[player].ToString();
        }

        if (game != null && game.Mode == SessionMode.Tournament && game.TournamentScenes.Count > 0) ShowCup(root, game);

        Localization.Bind(root);
        SeatPanels.Reveal(root.Q("podium"));
        _celebration = new PodiumCelebration(root.Q("rayLayer"), root.Q("confettiLayer"), everyone, champions);
    }

    // "Cúp Nhỏ · 3 trò" and the icons of the games that were played, in order.
    static void ShowCup(VisualElement root, GameManager game)
    {
        root.Q("podiumCup").style.display = DisplayStyle.Flex;
        var kind = game.Cup;
        int count = game.TournamentScenes.Count;
        Localization.Track(root.Q<Label>("podiumCupName"), () =>
            $"{Localization.Text(kind == CupKind.Mini ? "Cúp Nhỏ" : "Cúp Lớn")} · {string.Format(Localization.Text("{0} trò"), count)}");

        var games = root.Q("podiumCupGames");
        foreach (var scene in game.TournamentScenes)
            games.Add(CupPanel.Icon(CupPicker.Find(scene)));
    }

    // Player ids from first place to last; ties keep seat order.
    public static int[] StandingsOrder(int[] ranks) =>
        Enumerable.Range(0, ranks.Length).OrderBy(id => ranks[id]).ToArray();

    static void BackToMenu()
    {
        if (GameManager.Instance != null) GameManager.Instance.ReturnToMenu();
        else SceneManager.LoadScene("Menu");
    }
}
