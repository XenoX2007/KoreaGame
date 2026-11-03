using System;
using UnityEngine;
using UnityEngine.UIElements;

// The menu's online card: host a room or join one by code, then wait in the room until it is full.
// It follows OnlineSession's state, so it also shows why a player was sent back to the menu,
// and a join link that opened the app goes straight to joining.
public class OnlinePanel : IDisposable
{
    const string OnlineScene = "KeoCo"; // the only game online so far
    const float FlashSeconds = 2f;

    readonly VisualElement _panel;
    readonly VisualElement _choose;
    readonly VisualElement _join;
    readonly VisualElement _room;
    readonly VisualElement _slots;
    readonly Label _code;
    readonly Button _start;
    readonly TextField _codeField;
    readonly Func<int> _playerCount;
    bool _joining; // the code box is showing
    string _flash;
    float _flashAt = float.NegativeInfinity;

    static OnlineSession Session => OnlineSession.Instance;

    public OnlinePanel(VisualElement root, Func<int> playerCount)
    {
        _playerCount = playerCount;
        _panel = root.Q("onlinePanel");
        _choose = root.Q("onlineChoose");
        _join = root.Q("onlineJoin");
        _room = root.Q("onlineRoom");
        _slots = root.Q("onlineSlots");
        _code = root.Q<Label>("roomCode");
        _start = root.Q<Button>("startButton");
        _codeField = root.Q<TextField>("codeField");

        root.Q<Button>("onlineButton").clicked += Open;
        root.Q<Button>("hostButton").clicked += () => _ = Session.HostAsync(_playerCount());
        root.Q<Button>("joinButton").clicked += () =>
        {
            _joining = true;
            Refresh();
        };
        root.Q<Button>("joinConfirmButton").clicked += () => Join(_codeField.value);
        root.Q<Button>("shareButton").clicked += () =>
        {
            if (!Share.Text(JoinLink.ShareText(Session.Code), Localization.Text("Mời bạn chơi"))) Flash("Đã sao chép!");
        };
        root.Q<Button>("copyButton").clicked += () =>
        {
            Share.Copy(JoinLink.ShareText(Session.Code));
            Flash("Đã sao chép!");
        };
        _start.clicked += () => Session.StartGame(OnlineScene);

        var close = root.Q<Button>("onlineCloseButton");
        close.clicked += Close;
        Localization.Track(close, () => Localization.Text(InRoom ? "Rời phòng" : "Đóng"));
        Localization.Track(root.Q<Label>("onlineStatus"), StatusText);

        Session.Changed += Refresh;
        Session.JoinLinkOpened += Join;

        // Back on the menu after a problem, or opened from a join link.
        if (Session.Notice != null || Session.State == OnlineState.Lobby) Open();
        if (Session.PendingJoinCode != null) Join(Session.PendingJoinCode);
    }

    public void Dispose()
    {
        Session.Changed -= Refresh;
        Session.JoinLinkOpened -= Join;
    }

    static bool InRoom => Session.State == OnlineState.Connecting || Session.State == OnlineState.Lobby;

    void Open()
    {
        _panel.style.display = DisplayStyle.Flex;
        SeatPanels.Reveal(_panel);
        Refresh();
    }

    void Close()
    {
        if (InRoom)
        {
            Session.LeaveToMenu();
            return;
        }
        Session.ClearNotice();
        _joining = false;
        _panel.style.display = DisplayStyle.None;
        _panel.RemoveFromClassList("is-in");
    }

    void Join(string text)
    {
        if (!JoinLink.TryParse(text, out string code))
        {
            Flash("Nhập mã phòng");
            return;
        }
        _codeField.value = code;
        Open();
        _ = Session.JoinAsync(code);
    }

    void Flash(string text)
    {
        _flash = text;
        _flashAt = Time.unscaledTime;
    }

    string StatusText()
    {
        if (Time.unscaledTime - _flashAt < FlashSeconds) return Localization.Text(_flash);

        switch (Session.State)
        {
            case OnlineState.Connecting:
                return Localization.Text("Đang kết nối...");
            case OnlineState.Lobby:
                if (!OnlineSession.IsHost) return Localization.Text("Đang chờ chủ phòng bắt đầu...");
                return Localization.Text(RoomFull ? "Đủ người rồi, bắt đầu thôi!" : "Đang chờ người chơi...");
            default:
                if (Session.Notice != null) return Localization.Text(Session.Notice);
                return _joining ? Localization.Text("Nhập mã phòng") : "";
        }
    }

    static bool RoomFull => Session.ConnectedPlayers >= Session.MaxPlayers && Session.MaxPlayers > 0;

    void Refresh()
    {
        if (Session.State == OnlineState.Offline && Session.Notice != null) _joining = false;

        bool lobby = Session.State == OnlineState.Lobby;
        bool offline = Session.State == OnlineState.Offline;
        Show(_choose, offline && !_joining);
        Show(_join, offline && _joining);
        Show(_room, lobby);
        if (!lobby) return;

        _code.text = Session.Code;
        Show(_start, OnlineSession.IsHost);
        _start.SetEnabled(RoomFull);

        _slots.Clear();
        for (int i = 0; i < Session.MaxPlayers; i++)
        {
            var slot = new Label($"P{i + 1}") { pickingMode = PickingMode.Ignore };
            slot.AddToClassList("online-slot");
            slot.AddToClassList($"player--p{i + 1}");
            slot.EnableInClassList("is-empty", i >= Session.ConnectedPlayers);
            _slots.Add(slot);
        }
    }

    static void Show(VisualElement element, bool visible) =>
        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
}
