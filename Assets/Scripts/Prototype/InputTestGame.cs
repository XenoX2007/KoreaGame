using UnityEngine;
using UnityEngine.UI;

// Device test for multi-touch: each seat zone lights up in its player's color while held.
// Goal on a phone: all 4 seats lit at once, with each finger staying on its own player.
public class InputTestGame : MiniGameBase
{
    static readonly Rect ToggleZone = new Rect(0.42f, 0.42f, 0.16f, 0.16f);

    SeatCanvas _canvas;
    Image[] _zones;
    Image[] _buttons;
    Text[] _labels;
    int[] _pressCounts;
    Text _status;
    int _maxHeldAtOnce;

    protected override void Start()
    {
        _canvas = SeatCanvas.Create("InputTestCanvas");
        UiBuilder.EnsureEventSystem();
        CreateToggleButton();
        _status = UiBuilder.Text(UiBuilder.Rect(_canvas.SafeArea, "Status", new Vector2(0.5f, 0.32f), new Vector2(900f, 120f)), "", 36, Color.white);

        // The center button must not count as a seat press.
        PlayerInputRouter.Instance.IgnoredZones.Add(ToggleZone);
        base.Start();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        var router = PlayerInputRouter.Instance;
        if (router != null) router.IgnoredZones.Remove(ToggleZone);
    }

    public override void Setup(int playerCount)
    {
        base.Setup(playerCount);
        BuildSeats();
        _maxHeldAtOnce = 0;
        Begin();
    }

    public override void OnPlayerInput(int playerId, bool pressed)
    {
        if (pressed) _pressCounts[playerId]++;

        var color = SeatLayout.PlayerColors[playerId];
        _zones[playerId].color = new Color(color.r, color.g, color.b, pressed ? 0.55f : 0.12f);
        _buttons[playerId].color = pressed ? Color.white : color;
        _labels[playerId].text = $"P{playerId + 1}\n{_pressCounts[playerId]}";
    }

    public override int[] GetRanking()
    {
        var scores = new float[PlayerCount];
        for (int i = 0; i < PlayerCount; i++) scores[i] = _pressCounts[i];
        return RanksFromScores(scores);
    }

    void Update()
    {
        var router = PlayerInputRouter.Instance;
        int held = 0;
        for (int i = 0; i < PlayerCount; i++)
            if (router.IsPressed(i)) held++;
        _maxHeldAtOnce = Mathf.Max(_maxHeldAtOnce, held);

        _status.text = $"{PlayerCount} players | fingers down: {router.ActiveFingerCount}\n" +
                       $"players held now: {held} | best at once: {_maxHeldAtOnce}/{PlayerCount}";
    }

    void BuildSeats()
    {
        if (_zones != null)
        {
            foreach (var zone in _zones) Destroy(zone.gameObject);
            foreach (var button in _buttons) Destroy(button.transform.parent.gameObject);
        }

        var seats = SeatLayout.Get(PlayerCount);
        _zones = new Image[PlayerCount];
        _buttons = new Image[PlayerCount];
        _labels = new Text[PlayerCount];
        _pressCounts = new int[PlayerCount];

        foreach (var seat in seats)
        {
            int id = seat.PlayerId;
            var color = SeatLayout.PlayerColors[id];

            _zones[id] = UiBuilder.Image(_canvas.CreateScreenRegion($"Zone P{id + 1}", seat.Zone), new Color(color.r, color.g, color.b, 0.12f));

            var root = _canvas.CreateSeatRoot(seat, new Vector2(220f, 220f));
            _buttons[id] = UiBuilder.Image(UiBuilder.Rect(root, "Button"), color);
            _labels[id] = UiBuilder.Text(UiBuilder.Rect(root, "Label"), $"P{id + 1}\n0", 64, Color.black);
        }
    }

    void CreateToggleButton()
    {
        var rect = _canvas.CreateScreenRegion("Toggle 2P/4P", ToggleZone);
        rect.SetAsLastSibling();
        var image = UiBuilder.Image(rect, new Color(1f, 1f, 1f, 0.85f));
        image.raycastTarget = true;
        rect.gameObject.AddComponent<Button>().onClick.AddListener(() =>
        {
            int next = PlayerCount == 2 ? 4 : 2;
            PlayerInputRouter.Instance.Configure(next);
            Setup(next);
        });
        UiBuilder.Text(UiBuilder.Rect(rect, "Label"), "2P / 4P", 40, Color.black);
    }
}
