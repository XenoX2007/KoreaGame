using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

// Turns touches (and keyboard/mouse in the Editor) into per-player press/release events.
// A finger belongs to the seat zone it first touched until it lifts, even if it slides away.
public class PlayerInputRouter : MonoBehaviour
{
    // Spatial keyboard mapping for testing on PC: Z = bottom-left, P = top-right, Q = top-left, M = bottom-right.
    static readonly Key[] PlayerKeys = { Key.Z, Key.P, Key.Q, Key.M };
    const int MouseFingerId = -1;

    static PlayerInputRouter _instance;
    static bool _quitting;

    // Fired when a player goes from no input to pressed (true) or back to released (false).
    public event Action<int, bool> PlayerInput;

    // Normalized screen rects where new touches are ignored (e.g. a pause button).
    public readonly List<Rect> IgnoredZones = new List<Rect>();

    public int PlayerCount { get; private set; }
    public int ActiveFingerCount => _fingerToPlayer.Count;

    readonly Dictionary<int, int> _fingerToPlayer = new Dictionary<int, int>();
    readonly int[] _sourceCount = new int[SeatLayout.MaxPlayers];
    readonly bool[] _keyHeld = new bool[SeatLayout.MaxPlayers];
    Seat[] _seats = Array.Empty<Seat>();

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
        PlayerCount = playerCount;
    }

    public bool IsPressed(int playerId) => _sourceCount[playerId] > 0;

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
    }

    void OnDisable()
    {
        ETouch.onFingerDown -= OnFingerDown;
        ETouch.onFingerUp -= OnFingerUp;
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

    void PollKeyboard()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        for (int i = 0; i < PlayerCount; i++)
        {
            bool held = keyboard[PlayerKeys[i]].isPressed;
            if (held == _keyHeld[i]) continue;
            _keyHeld[i] = held;
            if (held) Press(i);
            else Release(i);
        }
    }

    // Lets you test touch zones with the mouse in the Editor (one pointer only).
    void PollMouse()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame) BeginPointer(MouseFingerId, mouse.position.ReadValue());
        else if (mouse.leftButton.wasReleasedThisFrame) EndPointer(MouseFingerId);
    }

    void BeginPointer(int fingerId, Vector2 screenPosition)
    {
        if (_fingerToPlayer.ContainsKey(fingerId)) return;

        int player = PlayerAt(screenPosition);
        if (player < 0) return;

        _fingerToPlayer[fingerId] = player;
        Press(player);
    }

    void EndPointer(int fingerId)
    {
        if (!_fingerToPlayer.TryGetValue(fingerId, out int player)) return;

        _fingerToPlayer.Remove(fingerId);
        Release(player);
    }

    int PlayerAt(Vector2 screenPosition)
    {
        var point = new Vector2(
            Mathf.Clamp(screenPosition.x / Screen.width, 0f, 0.9999f),
            Mathf.Clamp(screenPosition.y / Screen.height, 0f, 0.9999f));

        foreach (var zone in IgnoredZones)
            if (zone.Contains(point)) return -1;

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
        for (int i = 0; i < SeatLayout.MaxPlayers; i++)
        {
            _keyHeld[i] = false;
            if (_sourceCount[i] == 0) continue;
            _sourceCount[i] = 0;
            PlayerInput?.Invoke(i, false);
        }
    }
}
