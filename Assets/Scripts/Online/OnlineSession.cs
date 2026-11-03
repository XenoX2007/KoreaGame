using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum OnlineState
{
    Offline,
    Connecting,
    Lobby,
    Playing,
}

// Online play, one phone per player. The host phone creates a room with a join code and runs the
// rules; the other phones join by code or link, send their presses and draw what the host sends.
// Phones talk through Netcode named messages, and each loads scenes itself when the host says so,
// so no networked prefabs or scene objects are needed. Creates itself and survives scene loads.
public class OnlineSession : MonoBehaviour
{
    public delegate void Write(ref FastBufferWriter writer);

    // Handled here rather than in a game scene, because they arrive on the menu.
    const string HelloMessage = "hello"; // client -> host, once connected
    const string LobbyMessage = "lobby"; // host -> clients: players connected, room size
    const string StartMessage = "start"; // host -> one client: its player id, player count, scene
    const string ReadyMessage = "ready"; // client -> host: the scene is loaded and listening

    public static OnlineSession Instance { get; private set; }

    NetworkManager _network;
    ISession _session;
    Dictionary<ulong, int> _seats = new Dictionary<ulong, int>();
    readonly HashSet<ulong> _ready = new HashSet<ulong>();
    bool _leaving;
    bool _hosting; // kept from when the room opened: once the connection drops, the network can't say

    // Why the room went away when nobody chose to leave. A host can only lose its own connection.
    string LostNotice => _hosting ? "Mất kết nối" : "Chủ phòng đã rời đi";

    public event Action Changed;
    public event Action<string> JoinLinkOpened;

    public OnlineState State { get; private set; }
    public string Code => _session?.Code;
    public int MaxPlayers { get; private set; }
    public int ConnectedPlayers { get; private set; }

    // A join code from a link that opened the app, waiting for the menu to use it.
    public string PendingJoinCode { get; set; }

    // Why the player was sent back to the menu (Vietnamese; shown with Localization.Text).
    public string Notice { get; private set; }

    public void ClearNotice() => Notice = null;

    public static bool IsHost => Instance != null && Instance._network != null && Instance._network.IsHost;

    // The shared network clock, so every phone agrees on when the beats fall.
    public static double Now => Instance._network.ServerTime.Time;

    // Domain reload is disabled in this project, so statics survive between play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject(nameof(OnlineSession));
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<OnlineSession>();
    }

    void Awake()
    {
        Application.deepLinkActivated += OnDeepLink;
        if (!string.IsNullOrEmpty(Application.absoluteURL)) OnDeepLink(Application.absoluteURL);

        // Lets a desktop build join from the command line (-join CODE), for testing without a phone.
        var args = Environment.GetCommandLineArgs();
        int join = Array.IndexOf(args, "-join");
        if (join >= 0 && join + 1 < args.Length) OnDeepLink(args[join + 1]);
    }

    void OnDestroy() => Application.deepLinkActivated -= OnDeepLink;

    void OnDeepLink(string url)
    {
        if (!JoinLink.TryParse(url, out string code)) return;
        PendingJoinCode = code;
        JoinLinkOpened?.Invoke(code);
    }

    public async Task HostAsync(int playerCount)
    {
        _hosting = true;
        await Connect(async () =>
        {
            var options = new SessionOptions { MaxPlayers = playerCount, IsPrivate = true }.WithRelayNetwork();
            _session = await MultiplayerService.Instance.CreateSessionAsync(options);
        });
    }

    public async Task JoinAsync(string code)
    {
        PendingJoinCode = null;
        _hosting = false;
        await Connect(async () => _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code));
    }

    async Task Connect(Func<Task> open)
    {
        if (State != OnlineState.Offline) return;
        State = OnlineState.Connecting;
        Notice = null;
        Changed?.Invoke();

        try
        {
            EnsureNetwork();
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
            await open();

            _session.RemovedFromSession += OnRemovedFromSession;
            _session.Deleted += OnRemovedFromSession;
            MaxPlayers = _session.MaxPlayers;
            ConnectedPlayers = _session.PlayerCount;
            State = OnlineState.Lobby;
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Online: {e}");
            var error = e as SessionException;
            LeaveToMenu(FriendlyError(error?.Error ?? SessionError.Unknown, e.Message));
        }
    }

    // Turns a failure into one of a few lines a player understands (Vietnamese keys for Localization).
    public static string FriendlyError(SessionError error, string message)
    {
        if (error == SessionError.SessionNotFound || error == SessionError.InvalidSessionIdentifier ||
            message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Không tìm thấy phòng";
        if (message.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("locked", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Phòng đã đủ người";
        return "Không kết nối được";
    }

    // The host starts the game once the room is full: every phone loads the scene with its own player id.
    public async void StartGame(string scene)
    {
        if (!IsHost || State != OnlineState.Lobby) return;

        _seats = OnlineSeats.Assign(_network.LocalClientId, _network.ConnectedClientsIds);
        _ready.Clear();
        foreach (var seat in _seats)
        {
            if (seat.Key == _network.LocalClientId) continue;
            int player = seat.Value;
            Send(StartMessage, seat.Key, (ref FastBufferWriter writer) =>
            {
                writer.WriteValueSafe(player);
                writer.WriteValueSafe(MaxPlayers);
                writer.WriteValueSafe(scene);
            });
        }
        BeginGame(scene, MaxPlayers, 0);

        // Nobody can join a game that has already started.
        try
        {
            var host = _session.AsHost();
            host.IsLocked = true;
            await host.SavePropertiesAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Online: could not lock the room: {e.Message}");
        }
    }

    void BeginGame(string scene, int playerCount, int localPlayerId)
    {
        State = OnlineState.Playing;
        Changed?.Invoke();
        GameManager.Instance.StartOnline(scene, playerCount, localPlayerId);
    }

    // Leaves the room (a host leaving closes it for everyone) and goes back to the menu.
    public async void LeaveToMenu(string notice = null)
    {
        if (_leaving) return;
        _leaving = true;
        Notice = notice;

        var session = _session;
        _session = null;
        State = OnlineState.Offline;
        _seats.Clear();
        _ready.Clear();
        if (SceneManager.GetActiveScene().name != "Menu")
        {
            if (GameManager.Instance != null) GameManager.Instance.ReturnToMenu();
            else SceneManager.LoadScene("Menu");
        }
        Changed?.Invoke();

        // Close the connection before leaving: the room's relay goes away with it, and a connection
        // still open at that moment logs transport errors.
        if (_network != null && _network.IsListening) _network.Shutdown();
        try
        {
            if (session != null) await session.LeaveAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Online: leaving the room failed: {e.Message}");
        }
        _leaving = false;
    }

    public int PlayerOf(ulong clientId) => _seats.TryGetValue(clientId, out int player) ? player : -1;

    public bool AllClientsReady =>
        _network.ConnectedClientsIds.All(id => id == _network.LocalClientId || _ready.Contains(id));

    public void ClearReady() => _ready.Clear();

    public void SendReady() => SendToHost(ReadyMessage);

    public void Listen(string name, CustomMessagingManager.HandleNamedMessageDelegate handler) =>
        _network.CustomMessagingManager.RegisterNamedMessageHandler(name, handler);

    public void Unlisten(string name)
    {
        if (_network != null && _network.CustomMessagingManager != null)
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(name);
    }

    public void SendToHost(string name, Write write = null, NetworkDelivery delivery = NetworkDelivery.ReliableSequenced) =>
        Send(name, NetworkManager.ServerClientId, write, delivery);

    // To every other phone; the host applies its own change directly.
    public void Broadcast(string name, Write write = null, NetworkDelivery delivery = NetworkDelivery.ReliableSequenced)
    {
        var clients = _network.ConnectedClientsIds.Where(id => id != _network.LocalClientId).ToList();
        if (clients.Count == 0) return;
        var writer = NewWriter(write);
        using (writer) _network.CustomMessagingManager.SendNamedMessage(name, clients, writer, delivery);
    }

    void Send(string name, ulong clientId, Write write = null, NetworkDelivery delivery = NetworkDelivery.ReliableSequenced)
    {
        var writer = NewWriter(write);
        using (writer) _network.CustomMessagingManager.SendNamedMessage(name, clientId, writer, delivery);
    }

    static FastBufferWriter NewWriter(Write write)
    {
        var writer = new FastBufferWriter(64, Allocator.Temp, 1024);
        write?.Invoke(ref writer);
        return writer;
    }

    void EnsureNetwork()
    {
        if (_network != null) return;
        var transport = gameObject.AddComponent<UnityTransport>();
        _network = gameObject.AddComponent<NetworkManager>();
        // Added at runtime, so there is no Inspector-made config.
        _network.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        _network.OnServerStarted += OnServerStarted;
        _network.OnClientConnectedCallback += OnClientConnected;
        _network.OnClientDisconnectCallback += OnClientDisconnected;
        _network.OnClientStopped += OnClientStopped;
        // The relay connection dropped (e.g. the phone's Wi-Fi blinked): the room is dead, so say so
        // instead of waiting in it.
        _network.OnTransportFailure += () =>
        {
            if (!_leaving && State != OnlineState.Offline) LeaveToMenu(LostNotice);
        };
    }

    void OnServerStarted()
    {
        Listen(HelloMessage, (sender, reader) => RefreshLobby());
        Listen(ReadyMessage, (sender, reader) => _ready.Add(sender));
    }

    void OnClientConnected(ulong clientId)
    {
        if (_network.IsHost)
        {
            RefreshLobby();
            return;
        }
        if (clientId != _network.LocalClientId) return;

        Listen(LobbyMessage, (sender, reader) =>
        {
            reader.ReadValueSafe(out int connected);
            reader.ReadValueSafe(out int max);
            ConnectedPlayers = connected;
            MaxPlayers = max;
            Changed?.Invoke();
        });
        Listen(StartMessage, (sender, reader) =>
        {
            reader.ReadValueSafe(out int player);
            reader.ReadValueSafe(out int count);
            reader.ReadValueSafe(out string scene);
            BeginGame(scene, count, player);
        });
        SendToHost(HelloMessage);
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (_network.IsHost)
        {
            _ready.Remove(clientId);
            RefreshLobby();
        }
        else if (clientId == _network.LocalClientId && !_leaving)
        {
            LeaveToMenu(LostNotice);
        }
    }

    void OnClientStopped(bool wasHost)
    {
        if (!_leaving && State != OnlineState.Offline) LeaveToMenu(LostNotice);
    }

    void OnRemovedFromSession()
    {
        if (!_leaving && State != OnlineState.Offline) LeaveToMenu(LostNotice);
    }

    void RefreshLobby()
    {
        if (!_network.IsHost) return;
        int connected = _network.ConnectedClientsIds.Count;
        int max = MaxPlayers;
        ConnectedPlayers = connected;
        Broadcast(LobbyMessage, (ref FastBufferWriter writer) =>
        {
            writer.WriteValueSafe(connected);
            writer.WriteValueSafe(max);
        });
        Changed?.Invoke();
    }
}
