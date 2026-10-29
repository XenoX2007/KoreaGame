using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Device test for multi-touch: each seat zone lights up in its player's color while held.
// Goal on a phone: all 4 seats lit at once, with each finger staying on its own player.
public class InputTestGame : MiniGameBase
{
    static readonly Rect ToggleZone = new Rect(0.42f, 0.42f, 0.16f, 0.16f);

    SeatCanvas _canvas;
    Font _font;
    Image[] _zones;
    Image[] _buttons;
    Text[] _labels;
    int[] _pressCounts;
    Text _status;
    int _maxHeldAtOnce;

    protected override void Start()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _canvas = SeatCanvas.Create("InputTestCanvas");
        CreateEventSystem();
        CreateToggleButton();
        _status = CreateText(_canvas.SafeArea, "", 36);
        var statusRect = _status.rectTransform;
        statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 0.32f);
        statusRect.sizeDelta = new Vector2(900f, 120f);

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

            _zones[id] = _canvas.CreateScreenRegion($"Zone P{id + 1}", seat.Zone).gameObject.AddComponent<Image>();
            _zones[id].color = new Color(color.r, color.g, color.b, 0.12f);
            _zones[id].raycastTarget = false;

            var root = _canvas.CreateSeatRoot(seat, new Vector2(220f, 220f));
            _buttons[id] = CreateImage(root, color);
            _labels[id] = CreateText(root, $"P{id + 1}\n0", 64);
            _labels[id].color = Color.black;
        }
    }

    void CreateToggleButton()
    {
        var rect = _canvas.CreateScreenRegion("Toggle 2P/4P", ToggleZone);
        rect.SetAsLastSibling();
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.85f);
        rect.gameObject.AddComponent<Button>().onClick.AddListener(() =>
        {
            int next = PlayerCount == 2 ? 4 : 2;
            PlayerInputRouter.Instance.Configure(next);
            Setup(next);
        });
        var label = CreateText(rect, "2P / 4P", 40);
        label.color = Color.black;
    }

    static void CreateEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    static Image CreateImage(RectTransform parent, Color color)
    {
        var image = Stretch(new GameObject("Button", typeof(RectTransform)), parent).AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    Text CreateText(RectTransform parent, string value, int size)
    {
        var text = Stretch(new GameObject("Label", typeof(RectTransform)), parent).AddComponent<Text>();
        text.font = _font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    static GameObject Stretch(GameObject go, RectTransform parent)
    {
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return go;
    }
}
