using System;
using System.Collections.Generic;
using System.Globalization;

namespace OneSchedule
{
    // 农历转换使用系统内置的权威农历日历（ChineseLunisolarCalendar），全部本地计算：
    // 公历转农历直接取系统结果；农历转公历按"该农历年每个月（序数 1..13）的起始日"推算，
    // 与系统数据完全一致，保证生日等每年重复的事项跨年换算准确、可往返验证。
    public static class Lunar
    {
        public const int MinYear = 1901;
        public const int MaxYear = 2100;

        private static ChineseLunisolarCalendar cal = new ChineseLunisolarCalendar();
        private static readonly DateTime SolarBase = new DateTime(1900, 1, 31);

        public struct LunarDate
        {
            public int Year;
            public int Month;   // 1-12（真实月份，非序数）
            public int Day;     // 1-30
            public bool IsLeap; // 是否闰月
        }

        private static string[] monthNames = { "正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊" };
        private static string[] dayNames = {
            "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
            "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
            "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
        };
        private static string[] gan = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
        private static string[] zhi = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
        private static string[] animals = { "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪" };

        private class YearInfo
        {
            public int[] OrdStart = new int[15]; // 序数 1..13 的起始日（相对 1900-01-31 的天数，-1 表示不存在）
            public int LeapOrdinal;              // 闰月所在的序数（1..12；闰腊月为 13；0 表示无闰月）
            public int NextYearStart = -1;       // 下一年正月初一的起始日（用于计算最后一个月（腊月）的长度）
        }

        private static Dictionary<int, YearInfo> cache = new Dictionary<int, YearInfo>();

        private static YearInfo GetYearInfo(int year)
        {
            if (cache.TryGetValue(year, out YearInfo? info) && info is not null) return info;
            info = BuildYearInfo(year);
            cache[year] = info;
            return info;
        }

        private static YearInfo BuildYearInfo(int year)
        {
            YearInfo info = new YearInfo();
            for (int m = 1; m <= 14; m++) info.OrdStart[m] = -1;
            DateTime start = new DateTime(year - 1, 7, 1);
            DateTime end = new DateTime(year + 1, 4, 1);
            DateTime minDate = new DateTime(1901, 2, 19);
            DateTime maxDate = new DateTime(2101, 1, 28);
            if (start < minDate) start = minDate;
            if (end > maxDate) end = maxDate;
            for (DateTime d = start; d <= end; d = d.AddDays(1))
            {
                int yy = cal.GetYear(d);
                if (yy == year)
                {
                    int mo = cal.GetMonth(d);
                    if (info.OrdStart[mo] == -1) info.OrdStart[mo] = (int)(d - SolarBase).TotalDays;
                }
                else if (yy == year + 1 && info.NextYearStart == -1)
                {
                    info.NextYearStart = (int)(d - SolarBase).TotalDays;
                }
            }
            for (int m = 1; m <= 12; m++)
                if (cal.IsLeapMonth(year, m)) { info.LeapOrdinal = m; break; }
            if (info.LeapOrdinal == 0 && info.OrdStart[13] != -1) info.LeapOrdinal = 13;
            return info;
        }

        // 真实月份 -> 序数（isLeap 表示是否为闰月；返回 -1 表示该月在该年不存在）
        private static int OrdinalOf(YearInfo info, int month, bool isLeap)
        {
            int L = info.LeapOrdinal;
            if (isLeap)
            {
                if (L != month + 1) return -1;
                return month + 1;
            }
            if (L == 0) return month;
            int R = (L == 13) ? 12 : L - 1;
            return (month <= R) ? month : month + 1;
        }

        // 公历转农历
        public static LunarDate FromSolar(DateTime solar)
        {
            int year = cal.GetYear(solar);
            int cm = cal.GetMonth(solar);
            int day = cal.GetDayOfMonth(solar);
            YearInfo info = GetYearInfo(year);
            LunarDate d = new LunarDate();
            d.Year = year;
            d.Day = day;
            int L = info.LeapOrdinal;
            if (L > 0 && cm == L) { d.IsLeap = true; d.Month = L - 1; }
            else d.Month = (L == 0) ? cm : (cm < L ? cm : cm - 1);
            return d;
        }

        // 某月的结束偏移：优先取下一个月起始日，最后一个月份（腊月/闰腊月）取下一年的正月初一
        private static int MonthEnd(YearInfo info, int ordinal)
        {
            if (ordinal + 1 <= 13 && info.OrdStart[ordinal + 1] != -1)
                return info.OrdStart[ordinal + 1];
            if (info.NextYearStart != -1)
                return info.NextYearStart;
            return info.OrdStart[ordinal] + 30; // 边界年份无法得知下一年起点时，按最长 30 天处理
        }

        // 农历转公历
        public static DateTime ToSolar(int lunarYear, int lunarMonth, int lunarDay, bool isLeap)
        {
            if (lunarYear < MinYear || lunarYear > MaxYear)
                throw new ArgumentOutOfRangeException("lunarYear", "农历年份超出范围");
            if (lunarMonth < 1 || lunarMonth > 12)
                throw new ArgumentOutOfRangeException("lunarMonth");
            YearInfo info = GetYearInfo(lunarYear);
            int ordinal = OrdinalOf(info, lunarMonth, isLeap);
            if (ordinal < 1 || info.OrdStart[ordinal] == -1)
                throw new ArgumentException("该农历月在该年不存在");
            int len = MonthEnd(info, ordinal) - info.OrdStart[ordinal];
            if (lunarDay < 1 || lunarDay > len)
                throw new ArgumentException("该农历月没有这一天");
            return SolarBase.AddDays(info.OrdStart[ordinal] + lunarDay - 1);
        }

        // 某农历年的闰月真实月份（0 = 无闰月）
        public static int LeapMonthOf(int year)
        {
            YearInfo info = GetYearInfo(year);
            if (info.LeapOrdinal == 0) return 0;
            return info.LeapOrdinal == 13 ? 12 : info.LeapOrdinal - 1;
        }

        // 某农历月（真实月份）在该农历年的天数（29 或 30）
        public static int DaysInLunarMonth(int lunarYear, int month)
        {
            YearInfo info = GetYearInfo(lunarYear);
            int ordinal = OrdinalOf(info, month, false);
            if (ordinal < 1 || info.OrdStart[ordinal] == -1 || MonthEnd(info, ordinal) <= info.OrdStart[ordinal]) return 30;
            return MonthEnd(info, ordinal) - info.OrdStart[ordinal];
        }

        // 某农历闰月（闰month）在该农历年的天数（29 或 30）；该年没有此闰月时返回 0
        public static int DaysInLeapMonth(int lunarYear, int month)
        {
            YearInfo info = GetYearInfo(lunarYear);
            int ordinal = OrdinalOf(info, month, true);
            if (ordinal < 1 || info.OrdStart[ordinal] == -1 || MonthEnd(info, ordinal) <= info.OrdStart[ordinal]) return 0;
            return MonthEnd(info, ordinal) - info.OrdStart[ordinal];
        }

        // 每个日期格显示的农历文字：初一显示"X月"，其余显示"初X/廿X"等
        public static string GetLunarText(DateTime date)
        {
            LunarDate d = FromSolar(date);
            string m = monthNames[(d.Month - 1) % 12];
            if (d.IsLeap) m = "闰" + m;
            if (d.Day == 1) return m + "月";
            return dayNames[d.Day - 1];
        }

        public static string GetMonthText(int month, bool isLeap)
        {
            string m = monthNames[(month - 1) % 12];
            return (isLeap ? "闰" : "") + m + "月";
        }

        public static string GetDayText(int day)
        {
            if (day is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(day));
            return dayNames[day - 1];
        }

        public static string GetDateText(DateTime date)
        {
            LunarDate d = FromSolar(date);
            return "农历" + GetMonthText(d.Month, d.IsLeap) + GetDayText(d.Day);
        }

        public static string GetYearGanZhi(int lunarYear)
        {
            return gan[(lunarYear - 4) % 10] + zhi[(lunarYear - 4) % 12];
        }

        public static string GetZodiac(int lunarYear)
        {
            return animals[(lunarYear - 4) % 12];
        }
    }
}