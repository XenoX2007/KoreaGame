using UnityEngine;
using UnityEngine.UI;

// Builds a screen-space canvas with a safe-area root, and places per-player UI at each seat,
// rotated so it faces that player.
[RequireComponent(typeof(Canvas))]
public class SeatCanvas : MonoBehaviour
{
    public RectTransform SafeArea { get; private set; }

    Rect _appliedSafeArea;
    Vector2Int _appliedScreenSize;

    public static SeatCanvas Create(string name)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return go.AddComponent<SeatCanvas>();
    }

    void Awake()
    {
        SafeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
        SafeArea.SetParent(transform, false);
        ApplySafeArea();
    }

    void Update()
    {
        if (Screen.safeArea != _appliedSafeArea || Screen.width != _appliedScreenSize.x || Screen.height != _appliedScreenSize.y)
            ApplySafeArea();
    }

    // A rect at the seat's anchor inside the safe area, rotated toward the player.
    public RectTransform CreateSeatRoot(Seat seat, Vector2 size)
    {
        var rect = new GameObject($"Seat P{seat.PlayerId + 1}", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(SafeArea, false);
        rect.anchorMin = rect.anchorMax = seat.Anchor;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.Euler(0f, 0f, seat.Rotation);
        return rect;
    }

    // A rect covering a normalized full-screen region (e.g. a seat's touch zone), ignoring the safe area.
    public RectTransform CreateScreenRegion(string name, Rect normalized)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = normalized.min;
        rect.anchorMax = normalized.max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    void ApplySafeArea()
    {
        _appliedSafeArea = Screen.safeArea;
        _appliedScreenSize = new Vector2Int(Screen.width, Screen.height);

        var size = new Vector2(Screen.width, Screen.height);
        SafeArea.anchorMin = _appliedSafeArea.min / size;
        SafeArea.anchorMax = _appliedSafeArea.max / size;
        SafeArea.offsetMin = SafeArea.offsetMax = Vector2.zero;
    }
}
