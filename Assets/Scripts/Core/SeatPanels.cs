using UnityEngine;
using UnityEngine.UIElements;

// UI Toolkit counterparts of SeatCanvas: place rotated per-player UI and honor the safe area.
public static class SeatPanels
{
    // The element should have a fixed size and negative half-size margins, so it centers on the anchor.
    public static void Place(VisualElement element, Seat seat)
    {
        element.style.position = Position.Absolute;
        element.style.left = Length.Percent(seat.Anchor.x * 100f);
        element.style.top = Length.Percent((1f - seat.Anchor.y) * 100f);
        element.style.rotate = new Rotate(new Angle(-seat.Rotation)); // seat rotation is counter-clockwise, USS is clockwise
    }

    // Insets an absolutely positioned element to Screen.safeArea (bottom-left origin) in the panel's top-left space.
    public static void FitSafeArea(VisualElement element)
    {
        var safe = Screen.safeArea;
        element.style.left = Length.Percent(safe.xMin / Screen.width * 100f);
        element.style.right = Length.Percent((Screen.width - safe.xMax) / Screen.width * 100f);
        element.style.top = Length.Percent((Screen.height - safe.yMax) / Screen.height * 100f);
        element.style.bottom = Length.Percent(safe.yMin / Screen.height * 100f);
    }

    // Plays USS enter transitions: the class must be added after the first layout, or nothing animates.
    public static void Reveal(VisualElement element, string className = "is-in")
    {
        element.schedule.Execute(() => element.AddToClassList(className)).ExecuteLater(16);
    }
}
