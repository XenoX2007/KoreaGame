using System;
using UnityEngine.UIElements;

// The in-game pause button and the see-through card it opens (PauseOverlay.uxml).
public class PauseOverlay
{
    readonly VisualElement _overlay;

    public event Action PauseRequested;
    public event Action ResumeRequested;

    public Button PauseButton { get; }

    public PauseOverlay(VisualElement root)
    {
        PauseButton = root.Q<Button>("pauseButton");
        _overlay = root.Q("pause");

        PauseButton.clicked += () => PauseRequested?.Invoke();
        root.Q<Button>("resumeButton").clicked += () => ResumeRequested?.Invoke();
        root.Q<Button>("homeButton").clicked += ResultsOverlay.BackToMenu;
    }

    public bool ButtonVisible
    {
        set => PauseButton.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void Show()
    {
        _overlay.style.display = DisplayStyle.Flex;
        SeatPanels.Reveal(_overlay);
    }

    public void Hide()
    {
        _overlay.style.display = DisplayStyle.None;
        _overlay.RemoveFromClassList("is-in");
    }
}
