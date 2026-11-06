using System;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// End-of-round card shared by the mini games (ResultsOverlay.uxml). The fun fact holds one line per
// language (Vietnamese, Korean, English); buttons and the "Trò 2/3" line depend on whether this is a tournament.
public class ResultsOverlay
{
    readonly VisualElement _results;
    readonly Label _stage;
    readonly Label _title;
    readonly Label _fact;
    readonly Button _continue;
    readonly Button _playAgain;
    readonly Button _backToMenu;
    readonly Button _room;
    readonly VisualElement _actions;
    Func<string> _titleText;
    string[] _facts;
    bool _tournament;

    public event Action PlayAgain;

    public ResultsOverlay(VisualElement root)
    {
        _results = root.Q("results");
        _stage = root.Q<Label>("resultStage");
        _title = root.Q<Label>("resultTitle");
        _fact = root.Q<Label>("resultFact");
        _continue = root.Q<Button>("continueButton");
        _playAgain = root.Q<Button>("playAgainButton");
        _backToMenu = root.Q<Button>("backToMenuButton");
        _room = root.Q<Button>("roomButton");
        _actions = root.Q("resultActions");

        _continue.clicked += () => GameManager.Instance.Continue();
        _room.clicked += () => OnlineSession.Instance.ReturnToRoom();
        _playAgain.clicked += () => PlayAgain?.Invoke();
        _backToMenu.clicked += BackToMenu;
    }

    public void Show(Func<string> title, string[] facts)
    {
        _titleText = title;
        _facts = facts;

        var mode = GameManager.Instance != null ? GameManager.Instance.Mode : SessionMode.Single;
        bool tournament = mode == SessionMode.Tournament;
        _tournament = tournament;
        // Online only the host can start another round; everyone can leave.
        bool canReplay = !tournament && (mode != SessionMode.Online || OnlineSession.IsHost);
        // ...and take everyone back to the room to pick another game.
        bool canPickAnother = mode == SessionMode.Online && OnlineSession.IsHost;
        _continue.style.display = tournament ? DisplayStyle.Flex : DisplayStyle.None;
        _playAgain.style.display = canReplay ? DisplayStyle.Flex : DisplayStyle.None;
        _room.style.display = canPickAnother ? DisplayStyle.Flex : DisplayStyle.None;
        _actions.EnableInClassList("result-actions--three", canPickAnother);
        _backToMenu.style.display = tournament ? DisplayStyle.None : DisplayStyle.Flex;
        _stage.style.display = tournament ? DisplayStyle.Flex : DisplayStyle.None;

        _results.style.display = DisplayStyle.Flex;
        SeatPanels.Reveal(_results);
    }

    public void Hide()
    {
        _facts = null;
        _results.style.display = DisplayStyle.None;
        _results.RemoveFromClassList("is-in");
    }

    public void Update()
    {
        if (_facts == null) return;
        _title.text = _titleText();
        _fact.text = Localization.Pick(_facts);
        if (_tournament)
        {
            var game = GameManager.Instance;
            _stage.text = string.Format(Localization.Text("Trò {0}/{1}"), game.Stage + 1, game.TournamentScenes.Count);
        }
    }

    public static void BackToMenu()
    {
        if (OnlineSession.Instance != null && OnlineSession.Instance.State != OnlineState.Offline) OnlineSession.Instance.LeaveToMenu();
        else if (GameManager.Instance != null) GameManager.Instance.ReturnToMenu();
        else SceneManager.LoadScene("Menu");
    }
}
