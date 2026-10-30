using UnityEngine;
using UnityEngine.UIElements;

// Main menu: pick 2 or 4 players, then a mini game or the tournament.
// Cards unlock on their own once their scene is added to the build.
// GameManager lives on its own object: a duplicate destroys its whole GameObject on returning here.
[RequireComponent(typeof(PanelRenderer))]
public class MainMenu : MonoBehaviour
{
    const string SelectedClass = "is-selected";
    const string SoonClass = "is-soon";

    VisualElement _root;
    Button _twoPlayers;
    Button _fourPlayers;
    int _playerCount;
    bool _started;

    void Awake() => GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUiLoaded);

    void OnDestroy() => GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUiLoaded);

    void Start()
    {
        UiBuilder.EnsureEventSystem();
        _started = true;
        Bind();
    }

    void OnUiLoaded(PanelRenderer panel, VisualElement root)
    {
        _root = root;
        Bind();
    }

    // Needs both the panel and GameManager.Instance, whichever arrives last.
    void Bind()
    {
        if (_root == null || !_started) return;

        var game = GameManager.Instance;
        _playerCount = game.PlayerCount;

        SeatPanels.FitSafeArea(_root.Q("safeArea"));

        _twoPlayers = _root.Q<Button>("twoPlayers");
        _fourPlayers = _root.Q<Button>("fourPlayers");
        _twoPlayers.clicked += () => SelectPlayerCount(2);
        _fourPlayers.clicked += () => SelectPlayerCount(4);
        ShowPlayerCount();

        BindGameCard(_root.Q<Button>("cardKeoCo"), "KeoCo");
        BindGameCard(_root.Q<Button>("cardNguoiGo"), "NguoiGo");
        BindGameCard(_root.Q<Button>("cardDaCau"), "DaCau");

        var tournament = _root.Q<Button>("tournament");
        tournament.SetEnabled(game.TournamentReady);
        tournament.EnableInClassList(SoonClass, !game.TournamentReady);
        tournament.clicked += () => GameManager.Instance.StartTournament(_playerCount);

        SeatPanels.Reveal(_root.Q("menu"));
    }

    void BindGameCard(Button card, string scene)
    {
        bool playable = Application.CanStreamedLevelBeLoaded(scene);
        card.SetEnabled(playable);
        card.EnableInClassList(SoonClass, !playable);
        card.clicked += () => GameManager.Instance.StartSingle(scene, _playerCount);
    }

    void SelectPlayerCount(int count)
    {
        _playerCount = count;
        ShowPlayerCount();
    }

    void ShowPlayerCount()
    {
        _twoPlayers.EnableInClassList(SelectedClass, _playerCount == 2);
        _fourPlayers.EnableInClassList(SelectedClass, _playerCount == 4);
    }
}
