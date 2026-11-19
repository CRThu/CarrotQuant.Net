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
    /// 高性能 64 位市场时间标量值对象。
    /// <para>
    /// 内部以自 0001-01-01 起的 100ns UTC 刻度 (Ticks) 存储，结构体尺寸严格对齐 8 字节。<br/>
    /// 排除宿主机操作系统的本地时区 (<see cref="TimeZoneInfo.Local"/>) 与区域文化 (<see cref="CultureInfo.CurrentCulture"/>)。<br/>
    /// 支持 CPU 寄存器直传与单周期整数比对 (CMP)，时区仅作为外部呈现投影坐标系。
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
        /// 预置高频无时区标准格式数组（按出现频次降序排列，避免冗余遍历）。
        /// </summary>
        private static readonly string[] s_standardDateTimeFormats = new[]
        {
            // 1. 标准空格分隔格式（高频日内与日线行情）
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss.ffffff",
            "yyyy-MM-dd",
            // 2. 标准 T 分隔格式（无时区 ISO 文本）
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss.ffffff",
            // 3. 紧凑纯数字格式（无分隔符，系统兜底无法推断，必须精确匹配）
            "yyyyMMdd",
            "yyyyMMddHHmmss",
            "yyyyMMddHHmmssfff",
            "yyyyMMddHHmmssffffff"
        };

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

        private static long DateToTicks(int year, int month, int day)
        {
            if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1)
            {
                ThrowDateOutOfRange();
            }
            int[] daysToMonth = IsLeapYear(year) ? s_daysToMonth366 : s_daysToMonth365;
            if (day > daysToMonth[month] - daysToMonth[month - 1])
            {
                ThrowDateOutOfRange();
            }
            int y = year - 1;
            int n = y * 365 + y / 4 - y / 100 + y / 400 + daysToMonth[month - 1] + day - 1;
            return (long)n * TimeSpan.TicksPerDay;
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
        /// 从 <see cref="DateTime"/> 创建市场时间（直接提取其 Ticks 刻度，杜绝隐式时区猜测）。
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
        /// 单次扫描、零堆内存分配。
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

        /// <summary>
        /// 解析文本为 <see cref="MarketTime"/>。
        /// <para>
        /// 1. 纯数字根据字符长度与数值量纲分流（13位为 Unix 毫秒戳、10位为 Unix 秒戳，其余紧凑数字格式走日期解析）；<br/>
        /// 2. 文本自带时区标识（如 +08:00、+0800 或 Z）时，统一转换为 UTC 刻度；若传入了矛盾的 <paramref name="sourceTimeZone"/>，则抛出 <see cref="ArgumentException"/>；<br/>
        /// 3. 文本不含时区标识时，若指定了 <paramref name="sourceTimeZone"/> 则作为源时区转换为 UTC 刻度，未指定则直接映射为字面刻度；<br/>
        /// 4. 严格绑定 InvariantCulture，排除宿主机本地时区与文化干扰。
        /// </para>
        /// </summary>
        public static MarketTime Parse(ReadOnlySpan<char> text, TimeZoneInfo? sourceTimeZone = null)
        {
            if (TryParse(text, sourceTimeZone, out MarketTime result))
            {
                return result;
            }
            throw new FormatException($"String '{text.ToString()}' was not recognized as a valid MarketTime.");
        }

        /// <inheritdoc cref="Parse(ReadOnlySpan{char}, TimeZoneInfo?)"/>
        public static MarketTime Parse(string text, TimeZoneInfo? sourceTimeZone = null)
        {
            ArgumentNullException.ThrowIfNull(text);
            return Parse(text.AsSpan(), sourceTimeZone);
        }

        /// <summary>
        /// 尝试解析文本为 <see cref="MarketTime"/>。
        /// </summary>
        public static bool TryParse(ReadOnlySpan<char> text, TimeZoneInfo? sourceTimeZone, out MarketTime result)
        {
            ReadOnlySpan<char> s = text.Trim();
            if (s.IsEmpty)
            {
                result = default;
                return false;
            }

            // 1. 纯数字时间戳分流：严格基于字符长度与数值量纲分流，防御误判 14 位 yyyyMMddHHmmss 或 8 位 yyyyMMdd
            if (long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out long num))
            {
                // 13 位数字：Unix 毫秒戳 (覆盖 1970 - 2065 年: 10^12 ~ 3*10^12)
                if (s.Length == 13 && num >= 1_000_000_000_000L && num <= 3_000_000_000_000L)
                {
                    result = FromUnixTimeMilliseconds(num);
                    return true;
                }
                // 10 位数字：Unix 秒戳 (覆盖 1970 - 2065 年: 10^8 ~ 3*10^9)
                if (s.Length == 10 && num >= 100_000_000L && num <= 3_000_000_000L)
                {
                    result = FromUnixTimeSeconds(num);
                    return true;
                }
                // 其余长度（如 14 位 yyyyMMddHHmmss、8 位 yyyyMMdd）不得视为时间戳，掉落后续日期解析
            }

            // 2. 包含显式时区偏移（Z, +08:00, +0800, -05:00）的 ISO 文本
            // 通过 DateTimeOffset 提取，脱敏宿主机系统时区
            if (HasTimezone(s))
            {
                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset dto))
                {
                    if (sourceTimeZone != null)
                    {
                        // 若显式传入了具体的业务时区（非 UTC），严格校验是否与文本自带时区偏移冲突
                        if (sourceTimeZone != TimeZoneInfo.Utc)
                        {
                            TimeSpan expectedOffset = sourceTimeZone.GetUtcOffset(dto.DateTime);
                            if (dto.Offset != expectedOffset)
                            {
                                ThrowTimezoneConflict(dto.Offset, expectedOffset, sourceTimeZone);
                            }
                        }
                        // 校验通过或显式要求转为 UTC：换算为绝对物理 UTC 刻度
                        result = new MarketTime(dto.UtcTicks);
                    }
                    else
                    {
                        // 未指定目标时区：保留钟表时间，排除宿主机本地时区干扰
                        result = new MarketTime(dto.DateTime.Ticks);
                    }
                    return true;
                }
            }

            // 3. 不含显式时区标记的文本（严格绑定 InvariantCulture，排除宿主机系统时区干扰）
            if (DateTime.TryParseExact(s, s_standardDateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt) ||
                DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                if (sourceTimeZone != null)
                {
                    DateTime unspecified = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
                    result = new MarketTime(TimeZoneInfo.ConvertTimeToUtc(unspecified, sourceTimeZone).Ticks);
                }
                else
                {
                    // 未指定时区：直接将字面时刻保真映射为基准标量
                    result = new MarketTime(dt.Ticks);
                }
                return true;
            }

            result = default;
            return false;
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
        /// 零分配获取指定时区偏移量下的本地小时数 (0-23)，安全防御负偏移。
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
        /// 零分配获取指定时区偏移量下的本地分钟数 (0-59)，安全防御负偏移。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetMinute(TimeSpan offset)
        {
            long localTicks = _ticks + offset.Ticks;
            long minutes = localTicks / TimeSpan.TicksPerMinute;
            int mod = (int)(minutes % 60);
            return mod < 0 ? mod + 60 : mod;
        }

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

        /// <summary>增加指定的持续时间。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime Add(TimeSpan value) => new(_ticks + value.Ticks);

        /// <summary>增加指定天数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddDays(double value) => new(_ticks + (long)(value * TimeSpan.TicksPerDay));

        /// <summary>增加指定小时数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddHours(double value) => new(_ticks + (long)(value * TimeSpan.TicksPerHour));

        /// <summary>增加指定分钟数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddMinutes(double value) => new(_ticks + (long)(value * TimeSpan.TicksPerMinute));

        /// <summary>增加指定秒数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddSeconds(double value) => new(_ticks + (long)(value * TimeSpan.TicksPerSecond));

        /// <summary>增加指定毫秒数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddMilliseconds(double value) => new(_ticks + (long)(value * TimeSpan.TicksPerMillisecond));

        /// <summary>增加指定 100ns 刻度数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime AddTicks(long value) => new(_ticks + value);

        /// <summary>减去指定的持续时间。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MarketTime Subtract(TimeSpan value) => new(_ticks - value.Ticks);

        /// <summary>计算两个市场时间之间的时间间隔。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TimeSpan Subtract(MarketTime value) => new(_ticks - value._ticks);

        #endregion

        #region 操作符重载与类型转换 (Zero-Cost Inlining)

        /// <summary>
        /// 显式转换为标准 <see cref="DateTime"/>（默认保留其 Unspecified 真实时间刻度）。
        /// 需显式转换或调用 <see cref="ToDateTime()"/>，杜绝双向隐式转换导致的类型推断二义性。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator DateTime(MarketTime mt) => mt.ToDateTime();

        /// <summary>
        /// 从 <see cref="DateTime"/> 隐式构造市场时间。
        /// <para>
        /// 纯粹取其 Ticks 刻度，严禁调用 <see cref="DateTime.ToUniversalTime()"/> 读取宿主机操作系统的本地时区。
        /// </para>
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
            if (obj is DateTime dt) return _ticks.CompareTo(dt.Ticks);
            throw new ArgumentException("Object must be of type MarketTime or DateTime.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(MarketTime other) => _ticks == other._ticks;

        /// <summary>
        /// 遵循类型一致性哈希相等公理：仅当对象同为 MarketTime 时按 Ticks 比较。
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
    /// 统一按 ISO 8601 / 标准时间格式以纯标量字符串读写，杜绝对象属性递归展开。
    /// </summary>
    public sealed class MarketTimeJsonConverter : JsonConverter<MarketTime>
    {
        public override MarketTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s)) return MarketTime.MinValue;
                return MarketTime.Parse(s);
            }
            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt64(out long ticks))
                {
                    return MarketTime.FromTicks(ticks);
                }
            }
            throw new JsonException($"Cannot convert token {reader.TokenType} to MarketTime.");
        }

        public override void Write(Utf8JsonWriter writer, MarketTime value, JsonSerializerOptions options)
        {
            string formatted = value.Ticks % TimeSpan.TicksPerSecond != 0
                ? value.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture)
                : value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            writer.WriteStringValue(formatted);
        }
    }
}
