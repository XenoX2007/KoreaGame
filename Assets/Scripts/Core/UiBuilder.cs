using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Helpers for building placeholder uGUI in code, including generated circle/ring sprites.
public static class UiBuilder
{
    static Font _font;
    static Sprite _circle;
    static Sprite _ring;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _font = null;
        _circle = null;
        _ring = null;
    }

    public static Font Font => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    public static Sprite Circle => _circle != null ? _circle : _circle = CreateDisc("Circle", 0f);
    public static Sprite Ring => _ring != null ? _ring : _ring = CreateDisc("Ring", 0.8f);

    // A rect that fills its parent.
    public static RectTransform Rect(Transform parent, string name)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    // A fixed-size rect centered on a normalized anchor of its parent, offset in parent units.
    public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 size, Vector2 offset = default)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
        return rect;
    }

    public static Image Image(RectTransform rect, Color color, Sprite sprite = null)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static Text Text(RectTransform rect, string value, int size, Color color)
    {
        var text = rect.gameObject.AddComponent<Text>();
        text.font = Font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    // Anti-aliased white disc; innerRadius > 0 cuts a hole to make a ring.
    static Sprite CreateDisc(string name, float innerRadius)
    {
        const int size = 128;
        const float half = size / 2f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
            float alpha = Mathf.Clamp01((1f - r) * half);
            if (innerRadius > 0f) alpha *= Mathf.Clamp01((r - innerRadius) * half);
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
