using System;

namespace TimeSync.Utils
{
    public static class DateUtil
    {
        public static (int Year, int Week) GetWeekNumber(DateTime date)
        {
            var week = DanishWeekNumber(date.Year, date.Month, date.Day);
            if (date.Month == 1 && week > 6)
                return (Year: date.Year - 1, Week: week);
            if (date.Month == 12 && week < 6)
                return (Year: date.Year + 1, Week: week);
            return (Year: date.Year, Week: week);
        }

        private static int DanishWeekNumber(int year, int mon, int day)
        {
            int a = (14 - mon) / 12;
            int y = year + 4800 - a;
            int m = mon + 12*a - 3;
            int JD = day + (153 * m + 2)/5 + 365*y + y/4 - y/100 + y/400 - 32045;
            int d4 = (((JD + 31741 - JD % 7) % 146097) % 36524) % 1461;
            int L = d4 / 1460;
            int d1 = ((d4 - L) % 365) + L;
            return d1 / 7 + 1;
        }
    }
}
