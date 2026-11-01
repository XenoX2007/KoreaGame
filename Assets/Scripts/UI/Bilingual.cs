using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// All game text alternates between Vietnamese and Korean on one shared clock, so a whole screen
// flips together. UXML and code keep the Vietnamese text; its Korean partner lives in this table.
// A few menu lines that show both names keep the Korean first and swap the other way.
// Chants stay in their own language, because they are what each player is singing along to.
public static class Bilingual
{
    public const double SwitchSeconds = 5.0;

    static readonly Dictionary<string, string> Alternates = new Dictionary<string, string>
    {
        // Menu
        { "Sân Chơi", "마당" },
        { "마당 · Madang · 같이 놀자", "Sân Chơi · Madang · Cùng chơi nào" },
        { "Bao nhiêu người chơi?", "몇 명이 놀까요?" },
        { "Người Gỗ", "무궁화 놀이" },
        { "Hai trò khác tên, một luật chơi", "이름은 달라도 규칙은 하나" },
        { "무궁화 꽃이 피었습니다", "Một hai ba người gỗ" },
        { "Kéo Co", "줄다리기" },
        { "Một di sản chung", "함께 나누는 유산" },
        { "줄다리기", "Kéo Co" },
        { "Đá Cầu", "제기차기" },
        { "Chơi cùng nhau, chuyền cầu cho nhau", "함께 제기를 주고받아요" },
        { "제기차기", "Đá Cầu" },
        { "Sắp có", "준비 중" },
        { "Giải đấu", "토너먼트" },
        { "Chơi cả 3 trò, tính điểm, lên bục vinh quang", "세 가지 놀이로 점수를 모아 시상대에 올라요" },

        // Results and podium
        { "Về menu", "메뉴로" },
        { "Chơi lại", "다시 하기" },
        { "Tiếp tục", "계속하기" },
        { "Hòa!", "무승부!" },
        { "Bục vinh quang", "시상대" },

        // Shared in-game
        { "Đổi sang 2 đấu 2", "2대2로 바꾸기" },
        { "Đổi sang 1 đấu 1", "1대1로 바꾸기" },
        { "Tạm dừng", "일시 정지" },
        { "Về trang chủ", "홈으로" },

        // Kéo Co
        { "Kéo!", "당겨!" },
        { "Đồng lòng!", "한마음!" },

        // Người Gỗ
        { "Chạy!", "달려!" },
        { "Đứng im!", "멈춰!" },
        { "Bị bắt!", "잡혔다!" },

        // Đá Cầu
        { "Đá!", "차!" },
        { "Sớm quá!", "너무 빨라!" },
        { "Trượt!", "놓쳤다!" },
        { "Hết mạng!", "탈락!" },
        { "Gió xuôi!", "순풍!" },
        { "Gió ngược!", "역풍!" },
    };

    public static bool IsKorean => IsKoreanAt(Time.unscaledTimeAsDouble);

    public static bool IsKoreanAt(double time) => (long)(time / SwitchSeconds) % 2 == 1;

    public static bool HasAlternate(string text) => Alternates.ContainsKey(text);

    // The partner of text while Korean is showing; text without one stays as it is.
    public static string Text(string text) =>
        IsKorean && Alternates.TryGetValue(text, out var alternate) ? alternate : text;

    public static string Won(string names) => IsKorean ? $"{names} 승리!" : $"{names} thắng!";

    public static string Points(int points) => IsKorean ? $"{points}점" : $"{points} điểm";

    // Keeps every label and button in a loaded panel switching.
    public static void Bind(VisualElement root) =>
        root.Query<TextElement>().ForEach(label =>
        {
            string text = label.text;
            if (!string.IsNullOrEmpty(text) && HasAlternate(text)) Track(label, () => Text(text));
        });

    public static void Track(TextElement label, Func<string> text)
    {
        label.text = text();
        label.schedule.Execute(() => label.text = text()).Every(100);
    }
}
