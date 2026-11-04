using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public enum Language
{
    Vietnamese,
    Korean,
    English,
}

// All game text in the language the players picked on the menu, remembered on the device.
// UXML and code keep the Vietnamese text; its Korean and English versions live in this table.
// The menu's other-name lines (a game's name in the other country) stay as they are in English.
// Chants stay in their own language, because they are what each player is singing along to.
public static class Localization
{
    const string LanguageKey = "language";

    // Each language's name, written in that language.
    public static readonly string[] NativeNames = { "Tiếng Việt", "한국어", "English" };

    // Vietnamese -> { Korean, English }
    static readonly Dictionary<string, string[]> Translations = new Dictionary<string, string[]>
    {
        // Menu
        { "Sân Chơi", new[] { "마당", "Playground" } },
        { "마당 · Madang · 같이 놀자", new[] { "Sân Chơi · Madang · Cùng chơi nào", "Sân Chơi · 마당 · Let's play together" } },
        { "Bao nhiêu người chơi?", new[] { "몇 명이 놀까요?", "How many players?" } },
        { "Người Gỗ", new[] { "무궁화 놀이", "Statues" } },
        { "Hai trò khác tên, một luật chơi", new[] { "이름은 달라도 규칙은 하나", "Two names, one game" } },
        { "무궁화 꽃이 피었습니다", new[] { "Một hai ba người gỗ", "무궁화 꽃이 피었습니다" } },
        { "Kéo Co", new[] { "줄다리기", "Tug of War" } },
        { "Một di sản chung", new[] { "함께 나누는 유산", "A shared heritage" } },
        { "줄다리기", new[] { "Kéo Co", "줄다리기" } },
        { "Đá Cầu", new[] { "제기차기", "Shuttlecock Kick" } },
        { "Chơi cùng nhau, chuyền cầu cho nhau", new[] { "함께 제기를 주고받아요", "Play together, pass it around" } },
        { "제기차기", new[] { "Đá Cầu", "제기차기" } },
        { "Bắn Bi", new[] { "구슬치기", "Marbles" } },
        { "Búng bi ra khỏi vòng", new[] { "구슬을 튕겨 원 밖으로", "Flick marbles out of the ring" } },
        { "구슬치기", new[] { "Bắn Bi", "구슬치기" } },
        { "Tách Kẹo", new[] { "뽑기", "Candy Cut" } },
        { "Tách hình kẹo mà không làm vỡ", new[] { "깨지지 않게 모양을 떼어내요", "Cut the shape out without breaking it" } },
        { "뽑기", new[] { "Tách Kẹo", "뽑기" } },
        { "Sắp có", new[] { "준비 중", "Coming soon" } },
        { "Giải đấu", new[] { "토너먼트", "Tournament" } },
        { "Chơi cả 5 trò, tính điểm, lên bục vinh quang", new[] { "다섯 가지 놀이로 점수를 모아 시상대에 올라요", "Play all 5 games, score points, reach the podium" } },
        { "Ngôn ngữ", new[] { "언어", "Language" } },

        // Online
        { "Chơi online", new[] { "온라인 플레이", "Play online" } },
        { "Tạo phòng", new[] { "방 만들기", "Host a game" } },
        { "Vào phòng", new[] { "방 참가", "Join a game" } },
        { "Vào", new[] { "참가", "Join" } },
        { "Mã phòng", new[] { "방 코드", "Room code" } },
        { "Nhập mã phòng", new[] { "방 코드를 입력하세요", "Enter the room code" } },
        { "Chia sẻ", new[] { "공유", "Share" } },
        { "Sao chép", new[] { "복사", "Copy" } },
        { "Đã sao chép!", new[] { "복사했어요!", "Copied!" } },
        { "Bắt đầu", new[] { "시작", "Start" } },
        { "Rời phòng", new[] { "나가기", "Leave" } },
        { "Đóng", new[] { "닫기", "Close" } },
        { "Đang kết nối...", new[] { "연결 중...", "Connecting..." } },
        { "Đang chờ người chơi...", new[] { "친구를 기다리는 중...", "Waiting for players..." } },
        { "Đủ người rồi, bắt đầu thôi!", new[] { "모두 모였어요, 시작해요!", "Everyone's here, let's start!" } },
        { "Đang chờ chủ phòng bắt đầu...", new[] { "방장이 시작하길 기다리는 중...", "Waiting for the host to start..." } },
        { "Không tìm thấy phòng", new[] { "방을 찾을 수 없어요", "Room not found" } },
        { "Phòng đã đủ người", new[] { "방이 꽉 찼어요", "The room is full" } },
        { "Chủ phòng đã rời đi", new[] { "방장이 나갔어요", "The host left" } },
        { "Mất kết nối", new[] { "연결이 끊겼어요", "Connection lost" } },
        { "Không kết nối được", new[] { "연결할 수 없어요", "Couldn't connect" } },
        { "Mời bạn chơi", new[] { "같이 놀자", "Play with me" } },
        { "Vào chơi Kéo Co với mình trên Sân Chơi!", new[] { "마당에서 같이 줄다리기 하자!", "Come play Tug of War with me in Madang!" } },

        // Results and podium
        { "Về menu", new[] { "메뉴로", "Menu" } },
        { "Chơi lại", new[] { "다시 하기", "Play again" } },
        { "Tiếp tục", new[] { "계속하기", "Continue" } },
        { "Hòa!", new[] { "무승부!", "Draw!" } },
        { "Bục vinh quang", new[] { "시상대", "Podium" } },

        // Shared in-game
        { "Đổi sang 2 đấu 2", new[] { "2대2로 바꾸기", "Switch to 2 vs 2" } },
        { "Đổi sang 1 đấu 1", new[] { "1대1로 바꾸기", "Switch to 1 vs 1" } },
        { "Tạm dừng", new[] { "일시 정지", "Paused" } },
        { "Về trang chủ", new[] { "홈으로", "Home" } },

        // Kéo Co
        { "Kéo!", new[] { "당겨!", "Pull!" } },
        { "Đồng lòng!", new[] { "한마음!", "In sync!" } },

        // Người Gỗ
        { "Chạy!", new[] { "달려!", "Run!" } },
        { "Đứng im!", new[] { "멈춰!", "Freeze!" } },
        { "Bị bắt!", new[] { "잡혔다!", "Caught!" } },

        // Đá Cầu
        { "Đá!", new[] { "차!", "Kick!" } },
        { "Sớm quá!", new[] { "너무 빨라!", "Too early!" } },
        { "Trượt!", new[] { "놓쳤다!", "Missed!" } },
        { "Hết mạng!", new[] { "탈락!", "Out!" } },
        { "Gió xuôi!", new[] { "순풍!", "Tailwind!" } },
        { "Gió ngược!", new[] { "역풍!", "Headwind!" } },

        // Bắn Bi
        { "Đổ xúc xắc!", new[] { "주사위를 굴려요!", "Roll the dice!" } },
        { "Lượt của P{0}", new[] { "P{0} 차례", "P{0}'s turn" } },
        { "Thêm lượt!", new[] { "한 번 더!", "Bonus shot!" } },
        { "Hụt!", new[] { "빗나갔어요!", "Missed!" } },
        { "Lượt {0}/{1}", new[] { "{0}/{1} 턴", "Turn {0}/{1}" } },
        { "Giữ nút để kéo, thả để bắn", new[] { "버튼을 꾹 눌러 당기고, 떼서 튕겨요", "Hold the button to pull, release to flick" } },
        { "Bắn!", new[] { "튕겨요!", "Flick!" } },

        // Tách Kẹo
        { "Chuẩn bị tách kẹo!", new[] { "뽑기 준비!", "Get ready to cut!" } },
        { "Kéo cần để cắt theo đường", new[] { "스틱으로 선을 따라 잘라요", "Move the stick along the line" } },
        { "Vỡ rồi!", new[] { "깨졌어요!", "Cracked!" } },
        { "Xong!", new[] { "성공!", "Done!" } },
        { "Cái ô", new[] { "우산", "Umbrella" } },
        { "Hình vuông", new[] { "네모", "Square" } },
        { "Hình bầu dục", new[] { "타원", "Oval" } },
        { "Ngôi sao", new[] { "별", "Star" } },
    };

    static Language? _current;

    // Domain reload is disabled in this project, so statics survive between play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _current = null;

    // The saved choice, or the device's language the first time the game runs.
    public static Language Current
    {
        get
        {
            if (_current == null)
                _current = PlayerPrefs.HasKey(LanguageKey) ? (Language)PlayerPrefs.GetInt(LanguageKey) : FromSystem(Application.systemLanguage);
            return _current.Value;
        }
        set
        {
            _current = value;
            PlayerPrefs.SetInt(LanguageKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    public static Language FromSystem(SystemLanguage language) =>
        language == SystemLanguage.Vietnamese ? Language.Vietnamese
        : language == SystemLanguage.Korean ? Language.Korean
        : Language.English;

    public static bool HasTranslation(string text) => Translations.ContainsKey(text);

    // The text in the current language; text without a translation stays as it is.
    public static string Text(string text) => Text(text, Current);

    public static string Text(string text, Language language) =>
        language != Language.Vietnamese && Translations.TryGetValue(text, out var translated)
            ? translated[(int)language - 1]
            : text;

    // Picks this language's line from a { Vietnamese, Korean, English } set.
    public static string Pick(string[] lines) => lines[(int)Current];

    public static string Won(string names)
    {
        switch (Current)
        {
            case Language.Korean: return $"{names} 승리!";
            case Language.English: return names.Contains("+") ? $"{names} win!" : $"{names} wins!";
            default: return $"{names} thắng!";
        }
    }

    public static string Points(int points)
    {
        switch (Current)
        {
            case Language.Korean: return $"{points}점";
            case Language.English: return points == 1 ? "1 point" : $"{points} points";
            default: return $"{points} điểm";
        }
    }

    // Keeps every label and button in a loaded panel in the current language, even after it changes.
    public static void Bind(VisualElement root) =>
        root.Query<TextElement>().ForEach(label =>
        {
            string text = label.text;
            if (!string.IsNullOrEmpty(text) && HasTranslation(text)) Track(label, () => Text(text));
        });

    public static void Track(TextElement label, Func<string> text)
    {
        label.text = text();
        label.schedule.Execute(() => label.text = text()).Every(100);
    }
}
