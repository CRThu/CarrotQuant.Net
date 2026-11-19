using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarrotBacktesting.NET
{
    /// <summary>
    /// 64 位市场时间标量值对象。
    /// <para>
    /// 内部以自 0001-01-01 00:00:00 起的 100ns 刻度 (Ticks) 存储，结构体尺寸为 8 字节。<br/>
    /// 内部刻度与宿主机时区及 CultureInfo 解耦，时区仅作为外部呈现或显式转换时的投影坐标系。<br/>
    /// 文本解析严格基于 ISO 8601 与 RFC 3339 标准，无隐式兜底。
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(MarketTimeJsonConverter))]
    public readonly struct MarketTime :
        IComparable<MarketTime>,
        IEquatable<MarketTime>,
        IComparable,
        IFormattable,
        ISpanFormattable,
        ISpanParsable<MarketTime>
    {
        private const int DaysPerYear = 365;
        private const int DaysPer4Years = 1461;
        private const int DaysPer100Years = 36524;
        private const int DaysPer400Years = 146097;

        private static readonly int[] s_daysToMonth365 = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365 };
        private static readonly int[] s_daysToMonth366 = { 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366 };

        /// <summary>
        /// 自公元 0001-01-01 00:00:00 起的 100ns 刻度值 (Ticks)。
        /// </summary>
        private readonly long _ticks;

        /// <summary>
        /// 获取 100ns 刻度值。
        /// </summary>
        public long Ticks
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _ticks;
        }

        #region 日历组件与时间投影

        /// <summary>获取公历年份 (1-9999)。</summary>
        public int Year
        {
            get
            {
                GetDateParts(out int year, out _, out _);
                return year;
            }
        }

        /// <summary>获取公历月份 (1-12)。</summary>
        public int Month
        {
            get
            {
                GetDateParts(out _, out int month, out _);
                return month;
            }
        }

        /// <summary>获取公历日期 (1-31)。</summary>
        public int Day
        {
            get
            {
                GetDateParts(out _, out _, out int day);
                return day;
            }
        }

        /// <summary>获取小时数 (0-23)。</summary>
        public int Hour
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)((_ticks / TimeSpan.TicksPerHour) % 24);
        }

        /// <summary>获取分钟数 (0-59)。</summary>
        public int Minute
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)((_ticks / TimeSpan.TicksPerMinute) % 60);
        }

        /// <summary>获取秒数 (0-59)。</summary>
        public int Second
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)((_ticks / TimeSpan.TicksPerSecond) % 60);
        }

        /// <summary>获取毫秒数 (0-999)。</summary>
        public int Millisecond
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)((_ticks / TimeSpan.TicksPerMillisecond) % 1000);
        }

        /// <summary>获取微秒数 (0-999)。</summary>
        public int Microsecond
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)((_ticks / 10) % 1000);
        }

        /// <summary>获取星期几。</summary>
        public DayOfWeek DayOfWeek
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (DayOfWeek)((int)((_ticks / TimeSpan.TicksPerDay + 1) % 7));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GetDateParts(out int year, out int month, out int day)
        {
            int n = (int)(_ticks / TimeSpan.TicksPerDay);

            int y400 = n / DaysPer400Years;
            n -= y400 * DaysPer400Years;

            int y100 = n / DaysPer100Years;
            if (y100 == 4) y100 = 3;
            n -= y100 * DaysPer100Years;

            int y4 = n / DaysPer4Years;
            n -= y4 * DaysPer4Years;

            int y1 = n / DaysPerYear;
            if (y1 == 4) y1 = 3;
            n -= y1 * DaysPerYear;

            year = y400 * 400 + y100 * 100 + y4 * 4 + y1 + 1;

            bool leapYear = y1 == 3 && (y4 != 24 || y100 == 3);
            int[] days = leapYear ? s_daysToMonth366 : s_daysToMonth365;
            int m = (n >> 5) + 1;
            while (n >= days[m]) m++;
            month = m;
            day = n - days[m - 1] + 1;
        }

        /// <summary>判断指定年份是否为公历闰年。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLeapYear(int year)
        {
            if (year < 1 || year > 9999)
            {
                ThrowYearOutOfRange(year);
            }
            return (year % 4 == 0 && year % 100 != 0) || (year % 400 == 0);
        }

        /// <summary>获取一天中的时间跨度 (00:00:00 至 23:59:59.9999999)。</summary>
        public TimeSpan TimeOfDay
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                long rem = _ticks % TimeSpan.TicksPerDay;
                if (rem < 0) rem += TimeSpan.TicksPerDay;
                return new TimeSpan(rem);
            }
        }

        #endregion

        #region 常量定义

        /// <summary>表示支持的最小时间标量。</summary>
        public static readonly MarketTime MinValue = new(DateTime.MinValue.Ticks);

        /// <summary>表示支持的最大时间标量。</summary>
        public static readonly MarketTime MaxValue = new(DateTime.MaxValue.Ticks);

        /// <summary>Unix 纪元时刻 (1970-01-01 00:00:00 UTC)。</summary>
        public static readonly MarketTime UnixEpoch = new(DateTime.UnixEpoch.Ticks);

        /// <summary>获取当前系统时钟对应的 UTC 市场时间。</summary>
        public static MarketTime UtcNow
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(DateTime.UtcNow.Ticks);
        }

        #endregion

        #region 构造函数与显式工厂

        /// <summary>
        /// 使用指定的年月日、时间以及可选的时区偏移量初始化市场时间。
        /// </summary>
        /// <param name="year">公历年份 (1-9999)。</param>
        /// <param name="month">月份 (1-12)。</param>
        /// <param name="day">日期 (1-31)。</param>
        /// <param name="hour">小时数 (0-23)，默认为 0。</param>
        /// <param name="minute">分钟数 (0-59)，默认为 0。</param>
        /// <param name="second">秒数 (0-59)，默认为 0。</param>
        /// <param name="millisecond">毫秒数 (0-999)，默认为 0。</param>
        /// <param name="offsetHours">时区小时整数偏移量（默认为 0 即 UTC；例如北京时间传入 8 将自动减去 8 小时转换为 UTC 刻度）。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime(int year, int month, int day, int hour = 0, int minute = 0, int second = 0, int millisecond = 0, int offsetHours = 0)
        {
            if (offsetHours < -24 || offsetHours > 24)
            {
                ThrowOffsetOutOfRange(offsetHours);
            }
            long dateTicks = DateToTicks(year, month, day);
            long timeTicks = TimeToTicks(hour, minute, second, millisecond);
            long totalTicks = dateTicks + timeTicks;
            if (offsetHours != 0)
            {
                totalTicks -= (long)offsetHours * TimeSpan.TicksPerHour;
            }
            if (totalTicks < 0 || totalTicks > DateTime.MaxValue.Ticks)
            {
                ThrowOutOfRange(totalTicks);
            }
            _ticks = totalTicks;
        }

        /// <summary>
        /// 使用指定的年月日时间以及交易所/所属时区初始化市场时间（自动解析夏令时等动态偏移并转换为 UTC 刻度）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime(int year, int month, int day, int hour, int minute, int second, int millisecond, TimeZoneInfo timeZone)
        {
            ArgumentNullException.ThrowIfNull(timeZone);
            long dateTicks = DateToTicks(year, month, day);
            long timeTicks = TimeToTicks(hour, minute, second, millisecond);
            long wallClockTicks = dateTicks + timeTicks;
            DateTime dt = new DateTime(wallClockTicks, DateTimeKind.Unspecified);
            TimeSpan offset = timeZone.GetUtcOffset(dt);
            long utcTicks = wallClockTicks - offset.Ticks;
            if (utcTicks < 0 || utcTicks > DateTime.MaxValue.Ticks)
            {
                ThrowOutOfRange(utcTicks);
            }
            _ticks = utcTicks;
        }

        /// <summary>
        /// 使用指定的 Ticks 初始化市场时间。
        /// </summary>
        /// <param name="ticks">自 0001-01-01 起的 100ns 刻度值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime(long ticks)
        {
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                ThrowOutOfRange(ticks);
            }
            _ticks = ticks;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDigit(char c) => (uint)(c - '0') <= 9;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Parse2(ReadOnlySpan<char> s, int offset)
        {
            uint c0 = (uint)(s[offset] - '0');
            uint c1 = (uint)(s[offset + 1] - '0');
            if (c0 > 9 || c1 > 9) return -1;
            return (int)(c0 * 10 + c1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Parse4(ReadOnlySpan<char> s, int offset)
        {
            uint c0 = (uint)(s[offset] - '0');
            uint c1 = (uint)(s[offset + 1] - '0');
            uint c2 = (uint)(s[offset + 2] - '0');
            uint c3 = (uint)(s[offset + 3] - '0');
            if (c0 > 9 || c1 > 9 || c2 > 9 || c3 > 9) return -1;
            return (int)(c0 * 1000 + c1 * 100 + c2 * 10 + c3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryDateToTicks(int year, int month, int day, out long ticks)
        {
            if (year < 1 || year > 9999 || (uint)(month - 1) >= 12 || day < 1)
            {
                ticks = 0;
                return false;
            }
            int[] daysToMonth = IsLeapYear(year) ? s_daysToMonth366 : s_daysToMonth365;
            if (day > daysToMonth[month] - daysToMonth[month - 1])
            {
                ticks = 0;
                return false;
            }
            int y = year - 1;
            int n = y * 365 + y / 4 - y / 100 + y / 400 + daysToMonth[month - 1] + day - 1;
            ticks = (long)n * TimeSpan.TicksPerDay;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryTimeToTicks(int hour, int minute, int second, out long ticks)
        {
            if ((uint)hour >= 24 || (uint)minute >= 60 || (uint)second >= 60)
            {
                ticks = 0;
                return false;
            }
            ticks = hour * TimeSpan.TicksPerHour +
                    minute * TimeSpan.TicksPerMinute +
                    second * TimeSpan.TicksPerSecond;
            return true;
        }

        private static long DateToTicks(int year, int month, int day)
        {
            if (!TryDateToTicks(year, month, day, out long ticks))
            {
                ThrowDateOutOfRange();
            }
            return ticks;
        }

        private static long TimeToTicks(int hour, int minute, int second, int millisecond)
        {
            if ((uint)hour >= 24 || (uint)minute >= 60 || (uint)second >= 60 || (uint)millisecond >= 1000)
            {
                ThrowTimeOutOfRange();
            }
            return hour * TimeSpan.TicksPerHour +
                   minute * TimeSpan.TicksPerMinute +
                   second * TimeSpan.TicksPerSecond +
                   millisecond * TimeSpan.TicksPerMillisecond;
        }

        [DoesNotReturn]
        private static void ThrowDateOutOfRange()
        {
            throw new ArgumentOutOfRangeException("Year, Month, and Day parameters describe an un-representable DateTime.");
        }

        [DoesNotReturn]
        private static void ThrowTimeOutOfRange()
        {
            throw new ArgumentOutOfRangeException("Hour, Minute, Second, or Millisecond parameter is out of supported range.");
        }

        [DoesNotReturn]
        private static void ThrowOffsetOutOfRange(int offsetHours)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetHours), offsetHours, "offsetHours must be between -24 and 24.");
        }

        [DoesNotReturn]
        private static void ThrowYearOutOfRange(int year)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be between 1 and 9999.");
        }

        [DoesNotReturn]
        private static void ThrowOutOfRange(long ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "MarketTime ticks out of supported range (0001-01-01 to 9999-12-31).");
        }

        [DoesNotReturn]
        private static void ThrowTimezoneConflict(TimeSpan textOffset, TimeSpan expectedOffset, TimeZoneInfo sourceTimeZone)
        {
            throw new ArgumentException($"Timezone conflict: text specifies offset '{textOffset}', but sourceTimeZone '{sourceTimeZone.Id}' specifies offset '{expectedOffset}'.");
        }

        /// <summary>
        /// 从 <see cref="DateTime"/> 创建市场时间（提取其 Ticks 刻度）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarketTime FromDateTime(DateTime dateTime)
        {
            return new MarketTime(dateTime.Ticks);
        }

        /// <summary>
        /// 从 UTC <see cref="DateTime"/> 创建市场时间。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarketTime FromUtc(DateTime utcDateTime)
        {
            return new MarketTime(utcDateTime.Ticks);
        }

        /// <summary>
        /// 从 100ns 刻度创建市场时间。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarketTime FromTicks(long ticks) => new(ticks);

        /// <summary>
        /// 从 Unix 毫秒时间戳创建市场时间。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarketTime FromUnixTimeMilliseconds(long milliseconds)
        {
            return new MarketTime(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcTicks);
        }

        /// <summary>
        /// 从 Unix 秒时间戳创建市场时间。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarketTime FromUnixTimeSeconds(long seconds)
        {
            return new MarketTime(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcTicks);
        }

        /// <summary>
        /// 从交易所本地时间与所属时区显式映射为绝对 UTC 市场时间。
        /// </summary>
        /// <param name="exchangeTime">交易所当地时间。</param>
        /// <param name="exchangeTimeZone">交易所时区（若为 null 则按无时区字面处理，直接作为标量）。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarketTime FromExchange(DateTime exchangeTime, TimeZoneInfo? exchangeTimeZone)
        {
            if (exchangeTimeZone == null)
            {
                return new MarketTime(exchangeTime.Ticks);
            }
            DateTime unspecified = DateTime.SpecifyKind(exchangeTime, DateTimeKind.Unspecified);
            return new MarketTime(TimeZoneInfo.ConvertTimeToUtc(unspecified, exchangeTimeZone).Ticks);
        }

        /// <summary>
        /// 从交易所本地时间与固定时区偏移量显式映射为绝对 UTC 市场时间。
        /// </summary>
        /// <param name="exchangeTime">交易所当地时间。</param>
        /// <param name="offset">时区固定偏移量（例如北京时间为 +08:00）。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarketTime FromExchange(DateTime exchangeTime, TimeSpan offset)
        {
            return new MarketTime(exchangeTime.Ticks - offset.Ticks);
        }

        #endregion

        #region 解析引擎

        /// <summary>
        /// 检查时间文本是否包含显式的时区标识（如 'Z'、'+08:00'、'+0800'、'-05:00'）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasTimezone(ReadOnlySpan<char> s)
        {
            s = s.Trim();
            if (s.IsEmpty) return false;

            if (s[^1] == 'Z' || s[^1] == 'z') return true;

            int timeStartIndex = s.IndexOf('T');
            if (timeStartIndex < 0)
            {
                timeStartIndex = s.IndexOf(' ');
            }

            if (timeStartIndex >= 0 && timeStartIndex < s.Length - 1)
            {
                var timePart = s.Slice(timeStartIndex + 1);
                int signIndex = timePart.IndexOfAny('+', '-');
                if (signIndex >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc cref="HasTimezone(ReadOnlySpan{char})"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasTimezone(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return HasTimezone(text.AsSpan());
        }

        private enum ParseResultStatus
        {
            Success,
            FormatError,
            TimezoneConflict,
            OutOfRange
        }

        /// <summary>
        /// 解析符合 ISO 8601 与 RFC 3339 规范的时间文本为 <see cref="MarketTime"/>。
        /// <para>
        /// 1. 支持标准扩展格式 (yyyy-MM-dd[THH:mm:ss[.ffffff][Z|±HH:mm]]) 与紧凑基本格式 (yyyyMMdd[HHmmss[fff[fff]]])；<br/>
        /// 2. 文本自带时区标识时按时区偏移转换为物理 UTC 刻度；若传入了与之冲突的 <paramref name="sourceTimeZone"/>，抛出 <see cref="ArgumentException"/>；<br/>
        /// 3. 文本不含时区标识时，若指定了 <paramref name="sourceTimeZone"/> 则作为源时区转换为 UTC 刻度，未指定则直接映射为字面刻度；<br/>
        /// 4. 格式不匹配时抛出 <see cref="FormatException"/>，刻度超出支持范围时抛出 <see cref="ArgumentOutOfRangeException"/>，无隐式兜底。
        /// </para>
        /// </summary>
        public static MarketTime Parse(ReadOnlySpan<char> text, TimeZoneInfo? sourceTimeZone = null)
        {
            ParseResultStatus status = TryParseInternal(text, sourceTimeZone, out MarketTime result, out TimeSpan actual, out TimeSpan expected);
            switch (status)
            {
                case ParseResultStatus.Success:
                    return result;
                case ParseResultStatus.TimezoneConflict:
                    ThrowTimezoneConflict(actual, expected, sourceTimeZone!);
                    break;
                case ParseResultStatus.OutOfRange:
                    throw new ArgumentOutOfRangeException(nameof(text), "MarketTime ticks out of supported range (0001-01-01 to 9999-12-31).");
                default:
                    throw new FormatException($"String '{text.ToString()}' was not recognized as a valid MarketTime.");
            }
            return result;
        }

        /// <inheritdoc cref="Parse(ReadOnlySpan{char}, TimeZoneInfo?)"/>
        public static MarketTime Parse(string text, TimeZoneInfo? sourceTimeZone = null)
        {
            ArgumentNullException.ThrowIfNull(text);
            return Parse(text.AsSpan(), sourceTimeZone);
        }

        /// <summary>
        /// 尝试将符合 ISO 8601 与 RFC 3339 规范的时间文本解析为 <see cref="MarketTime"/>。
        /// 失败时安全返回 false，绝不向外抛出异常。
        /// </summary>
        public static bool TryParse(ReadOnlySpan<char> text, TimeZoneInfo? sourceTimeZone, out MarketTime result)
        {
            return TryParseInternal(text, sourceTimeZone, out result, out _, out _) == ParseResultStatus.Success;
        }

        private static ParseResultStatus TryParseInternal(
            ReadOnlySpan<char> text,
            TimeZoneInfo? sourceTimeZone,
            out MarketTime result,
            out TimeSpan actualOffset,
            out TimeSpan expectedOffset)
        {
            result = default;
            actualOffset = TimeSpan.Zero;
            expectedOffset = TimeSpan.Zero;

            ReadOnlySpan<char> s = text.Trim();
            if (s.IsEmpty)
            {
                return ParseResultStatus.FormatError;
            }

            // 1. 标准扩展格式：yyyy-MM-dd... 或 yyyy/MM/dd... (ISO 8601 Extended / RFC 3339)
            if (s.Length >= 10 && (s[4] == '-' || s[4] == '/'))
            {
                char sep = s[4];
                if (s[7] != sep)
                {
                    return ParseResultStatus.FormatError;
                }

                int year = Parse4(s, 0);
                int month = Parse2(s, 5);
                int day = Parse2(s, 8);
                if (year < 0 || month < 0 || day < 0 || !TryDateToTicks(year, month, day, out long dateTicks))
                {
                    return ParseResultStatus.FormatError;
                }

                if (s.Length == 10)
                {
                    if (sourceTimeZone != null)
                    {
                        try
                        {
                            DateTime unspecified = new DateTime(dateTicks, DateTimeKind.Unspecified);
                            result = new MarketTime(TimeZoneInfo.ConvertTimeToUtc(unspecified, sourceTimeZone).Ticks);
                            return ParseResultStatus.Success;
                        }
                        catch (ArgumentException)
                        {
                            return ParseResultStatus.OutOfRange;
                        }
                    }
                    result = new MarketTime(dateTicks);
                    return ParseResultStatus.Success;
                }

                char timeSep = s[10];
                if (timeSep != ' ' && timeSep != 'T' && timeSep != 't')
                {
                    return ParseResultStatus.FormatError;
                }

                if (s.Length < 19 || s[13] != ':' || s[16] != ':')
                {
                    return ParseResultStatus.FormatError;
                }

                int hour = Parse2(s, 11);
                int minute = Parse2(s, 14);
                int second = Parse2(s, 17);
                if (hour < 0 || minute < 0 || second < 0 || !TryTimeToTicks(hour, minute, second, out long timeTicks))
                {
                    return ParseResultStatus.FormatError;
                }

                int pos = 19;
                long fracTicks = 0;

                // 可选亚秒部分 (.ffffff 或 ,ffffff)
                if (pos < s.Length && (s[pos] == '.' || s[pos] == ','))
                {
                    pos++;
                    int fracStart = pos;
                    while (pos < s.Length && IsDigit(s[pos]))
                    {
                        pos++;
                    }
                    int fracLen = pos - fracStart;
                    if (fracLen == 0)
                    {
                        return ParseResultStatus.FormatError;
                    }
                    long fracVal = 0;
                    int parseLen = Math.Min(fracLen, 7);
                    for (int i = 0; i < parseLen; i++)
                    {
                        fracVal = fracVal * 10 + (s[fracStart + i] - '0');
                    }
                    for (int i = parseLen; i < 7; i++)
                    {
                        fracVal *= 10;
                    }
                    fracTicks = fracVal;
                }

                // 允许时间与时区符号之间存在空格
                while (pos < s.Length && s[pos] == ' ')
                {
                    pos++;
                }

                bool hasTz = false;
                TimeSpan tzOffset = TimeSpan.Zero;

                // 可选时区部分 (Z, +08:00, -05:00, +0800, -0500, +08)
                if (pos < s.Length)
                {
                    char tzChar = s[pos];
                    if (tzChar == 'Z' || tzChar == 'z')
                    {
                        pos++;
                        hasTz = true;
                        tzOffset = TimeSpan.Zero;
                    }
                    else if (tzChar == '+' || tzChar == '-')
                    {
                        hasTz = true;
                        bool negative = tzChar == '-';
                        pos++;
                        if (pos + 2 > s.Length)
                        {
                            return ParseResultStatus.FormatError;
                        }
                        int tzHour = Parse2(s, pos);
                        if (tzHour < 0 || tzHour > 23)
                        {
                            return ParseResultStatus.FormatError;
                        }
                        pos += 2;
                        int tzMin = 0;
                        if (pos < s.Length)
                        {
                            if (s[pos] == ':')
                            {
                                pos++;
                                if (pos + 2 > s.Length)
                                {
                                    return ParseResultStatus.FormatError; // 例如 "+08:"
                                }
                                tzMin = Parse2(s, pos);
                                if (tzMin < 0 || tzMin > 59)
                                {
                                    return ParseResultStatus.FormatError;
                                }
                                pos += 2;
                            }
                            else if (IsDigit(s[pos]))
                            {
                                if (pos + 2 > s.Length)
                                {
                                    return ParseResultStatus.FormatError; // 例如 "+080"
                                }
                                tzMin = Parse2(s, pos);
                                if (tzMin < 0 || tzMin > 59)
                                {
                                    return ParseResultStatus.FormatError;
                                }
                                pos += 2;
                            }
                            else
                            {
                                return ParseResultStatus.FormatError;
                            }
                        }
                        long offsetTicks = (tzHour * TimeSpan.TicksPerHour) + (tzMin * TimeSpan.TicksPerMinute);
                        tzOffset = new TimeSpan(negative ? -offsetTicks : offsetTicks);
                    }
                    else
                    {
                        return ParseResultStatus.FormatError;
                    }
                }

                if (pos != s.Length)
                {
                    return ParseResultStatus.FormatError;
                }

                long wallTicks = dateTicks + timeTicks + fracTicks;
                if ((ulong)wallTicks > (ulong)DateTime.MaxValue.Ticks)
                {
                    return ParseResultStatus.OutOfRange;
                }

                if (hasTz)
                {
                    if (sourceTimeZone != null)
                    {
                        if (sourceTimeZone != TimeZoneInfo.Utc)
                        {
                            DateTime wallDt = new DateTime(wallTicks, DateTimeKind.Unspecified);
                            TimeSpan expected = sourceTimeZone.GetUtcOffset(wallDt);
                            if (tzOffset != expected)
                            {
                                actualOffset = tzOffset;
                                expectedOffset = expected;
                                return ParseResultStatus.TimezoneConflict;
                            }
                        }

                        long utcTicks = wallTicks - tzOffset.Ticks;
                        if ((ulong)utcTicks > (ulong)DateTime.MaxValue.Ticks)
                        {
                            return ParseResultStatus.OutOfRange;
                        }
                        result = new MarketTime(utcTicks);
                        return ParseResultStatus.Success;
                    }
                    else
                    {
                        // 未指定源时区时：保真保留原始钟表时间（Wall-clock time），
                        // 确保日线行情与日历日期严格对齐，绝不受宿主机本地时区污染。
                        result = new MarketTime(wallTicks);
                        return ParseResultStatus.Success;
                    }
                }
                else
                {
                    if (sourceTimeZone != null)
                    {
                        DateTime wallDt = new DateTime(wallTicks, DateTimeKind.Unspecified);
                        TimeSpan expected = sourceTimeZone.GetUtcOffset(wallDt);
                        long utcTicks = wallTicks - expected.Ticks;
                        if ((ulong)utcTicks > (ulong)DateTime.MaxValue.Ticks)
                        {
                            return ParseResultStatus.OutOfRange;
                        }
                        result = new MarketTime(utcTicks);
                        return ParseResultStatus.Success;
                    }
                    result = new MarketTime(wallTicks);
                    return ParseResultStatus.Success;
                }
            }

            // 2. 紧凑基本格式 (ISO 8601 Basic: yyyyMMdd[HHmmss[fff[fff]]])
            if (s.Length is 8 or 14 or 17 or 20)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    if (!IsDigit(s[i]))
                    {
                        return ParseResultStatus.FormatError;
                    }
                }

                int year = Parse4(s, 0);
                int month = Parse2(s, 4);
                int day = Parse2(s, 6);
                if (year < 0 || month < 0 || day < 0 || !TryDateToTicks(year, month, day, out long dateTicks))
                {
                    return ParseResultStatus.FormatError;
                }

                if (s.Length == 8)
                {
                    if (sourceTimeZone != null)
                    {
                        DateTime wallDt = new DateTime(dateTicks, DateTimeKind.Unspecified);
                        TimeSpan expected = sourceTimeZone.GetUtcOffset(wallDt);
                        long utcTicks = dateTicks - expected.Ticks;
                        if ((ulong)utcTicks > (ulong)DateTime.MaxValue.Ticks)
                        {
                            return ParseResultStatus.OutOfRange;
                        }
                        result = new MarketTime(utcTicks);
                        return ParseResultStatus.Success;
                    }
                    result = new MarketTime(dateTicks);
                    return ParseResultStatus.Success;
                }

                int hour = Parse2(s, 8);
                int minute = Parse2(s, 10);
                int second = Parse2(s, 12);
                if (hour < 0 || minute < 0 || second < 0 || !TryTimeToTicks(hour, minute, second, out long timeTicks))
                {
                    return ParseResultStatus.FormatError;
                }

                long fracTicks = 0;
                if (s.Length == 17)
                {
                    int ms = (s[14] - '0') * 100 + (s[15] - '0') * 10 + (s[16] - '0');
                    fracTicks = ms * TimeSpan.TicksPerMillisecond;
                }
                else if (s.Length == 20)
                {
                    long us = (s[14] - '0') * 100000L +
                              (s[15] - '0') * 10000L +
                              (s[16] - '0') * 1000L +
                              (s[17] - '0') * 100L +
                              (s[18] - '0') * 10L +
                              (s[19] - '0');
                    fracTicks = us * 10;
                }

                long totalTicks = dateTicks + timeTicks + fracTicks;
                if ((ulong)totalTicks > (ulong)DateTime.MaxValue.Ticks)
                {
                    return ParseResultStatus.OutOfRange;
                }

                if (sourceTimeZone != null)
                {
                    DateTime wallDt = new DateTime(totalTicks, DateTimeKind.Unspecified);
                    TimeSpan expected = sourceTimeZone.GetUtcOffset(wallDt);
                    long utcTicks = totalTicks - expected.Ticks;
                    if ((ulong)utcTicks > (ulong)DateTime.MaxValue.Ticks)
                    {
                        return ParseResultStatus.OutOfRange;
                    }
                    result = new MarketTime(utcTicks);
                    return ParseResultStatus.Success;
                }
                result = new MarketTime(totalTicks);
                return ParseResultStatus.Success;
            }

            // 3. 不符合规范的格式：直接判定失败，无隐式兜底
            return ParseResultStatus.FormatError;
        }

        #endregion

        #region 视图投影与日历门面 (View Projections)

        /// <summary>
        /// 映射为 UTC <see cref="DateTime"/>（Kind 为 <see cref="DateTimeKind.Utc"/>）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DateTime ToUtcDateTime() => new(_ticks, DateTimeKind.Utc);

        /// <summary>
        /// 映射为字面 <see cref="DateTime"/>（Kind 为 <see cref="DateTimeKind.Unspecified"/>）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DateTime ToDateTime() => new(_ticks, DateTimeKind.Unspecified);

        /// <summary>
        /// 映射为指定交易所时区的当地日历时间（Kind 为 <see cref="DateTimeKind.Unspecified"/>）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DateTime ToExchangeTime(TimeZoneInfo exchangeTimeZone)
        {
            ArgumentNullException.ThrowIfNull(exchangeTimeZone);
            var utc = new DateTime(_ticks, DateTimeKind.Utc);
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, exchangeTimeZone);
            return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        }

        /// <summary>
        /// 映射为指定固定时区偏移量的当地日历时间（越界安全裁剪）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DateTime ToExchangeTime(TimeSpan offset)
        {
            long localTicks = _ticks + offset.Ticks;
            if (localTicks < DateTime.MinValue.Ticks) localTicks = DateTime.MinValue.Ticks;
            if (localTicks > DateTime.MaxValue.Ticks) localTicks = DateTime.MaxValue.Ticks;
            return new DateTime(localTicks, DateTimeKind.Unspecified);
        }

        /// <summary>
        /// 获取指定时区偏移量下的本地小时数 (0-23)。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHour(TimeSpan offset)
        {
            long localTicks = _ticks + offset.Ticks;
            long hours = localTicks / TimeSpan.TicksPerHour;
            int mod = (int)(hours % 24);
            return mod < 0 ? mod + 24 : mod;
        }

        /// <summary>
        /// 获取指定时区偏移量下的本地分钟数 (0-59)。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetMinute(TimeSpan offset)
        {
            long localTicks = _ticks + offset.Ticks;
            long minutes = localTicks / TimeSpan.TicksPerMinute;
            int mod = (int)(minutes % 60);
            return mod < 0 ? mod + 60 : mod;
        }

        /// <summary>
        /// 获取自 Unix 纪元 (1970-01-01 00:00:00 UTC) 起经过的毫秒数。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ToUnixTimeMilliseconds() => (_ticks - UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;

        /// <summary>
        /// 获取自 Unix 纪元 (1970-01-01 00:00:00 UTC) 起经过的秒数。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ToUnixTimeSeconds() => (_ticks - UnixEpoch.Ticks) / TimeSpan.TicksPerSecond;

        #endregion

        /// <summary>
        /// 获取当前市场时间的日期部分（去除时分秒，保留当日 00:00:00 刻度）。
        /// </summary>
        public MarketTime Date
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                long rem = _ticks % TimeSpan.TicksPerDay;
                if (rem < 0) rem += TimeSpan.TicksPerDay;
                return new MarketTime(_ticks - rem);
            }
        }

        #region 时间运算与偏移

        private MarketTime Add(double value, double scale)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("Value cannot be NaN or Infinity.", nameof(value));
            }
            double ticksD = value * scale;
            if (ticksD < -TimeSpan.TicksPerDay * 3650000 || ticksD > TimeSpan.TicksPerDay * 3650000)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Argument is out of the supported range.");
            }
            long ticks = (long)(ticksD >= 0 ? ticksD + 0.5 : ticksD - 0.5);
            return AddTicks(ticks);
        }

        /// <summary>增加指定的持续时间。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime Add(TimeSpan value) => AddTicks(value.Ticks);

        /// <summary>增加指定天数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddDays(double value) => Add(value, TimeSpan.TicksPerDay);

        /// <summary>增加指定小时数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddHours(double value) => Add(value, TimeSpan.TicksPerHour);

        /// <summary>增加指定分钟数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddMinutes(double value) => Add(value, TimeSpan.TicksPerMinute);

        /// <summary>增加指定秒数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddSeconds(double value) => Add(value, TimeSpan.TicksPerSecond);

        /// <summary>增加指定毫秒数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddMilliseconds(double value) => Add(value, TimeSpan.TicksPerMillisecond);

        /// <summary>增加指定 100ns 刻度数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddTicks(long value)
        {
            long total;
            try
            {
                total = checked(_ticks + value);
            }
            catch (OverflowException)
            {
                ThrowOutOfRange(value < 0 ? -1 : long.MaxValue);
                return default;
            }
            if ((ulong)total > (ulong)DateTime.MaxValue.Ticks)
            {
                ThrowOutOfRange(total);
            }
            return new MarketTime(total);
        }

        /// <summary>减去指定的持续时间。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime Subtract(TimeSpan value) => AddTicks(-value.Ticks);

        /// <summary>计算两个市场时间之间的时间间隔。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TimeSpan Subtract(MarketTime value) => new(_ticks - value._ticks);

        #endregion

        #region 操作符重载与类型转换

        /// <summary>
        /// 显式转换为标准 <see cref="DateTime"/>（保留其 Unspecified 时间刻度）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator DateTime(MarketTime mt) => mt.ToDateTime();

        /// <summary>
        /// 从 <see cref="DateTime"/> 隐式构造市场时间（提取其 Ticks 刻度）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator MarketTime(DateTime dt)
        {
            return new MarketTime(dt.Ticks);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(MarketTime left, MarketTime right) => left._ticks == right._ticks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(MarketTime left, MarketTime right) => left._ticks != right._ticks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(MarketTime left, MarketTime right) => left._ticks < right._ticks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(MarketTime left, MarketTime right) => left._ticks <= right._ticks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(MarketTime left, MarketTime right) => left._ticks > right._ticks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(MarketTime left, MarketTime right) => left._ticks >= right._ticks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarketTime operator +(MarketTime time, TimeSpan span) => new(time._ticks + span.Ticks);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarketTime operator -(MarketTime time, TimeSpan span) => new(time._ticks - span.Ticks);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TimeSpan operator -(MarketTime left, MarketTime right) => new(left._ticks - right._ticks);

        #endregion

        #region 接口实现 (IComparable, IEquatable, ISpanFormattable, ISpanParsable)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(MarketTime other) => _ticks.CompareTo(other._ticks);

        public int CompareTo(object? obj)
        {
            if (obj is null) return 1;
            if (obj is MarketTime mt) return CompareTo(mt);
            throw new ArgumentException("Object must be of type MarketTime.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(MarketTime other) => _ticks == other._ticks;

        /// <summary>
        /// 当对象同为 MarketTime 时按 Ticks 比较。
        /// </summary>
        public override bool Equals(object? obj)
        {
            return obj is MarketTime other && Equals(other);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => _ticks.GetHashCode();

        public override string ToString()
        {
            return ToDateTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public string ToString(string? format, IFormatProvider? formatProvider = null)
        {
            return ToDateTime().ToString(format, formatProvider ?? CultureInfo.InvariantCulture);
        }

        public string ToString(TimeZoneInfo exchangeTimeZone, string? format = null)
        {
            return ToExchangeTime(exchangeTimeZone).ToString(format ?? "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            return ToDateTime().TryFormat(destination, out charsWritten, format, provider ?? CultureInfo.InvariantCulture);
        }

        public static MarketTime Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        {
            return Parse(s, sourceTimeZone: null);
        }

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out MarketTime result)
        {
            return TryParse(s, sourceTimeZone: null, out result);
        }

        public static MarketTime Parse(string s, IFormatProvider? provider)
        {
            ArgumentNullException.ThrowIfNull(s);
            return Parse(s.AsSpan(), provider);
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out MarketTime result)
        {
            if (s is null)
            {
                result = default;
                return false;
            }
            return TryParse(s.AsSpan(), provider, out result);
        }

        #endregion
    }

    /// <summary>
    /// <see cref="MarketTime"/> 的 System.Text.Json 序列化转换器。
    /// 按 ISO 8601 与 RFC 3339 格式以字符串读写。
    /// </summary>
    public sealed class MarketTimeJsonConverter : JsonConverter<MarketTime>
    {
        public override MarketTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                {
                    throw new JsonException($"Cannot parse empty or whitespace string to {nameof(MarketTime)}.");
                }
                if (MarketTime.TryParse(s, sourceTimeZone: null, out var mt))
                {
                    return mt;
                }
                throw new JsonException($"String '{s}' was not recognized as a valid {nameof(MarketTime)}.");
            }
            throw new JsonException($"Cannot convert token {reader.TokenType} to {nameof(MarketTime)}.");
        }

        public override void Write(Utf8JsonWriter writer, MarketTime value, JsonSerializerOptions options)
        {
            string formatted = value.Ticks % TimeSpan.TicksPerSecond != 0
                ? value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture)
                : value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            writer.WriteStringValue(formatted);
        }
    }
}
