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

    void Awake() => GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUiLoaded);

    void OnDestroy() => GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUiLoaded);

    void Start() => UiBuilder.EnsureEventSystem();

    void OnUiLoaded(PanelRenderer panel, VisualElement root)
    {
        SeatPanels.FitSafeArea(root.Q("safeArea"));
        root.Q<Button>("backToMenu").clicked += BackToMenu;

        var game = GameManager.Instance;
        var scores = game != null ? game.Scores : previewScores;
        var ranks = MiniGameBase.RanksFromScores(scores.Select(s => (float)s).ToArray());
        var order = StandingsOrder(ranks);

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
            column.Q<Label>(className: "podium-player").text = $"P{player + 1}";
            column.Q<Label>(className: "podium-score").text = $"{scores[player]} điểm";
            column.Q<Label>(className: "podium-rank").text = ranks[player].ToString();
        }

        SeatPanels.Reveal(root.Q("podium"));
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
