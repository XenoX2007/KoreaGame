using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// The menu's cup screen: choose the Mini Cup (3 games, picked by hand or by the dice) or the
// Grand Cup (all 6, in story order or shuffled by the dice), then start the tournament.
// The dice decides first and then plays a short animation, which a tap anywhere skips.
public class CupPanel
{
    const string KindKey = "cup.kind";
    const string MiniKey = "cup.mini";
    const string SelectedClass = "is-selected";
    const string SoonClass = "is-soon";
    const float RollSeconds = 1.4f;
    const float LastHopAt = 1.25f; // the ring rests on the last pick for the remaining moment
    const int FirstLandingHop = 8;
    const int HopsBetweenLandings = 2;
    const float FlashSeconds = 1.6f;
    const float ShakeSeconds = 0.3f;
    const int RestingFace = 5;

    // Pips in a 3 x 3 grid, numbered left to right, top to bottom.
    static readonly int[][] Faces =
    {
        new[] { 4 },
        new[] { 0, 8 },
        new[] { 0, 4, 8 },
        new[] { 0, 2, 6, 8 },
        new[] { 0, 2, 4, 6, 8 },
        new[] { 0, 2, 3, 5, 6, 8 },
    };

    class Tile
    {
        public GameInfo Game;
        public Button Root;
        public Label Badge;
        public bool Playable;
    }

    // The dice's result, decided before the animation; each hop is when the ring moves and where.
    class DiceRoll
    {
        public float StartedAt;
        public string[] Result;
        public readonly List<(float At, string Scene, bool Lands)> Hops = new List<(float, string, bool)>();
    }

    readonly VisualElement _panel;
    readonly Button _mini;
    readonly Button _grand;
    readonly Button _start;
    readonly VisualElement _die;
    readonly VisualElement[] _pips = new VisualElement[9];
    readonly VisualElement _order;
    readonly List<Tile> _tiles = new List<Tile>();
    readonly Func<int> _playerCount;
    readonly System.Random _random = new System.Random();

    CupKind _kind;
    readonly List<string> _miniPicks = new List<string>();
    string[] _grandOrder = CupPicker.StoryOrder;
    bool _grandShuffled;
    DiceRoll _roll;
    int _shownHop = -1;
    string _shownOrder;
    string _flash;
    float _flashAt = float.NegativeInfinity;

    public CupPanel(VisualElement root, Func<int> playerCount)
    {
        _playerCount = playerCount;
        _panel = root.Q("cupPanel");
        _mini = root.Q<Button>("cupMini");
        _grand = root.Q<Button>("cupGrand");
        _start = root.Q<Button>("cupStart");
        _die = root.Q("cupDie");
        _order = root.Q("cupOrder");

        SeatPanels.FitSafeArea(root.Q("cupSafeArea"));
        BuildTiles(root.Q("cupGrid"));
        BuildDie();

        bool grandReady = GameManager.CupReady(CupKind.Grand);
        _grand.SetEnabled(grandReady);
        _grand.EnableInClassList(SoonClass, !grandReady);

        _mini.clicked += () => SelectKind(CupKind.Mini);
        _grand.clicked += () => SelectKind(CupKind.Grand);
        root.Q<Button>("cupDice").clicked += RollDice;
        _start.clicked += StartCup;
        var back = root.Q<Button>("cupBack");
        back.Clear();
        var backArrow = new Chevron { PointsLeft = true, pickingMode = PickingMode.Ignore };
        backArrow.AddToClassList("cup-back-arrow");
        back.Add(backArrow);
        back.clicked += Close;

        // While the dice rolls, a press anywhere finishes it and goes no further, so it can't also pick a tile.
        _panel.RegisterCallback<PointerDownEvent>(press =>
        {
            if (_roll == null) return;
            FinishRoll();
            press.StopImmediatePropagation();
        }, TrickleDown.TrickleDown);

        Localization.Track(root.Q<Label>("cupPlayers"), () => string.Format(Localization.Text("{0} người"), _playerCount()));
        Localization.Track(root.Q<Label>("cupHint"), HintText);
    }

    IReadOnlyList<string> Order => _kind == CupKind.Mini ? (IReadOnlyList<string>)_miniPicks : _grandOrder;

    bool Playable(string scene) => _tiles.Any(tile => tile.Game.Scene == scene && tile.Playable);

    public void Open()
    {
        Restore();
        _panel.style.display = DisplayStyle.Flex;
        SeatPanels.Reveal(_panel);
        Refresh();
    }

    void Close()
    {
        if (_roll != null) FinishRoll();
        _panel.style.display = DisplayStyle.None;
        _panel.RemoveFromClassList("is-in");
    }

    // Called every frame by MainMenu; runs the dice animation on unscaled time.
    public void Update()
    {
        if (_roll == null) return;

        float elapsed = Time.unscaledTime - _roll.StartedAt;
        if (elapsed >= RollSeconds)
        {
            FinishRoll();
            return;
        }

        int hop = _roll.Hops.FindLastIndex(h => h.At <= elapsed);
        if (hop != _shownHop)
        {
            _shownHop = hop;
            Refresh();
        }
        _die.style.rotate = new Rotate(new Angle(-12f + 720f * EaseOut(elapsed / RollSeconds)));
    }

    void BuildTiles(VisualElement grid)
    {
        grid.Clear();
        _tiles.Clear();
        VisualElement row = null;
        foreach (var game in CupPicker.AllGames)
        {
            if (_tiles.Count % 3 == 0)
            {
                row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("cup-grid-row");
                grid.Add(row);
            }

            var tile = new Tile { Game = game, Playable = Application.CanStreamedLevelBeLoaded(game.Scene) };
            tile.Root = new Button { name = $"cupTile{game.Scene}" };
            tile.Root.AddToClassList("cup-tile");
            tile.Root.EnableInClassList(SoonClass, !tile.Playable);
            tile.Root.SetEnabled(tile.Playable);

            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.AddToClassList("cup-tile-ring");
            tile.Root.Add(ring);
            tile.Root.Add(Icon(game));

            var name = new Label { pickingMode = PickingMode.Ignore };
            name.AddToClassList("cup-tile-name");
            Localization.Track(name, () => Localization.Text(game.NameVi));
            tile.Root.Add(name);

            var korean = new Label { pickingMode = PickingMode.Ignore };
            korean.AddToClassList("cup-tile-korean");
            Localization.Track(korean, () => BreakAtMiddleSpace(Localization.Text(game.NameKo)));
            tile.Root.Add(korean);

            tile.Badge = new Label { pickingMode = PickingMode.Ignore };
            tile.Badge.AddToClassList("cup-badge");
            tile.Root.Add(tile.Badge);

            tile.Root.clicked += () => TapTile(tile);
            row.Add(tile.Root);
            _tiles.Add(tile);
        }
    }

    void BuildDie()
    {
        _die.Clear();
        for (int i = 0; i < _pips.Length; i++)
        {
            _pips[i] = new VisualElement { pickingMode = PickingMode.Ignore };
            _pips[i].AddToClassList("cup-pip");
            _pips[i].AddToClassList($"cup-pip--{i}");
            _die.Add(_pips[i]);
        }
        ShowFace(RestingFace);
    }

    void ShowFace(int face)
    {
        for (int i = 0; i < _pips.Length; i++)
            _pips[i].style.display = Array.IndexOf(Faces[face - 1], i) >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // A game's menu icon. Bắn Bi's is two marbles, the others one picture.
    public static VisualElement Icon(GameInfo game)
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("card-icon");
        icon.AddToClassList(game.IconClass);
        if (game.IconClass == "card-icon--marble")
        {
            foreach (var team in new[] { "vn", "kr" })
            {
                var marble = new VisualElement { pickingMode = PickingMode.Ignore };
                marble.AddToClassList("card-marble");
                marble.AddToClassList($"card-marble--{team}");
                icon.Add(marble);
            }
        }
        return icon;
    }

    void SelectKind(CupKind kind)
    {
        if (_roll != null) return;
        _kind = kind;
        Save();
        Refresh();
    }

    void TapTile(Tile tile)
    {
        if (_roll != null) return;
        string scene = tile.Game.Scene;

        if (_kind == CupKind.Grand)
        {
            Shake(tile.Root);
            return;
        }

        if (!_miniPicks.Remove(scene))
        {
            if (_miniPicks.Count == CupPicker.GameCount(CupKind.Mini))
            {
                Shake(tile.Root);
                Flash("Đã chọn đủ 3 trò");
                return;
            }
            _miniPicks.Add(scene);
        }
        Save();
        Refresh();
    }

    void RollDice()
    {
        if (_roll != null) return;

        var roll = new DiceRoll { StartedAt = Time.unscaledTime };
        if (_kind == CupKind.Mini)
        {
            var playable = _tiles.Where(tile => tile.Playable).Select(tile => tile.Game.Scene).ToArray();
            roll.Result = CupPicker.Roll(_random, playable, CupPicker.GameCount(CupKind.Mini));
        }
        else roll.Result = CupPicker.Shuffle(_random, CupPicker.StoryOrder);

        // The ring wanders over playable tiles, landing on each pick in turn, fast at first and then slowing.
        var stops = _tiles.Where(tile => tile.Playable).Select(tile => tile.Game.Scene).ToArray();
        int total = FirstLandingHop + HopsBetweenLandings * (roll.Result.Length - 1);
        int landed = 0;
        string at = null;
        for (int hop = 1; hop <= total; hop++)
        {
            bool lands = hop == FirstLandingHop + HopsBetweenLandings * landed;
            var elsewhere = stops.Where(s => s != at).ToArray();
            string scene = lands ? roll.Result[landed++] : elsewhere[_random.Next(elsewhere.Length)];
            float time = LastHopAt * (1f - Mathf.Sqrt(1f - (float)hop / total)); // inverse of an ease-out
            roll.Hops.Add((time, scene, lands));
            at = scene;
        }

        _roll = roll;
        _shownHop = -1;
        Refresh();
    }

    void FinishRoll()
    {
        var result = _roll.Result;
        _roll = null;
        _shownHop = -1;
        if (_kind == CupKind.Mini)
        {
            _miniPicks.Clear();
            _miniPicks.AddRange(result);
        }
        else
        {
            _grandOrder = result;
            _grandShuffled = true;
        }
        _die.style.rotate = StyleKeyword.Null;
        ShowFace(RestingFace);
        Save();
        Refresh();
    }

    void StartCup()
    {
        if (_roll != null || !CupPicker.IsValid(_kind, Order, Playable)) return;
        Save();
        GameManager.Instance.StartTournament(_playerCount(), _kind, Order.ToArray());
    }

    void Refresh()
    {
        _mini.EnableInClassList(SelectedClass, _kind == CupKind.Mini);
        _grand.EnableInClassList(SelectedClass, _kind == CupKind.Grand);

        // While rolling, only the picks the ring has landed on so far are numbered.
        IReadOnlyList<string> shown = Order;
        string hopAt = null;
        if (_roll != null)
        {
            var passed = _roll.Hops.Take(_shownHop + 1).ToList();
            shown = passed.Where(h => h.Lands).Select(h => h.Scene).ToArray();
            hopAt = passed.Count > 0 ? passed[passed.Count - 1].Scene : null;
            ShowFace(_random.Next(1, 7));
        }

        foreach (var tile in _tiles)
        {
            int number = IndexOf(shown, tile.Game.Scene) + 1;
            bool wasShown = tile.Badge.style.display == DisplayStyle.Flex;
            tile.Root.EnableInClassList("is-picked", number > 0);
            tile.Root.EnableInClassList("is-hop", tile.Game.Scene == hopAt);
            tile.Badge.text = number > 0 ? number.ToString() : "";
            tile.Badge.style.display = number > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (number > 0 && !wasShown) Pop(tile.Badge);
        }

        _start.SetEnabled(_roll == null && CupPicker.IsValid(_kind, Order, Playable));
        ShowOrder(shown);
    }

    // The play order as small icons with arrows between them.
    void ShowOrder(IReadOnlyList<string> order)
    {
        string key = string.Join(",", order);
        if (key == _shownOrder) return;
        _shownOrder = key;

        _order.Clear();
        _order.EnableInClassList("cup-order--six", order.Count > CupPicker.GameCount(CupKind.Mini));
        for (int i = 0; i < order.Count; i++)
        {
            if (i > 0)
            {
                var arrow = new Chevron { pickingMode = PickingMode.Ignore };
                arrow.AddToClassList("cup-order-arrow");
                _order.Add(arrow);
            }
            _order.Add(Icon(CupPicker.Find(order[i])));
        }
    }

    static int IndexOf(IReadOnlyList<string> list, string scene)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == scene) return i;
        return -1;
    }

    string HintText()
    {
        if (Time.unscaledTime - _flashAt < FlashSeconds) return Localization.Text(_flash);
        if (_kind == CupKind.Grand)
            return Localization.Text(_grandShuffled ? "Cả 6 trò, thứ tự đã xáo" : "Cả 6 trò theo thứ tự câu chuyện");
        return Localization.Text(_miniPicks.Count == CupPicker.GameCount(CupKind.Mini) ? "Đã chọn 3 trò" : "Chọn 3 trò hoặc tung xúc xắc");
    }

    void Flash(string text)
    {
        _flash = text;
        _flashAt = Time.unscaledTime;
    }

    // A badge appears small, then grows into place (the class is removed after the first layout).
    static void Pop(VisualElement badge)
    {
        badge.AddToClassList("is-new");
        badge.schedule.Execute(() => badge.RemoveFromClassList("is-new")).ExecuteLater(16);
    }

    // A quick side-to-side wobble that dies out: "this tile can't be picked now".
    static void Shake(VisualElement tile)
    {
        float start = Time.unscaledTime;
        tile.schedule.Execute(() =>
        {
            float t = Time.unscaledTime - start;
            tile.style.translate = t < ShakeSeconds
                ? new Translate(Mathf.Sin(t * 60f) * 14f * (1f - t / ShakeSeconds), 0)
                : (StyleTranslate)StyleKeyword.Null;
        }).Every(16).Until(() => Time.unscaledTime - start > ShakeSeconds + 0.1f);
    }

    static float EaseOut(float x) => 1f - (1f - x) * (1f - x);

    // Hangul wraps between any two letters, so a long name gets its own line break at the space nearest the middle.
    static string BreakAtMiddleSpace(string text)
    {
        if (text.Length <= 10) return text;
        int best = -1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == ' ' && (best < 0 || Mathf.Abs(i - text.Length / 2) < Mathf.Abs(best - text.Length / 2))) best = i;
        return best < 0 ? text : text.Substring(0, best) + "\n" + text.Substring(best + 1);
    }

    // The last cup and the last Mini Cup picks, dropping anything that is no longer in the build.
    void Restore()
    {
        if (_roll != null) FinishRoll();

        _kind = PlayerPrefs.GetInt(KindKey, (int)CupKind.Mini) == (int)CupKind.Grand && GameManager.CupReady(CupKind.Grand)
            ? CupKind.Grand
            : CupKind.Mini;

        _miniPicks.Clear();
        foreach (var scene in PlayerPrefs.GetString(MiniKey, "").Split(','))
        {
            if (_miniPicks.Count == CupPicker.GameCount(CupKind.Mini)) break;
            if (CupPicker.IsKnown(scene) && Playable(scene) && !_miniPicks.Contains(scene)) _miniPicks.Add(scene);
        }

        _grandOrder = CupPicker.StoryOrder;
        _grandShuffled = false;
        _flashAt = float.NegativeInfinity;
    }

    void Save()
    {
        PlayerPrefs.SetInt(KindKey, (int)_kind);
        PlayerPrefs.SetString(MiniKey, string.Join(",", _miniPicks));
        PlayerPrefs.Save();
    }

    // A right-pointing chevron in the element's text color, for the play order and the back button.
    public class Chevron : VisualElement
    {
        public bool PointsLeft { get; set; }

        public Chevron() => generateVisualContent += Draw;

        void Draw(MeshGenerationContext context)
        {
            var size = contentRect.size;
            float edge = PointsLeft ? size.x * 0.8f : size.x * 0.2f;
            float tip = PointsLeft ? size.x * 0.2f : size.x * 0.8f;
            var painter = context.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = Mathf.Max(2f, size.x * 0.22f);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(new Vector2(edge, size.y * 0.15f));
            painter.LineTo(new Vector2(tip, size.y * 0.5f));
            painter.LineTo(new Vector2(edge, size.y * 0.85f));
            painter.Stroke();
        }
    }
}
