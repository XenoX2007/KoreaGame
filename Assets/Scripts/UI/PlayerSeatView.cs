using UnityEngine.UIElements;

// One player's rotated seat UI, instantiated from PlayerSeat.uxml.
public class PlayerSeatView
{
    const float FeedbackSeconds = 0.6f;

    string _feedbackClass;
    double _feedbackAt = double.NegativeInfinity;

    public VisualElement Root { get; }
    public VisualElement Ring { get; }
    public VisualElement Disc { get; }
    public Label Feedback { get; }
    public Label Chant { get; }

    public PlayerSeatView(VisualTreeAsset template, Seat seat, bool duel)
    {
        Root = template.Instantiate();
        Root.pickingMode = PickingMode.Ignore;
        Root.AddToClassList("seat");
        Root.AddToClassList($"player--p{seat.PlayerId + 1}");
        Root.EnableInClassList("seat--duel", duel);
        SeatPanels.Place(Root, seat);

        Ring = Root.Q("seatRing");
        Disc = Root.Q("seatDisc");
        Feedback = Root.Q<Label>("seatFeedback");
        Chant = Root.Q<Label>("seatChant");
        Root.Q<Label>("seatLabel").text = $"P{seat.PlayerId + 1}";
    }

    public void ShowFeedback(string text, string className, double now)
    {
        if (_feedbackClass != null) Feedback.RemoveFromClassList(_feedbackClass);
        Feedback.AddToClassList(className);
        Feedback.text = text;
        _feedbackClass = className;
        _feedbackAt = now;
    }

    public void UpdateFeedback(double now) =>
        Feedback.style.opacity = System.Math.Clamp(1f - (float)(now - _feedbackAt) / FeedbackSeconds, 0f, 1f);
}
