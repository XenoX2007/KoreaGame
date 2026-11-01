using UnityEngine;
using UnityEngine.UIElements;

// A player's drawn character: the left team is Vietnamese, the right team Korean, like the chants.
// The art is a side-on courtyard, so characters stand on the ground band below the horizon;
// the root's transform origin is the feet.
public class CharacterView
{
    const float Width = 170f;   // matches .character in Madang.uss
    const float Height = 210f;

    public const float FarGround = 0.66f;  // fractions of the stage height
    public const float MidGround = 0.78f;
    public const float NearGround = 0.92f;

    const float ArmHanging = 12f;   // degrees from straight down
    const float ArmRaised = 170f;
    const float CupAtChest = 90f;   // top of the cup, panel units below the top of the character
    const float CupOverHead = -180f;

    readonly VisualElement _sprite;
    VisualElement _leftArm;
    VisualElement _rightArm;

    public VisualElement Root { get; }
    public VisualElement Cup { get; private set; }

    public CharacterView(int playerId)
    {
        Root = new VisualElement { pickingMode = PickingMode.Ignore };
        Root.AddToClassList("character");
        Root.AddToClassList($"player--p{playerId + 1}");
        Root.AddToClassList(NationClass(playerId));

        _sprite = new VisualElement { pickingMode = PickingMode.Ignore };
        _sprite.AddToClassList("character-sprite");
        Root.Add(_sprite);

        var tag = new Label($"P{playerId + 1}") { pickingMode = PickingMode.Ignore };
        tag.AddToClassList("character-tag");
        Root.Add(tag);

        Face(new Vector2(0f, 1f));
    }

    public static string NationClass(int playerId) =>
        SeatLayout.TeamOf(playerId) == 0 ? "character--vn" : "character--kr";

    // Seats on the far long side stand near the horizon, the near side close to the camera.
    public static float GroundOf(Seat seat) =>
        seat.Anchor.y > 0.5f ? FarGround : seat.Anchor.y < 0.5f ? NearGround : MidGround;

    // Characters nearer the camera are drawn bigger.
    public static float DepthScale(float ground) =>
        Mathf.Lerp(0.85f, 1.05f, Mathf.InverseLerp(FarGround, NearGround, ground));

    // Picks the view that matches a direction on screen (y down). The side art looks right.
    public void Face(Vector2 direction)
    {
        bool vertical = Mathf.Abs(direction.y) > Mathf.Abs(direction.x) * 0.3f;
        _sprite.EnableInClassList("view--front", vertical && direction.y > 0f);
        _sprite.EnableInClassList("view--back", vertical && direction.y < 0f);
        _sprite.EnableInClassList("view--side", !vertical);
        _sprite.style.scale = new Scale(new Vector2(!vertical && direction.x < 0f ? -1f : 1f, 1f));
    }

    public void Stand(Vector2 feet, float scale)
    {
        Root.style.translate = new Translate(feet.x - Width / 2f, feet.y - Height);
        Root.style.scale = new Scale(new Vector2(scale, scale));
    }

    // Leans the whole body, pivoting on the feet.
    public void Tilt(float degrees) => Root.style.rotate = new Rotate(new Angle(degrees));

    // Swaps the hanging arm of the side view for the separate arm piece, reaching forward.
    public void AddArm()
    {
        _sprite.AddToClassList("is-pulling");
        var arm = new VisualElement { pickingMode = PickingMode.Ignore };
        arm.AddToClassList("character-arm");
        _sprite.Add(arm);
    }

    // Readies the winner: the arms painted on the front view become separate pieces that can lift.
    public void HoldCup()
    {
        Root.AddToClassList("is-champion");
        _sprite.AddToClassList("is-cheering");

        Cup = NewPiece("character-cup");
        Root.Insert(Root.childCount - 1, Cup); // in front of the body, behind the player tag
        _leftArm = BehindBody("character-cheer-arm", "character-cheer-arm--left");
        _rightArm = BehindBody("character-cheer-arm", "character-cheer-arm--right");
        Raise(0f);
    }

    // 0 = arms down and the cup hidden at the chest, 1 = holding it over the head.
    public void Raise(float amount)
    {
        float arm = Mathf.Lerp(ArmHanging, ArmRaised, amount);
        _leftArm.style.rotate = new Rotate(new Angle(arm));
        _rightArm.style.rotate = new Rotate(new Angle(-arm));

        float size = Mathf.Lerp(0.5f, 1f, amount);
        Cup.style.translate = new Translate(0f, Mathf.Lerp(CupAtChest, CupOverHead, amount));
        Cup.style.scale = new Scale(new Vector2(size, size));
        Cup.style.opacity = Mathf.Clamp01(amount * 4f);
    }

    // Jumps on the spot, for a character placed by layout instead of Stand.
    public void Lift(float height) => Root.style.translate = new Translate(0f, -height);

    // Raised arms stay behind the head, so they never cover the face.
    VisualElement BehindBody(params string[] classes)
    {
        var piece = NewPiece(classes);
        Root.Insert(0, piece);
        return piece;
    }

    static VisualElement NewPiece(params string[] classes)
    {
        var piece = new VisualElement { pickingMode = PickingMode.Ignore };
        foreach (var name in classes) piece.AddToClassList(name);
        return piece;
    }
}
