using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

// Turns touches (and keyboard/mouse in the Editor) into per-player press/release events.
// A finger belongs to the seat zone it first touched until it lifts, even if it slides away.
// Extra zones (e.g. a shared button) sit on top of the seats and press ids from MaxPlayers up.
// Each seat also has a polled joystick: how far its finger has dragged from where it went down.
public class PlayerInputRouter : MonoBehaviour
{
    // Spatial keyboard mapping for testing on PC: Z = bottom-left, P = top-right, Q = top-left, M = bottom-right.
    static readonly Key[] PlayerKeys = { Key.Z, Key.P, Key.Q, Key.M };
    // Stick keys per player: up, left, down, right.
    static readonly Key[][] StickKeys =
    {
        new[] { Key.W, Key.A, Key.S, Key.D },
        new[] { Key.UpArrow, Key.LeftArrow, Key.DownArrow, Key.RightArrow },
        new[] { Key.I, Key.J, Key.K, Key.L },
        new[] { Key.Numpad8, Key.Numpad4, Key.Numpad5, Key.Numpad6 },
    };
    const int MouseFingerId = -1;
    const int NoFinger = int.MinValue;
    const int MaxExtraZones = 2;
    const int MaxIds = SeatLayout.MaxPlayers + MaxExtraZones;

    static PlayerInputRouter _instance;
    static bool _quitting;

    // Fired when a player goes from no input to pressed (true) or back to released (false).
    public event Action<int, bool> PlayerInput;

    // Normalized screen rects where new touches are ignored (e.g. a pause button).
    public readonly List<Rect> IgnoredZones = new List<Rect>();

    // A full stick tilt is a drag of this fraction of the screen height.
    public const float StickRadiusOfHeight = 0.09f;
    public static float StickRadius => StickRadiusOfHeight * Screen.height;

    public int PlayerCount { get; private set; }
    public int ActiveFingerCount => _fingerToPlayer.Count;

    readonly Dictionary<int, int> _fingerToPlayer = new Dictionary<int, int>();
    readonly int[] _sourceCount = new int[MaxIds];
    readonly bool[] _keyHeld = new bool[MaxIds];
    readonly Dictionary<int, Rect> _extraZones = new Dictionary<int, Rect>();
    readonly Dictionary<int, Vector2> _fingerStart = new Dictionary<int, Vector2>();
    readonly Dictionary<int, Vector2> _fingerAt = new Dictionary<int, Vector2>();
    readonly int[] _stickFinger = { NoFinger, NoFinger, NoFinger, NoFinger };
    Seat[] _seats = Array.Empty<Seat>();
    int _soloPlayer = -1;

    public static PlayerInputRouter Instance
    {
        get
        {
            if (_instance == null && !_quitting)
            {
                var go = new GameObject(nameof(PlayerInputRouter));
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<PlayerInputRouter>();
            }
            return _instance;
        }
    }

    // For cleanup in OnDestroy: never creates a router while a scene is shutting down.
    public static PlayerInputRouter Existing => _instance;

    // Domain reload is disabled in this project, so statics survive between play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _instance = null;
        _quitting = false;
    }

    public void Configure(int playerCount)
    {
        ReleaseAll();
        _seats = SeatLayout.Get(playerCount);
        _extraZones.Clear();
        PlayerCount = playerCount;
        _soloPlayer = -1;
    }

    // Online each phone has one player, so a touch anywhere (or Space, in the Editor) is that player.
    public void ConfigureSolo(int playerId)
    {
        ReleaseAll();
        _seats = new[] { new Seat(playerId, new Rect(0f, 0f, 1f, 1f), Vector2.zero, 0f) };
        _extraZones.Clear();
        PlayerCount = 1;
        _soloPlayer = playerId;
    }

    public bool IsPressed(int playerId) => _sourceCount[playerId] > 0;

    // A normalized screen rect whose new touches press this id (SeatLayout.MaxPlayers or above)
    // instead of the seat underneath. Registering the same id again moves its zone.
    public void RegisterExtraZone(int id, Rect normalizedRect)
    {
        if (id < SeatLayout.MaxPlayers || id >= MaxIds) throw new ArgumentOutOfRangeException(nameof(id), id, "Extra zone ids start at SeatLayout.MaxPlayers.");
        _extraZones[id] = normalizedRect;
    }

    // Fingers already down stay with their zone's id until they lift.
    public void ClearExtraZones() => _extraZones.Clear();

    public bool HasExtraZone(int id) => _extraZones.ContainsKey(id);

    // The seat's joystick in screen space (x right, y up), length 0..1: the drag of the finger that owns
    // the seat from where it went down, or its stick keys in the Editor. Zero when nothing drives it.
    public Vector2 Stick(int playerId)
    {
        if (playerId < 0 || playerId >= SeatLayout.MaxPlayers) return Vector2.zero;

        int finger = _stickFinger[playerId];
        if (finger != NoFinger) return Vector2.ClampMagnitude((_fingerAt[finger] - _fingerStart[finger]) / StickRadius, 1f);

        var keys = StickKeysOf(playerId);
        if (keys == null) return Vector2.zero;
        var keyboard = Keyboard.current;
        var direction = new Vector2(
            (keyboard[keys[3]].isPressed ? 1f : 0f) - (keyboard[keys[1]].isPressed ? 1f : 0f),
            (keyboard[keys[0]].isPressed ? 1f : 0f) - (keyboard[keys[2]].isPressed ? 1f : 0f));
        return direction.normalized;
    }

    // A finger, the mouse or a stick key is driving this seat's joystick, even before it moves.
    public bool StickActive(int playerId)
    {
        if (playerId < 0 || playerId >= SeatLayout.MaxPlayers) return false;
        if (_stickFinger[playerId] != NoFinger) return true;

        var keys = StickKeysOf(playerId);
        if (keys == null) return false;
        foreach (var key in keys)
            if (Keyboard.current[key].isPressed) return true;
        return false;
    }

    Key[] StickKeysOf(int playerId) =>
        Keyboard.current != null && _soloPlayer < 0 && playerId < PlayerCount ? StickKeys[playerId] : null;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        ETouch.onFingerDown += OnFingerDown;
        ETouch.onFingerUp += OnFingerUp;
        ETouch.onFingerMove += OnFingerMove;
    }

    void OnDisable()
    {
        ETouch.onFingerDown -= OnFingerDown;
        ETouch.onFingerUp -= OnFingerUp;
        ETouch.onFingerMove -= OnFingerMove;
        EnhancedTouchSupport.Disable();
        ReleaseAll();
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) ReleaseAll();
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused) ReleaseAll();
    }

    void OnApplicationQuit() => _quitting = true;

    void Update()
    {
        PollKeyboard();
        PollMouse();
    }

    void OnFingerDown(Finger finger) => BeginPointer(finger.index, finger.screenPosition);

    void OnFingerUp(Finger finger) => EndPointer(finger.index);

    void OnFingerMove(Finger finger) => MovePointer(finger.index, finger.screenPosition);

    void PollKeyboard()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (_soloPlayer >= 0)
        {
            PollKey(keyboard[Key.Space].isPressed, _soloPlayer);
            return;
        }

        for (int i = 0; i < PlayerCount; i++) PollKey(keyboard[PlayerKeys[i]].isPressed, i);
        // Space is the first extra zone, while it's there.
        PollKey(keyboard[Key.Space].isPressed && HasExtraZone(SeatLayout.MaxPlayers), SeatLayout.MaxPlayers);
    }

    void PollKey(bool held, int player)
    {
        if (held == _keyHeld[player]) return;
        _keyHeld[player] = held;
        if (held) Press(player);
        else Release(player);
    }

    // Lets you test touch zones with the mouse in the Editor (one pointer only).
    void PollMouse()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame) BeginPointer(MouseFingerId, mouse.position.ReadValue());
        else if (mouse.leftButton.wasReleasedThisFrame) EndPointer(MouseFingerId);
        else if (mouse.leftButton.isPressed) MovePointer(MouseFingerId, mouse.position.ReadValue());
    }

    void BeginPointer(int fingerId, Vector2 screenPosition)
    {
        if (_fingerToPlayer.ContainsKey(fingerId)) return;

        int player = PlayerAt(screenPosition);
        if (player < 0) return;

        _fingerToPlayer[fingerId] = player;
        _fingerStart[fingerId] = screenPosition;
        _fingerAt[fingerId] = screenPosition;
        // The first finger on a seat drives its stick; another one takes over only when it lifts.
        if (player < SeatLayout.MaxPlayers && _stickFinger[player] == NoFinger) _stickFinger[player] = fingerId;
        Press(player);
    }

    void MovePointer(int fingerId, Vector2 screenPosition)
    {
        if (_fingerToPlayer.ContainsKey(fingerId)) _fingerAt[fingerId] = screenPosition;
    }

    void EndPointer(int fingerId)
    {
        if (!_fingerToPlayer.TryGetValue(fingerId, out int player)) return;

        _fingerToPlayer.Remove(fingerId);
        _fingerStart.Remove(fingerId);
        _fingerAt.Remove(fingerId);
        if (player < SeatLayout.MaxPlayers && _stickFinger[player] == fingerId)
        {
            _stickFinger[player] = NoFinger;
            foreach (var other in _fingerToPlayer)
                if (other.Value == player) _stickFinger[player] = other.Key;
        }
        Release(player);
    }

    int PlayerAt(Vector2 screenPosition)
    {
        var point = new Vector2(
            Mathf.Clamp(screenPosition.x / Screen.width, 0f, 0.9999f),
            Mathf.Clamp(screenPosition.y / Screen.height, 0f, 0.9999f));

        foreach (var zone in IgnoredZones)
            if (zone.Contains(point)) return -1;

        foreach (var extra in _extraZones)
            if (extra.Value.Contains(point)) return extra.Key;

        foreach (var seat in _seats)
            if (seat.Zone.Contains(point)) return seat.PlayerId;

        return -1;
    }

    void Press(int player)
    {
        if (_sourceCount[player]++ == 0) PlayerInput?.Invoke(player, true);
    }

    void Release(int player)
    {
        if (_sourceCount[player] == 0) return;
        if (--_sourceCount[player] == 0) PlayerInput?.Invoke(player, false);
    }

    void ReleaseAll()
    {
        _fingerToPlayer.Clear();
        _fingerStart.Clear();
        _fingerAt.Clear();
        for (int i = 0; i < _stickFinger.Length; i++) _stickFinger[i] = NoFinger;
        for (int i = 0; i < MaxIds; i++)
        {
            _keyHeld[i] = false;
            if (_sourceCount[i] == 0) continue;
            _sourceCount[i] = 0;
            PlayerInput?.Invoke(i, false);
        }
    }
}
