using System;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// End-of-round card shared by the mini games (ResultsOverlay.uxml). The fun fact cycles through
// its languages; buttons depend on whether this is a tournament.
public class ResultsOverlay
{
    const float FactSeconds = 2f;

    readonly VisualElement _results;
    readonly Label _title;
    readonly Label _fact;
    readonly Button _continue;
    readonly Button _playAgain;
    readonly Button _backToMenu;
    string[] _facts;
    double _shownAt;

    public event Action PlayAgain;

    public ResultsOverlay(VisualElement root)
    {
        _results = root.Q("results");
        _title = root.Q<Label>("resultTitle");
        _fact = root.Q<Label>("resultFact");
        _continue = root.Q<Button>("continueButton");
        _playAgain = root.Q<Button>("playAgainButton");
        _backToMenu = root.Q<Button>("backToMenuButton");

        _continue.clicked += () => GameManager.Instance.Continue();
        _playAgain.clicked += () => PlayAgain?.Invoke();
        _backToMenu.clicked += BackToMenu;
    }

    public static string FactAt(string[] facts, double elapsed) => facts[(int)(elapsed / FactSeconds) % facts.Length];

    public void Show(string title, string[] facts, double now)
    {
        _title.text = title;
        _facts = facts;
        _shownAt = now;

        bool tournament = GameManager.Instance != null && GameManager.Instance.Mode == SessionMode.Tournament;
        _continue.style.display = tournament ? DisplayStyle.Flex : DisplayStyle.None;
        _playAgain.style.display = tournament ? DisplayStyle.None : DisplayStyle.Flex;
        _backToMenu.style.display = tournament ? DisplayStyle.None : DisplayStyle.Flex;

        _results.style.display = DisplayStyle.Flex;
        SeatPanels.Reveal(_results);
    }

    public void Hide()
    {
        _facts = null;
        _results.style.display = DisplayStyle.None;
        _results.RemoveFromClassList("is-in");
    }

    public void Update(double now)
    {
        if (_facts != null) _fact.text = FactAt(_facts, now - _shownAt);
    }

    static void BackToMenu()
    {
        if (GameManager.Instance != null) GameManager.Instance.ReturnToMenu();
        else SceneManager.LoadScene("Menu");
    }
}
