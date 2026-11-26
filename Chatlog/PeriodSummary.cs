using LetheAISharp.Memory;
using Newtonsoft.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LetheAISharp.Files
{
    /// <summary>
    /// Calendar-aligned period types used by the period (tier 2) summary system.
    /// </summary>
    public enum PeriodType
    {
        /// <summary> ISO week, starting on Monday. </summary>
        Week,
        /// <summary> Calendar month. </summary>
        Month,
        /// <summary> Calendar quarter (Jan-Mar, Apr-Jun, Jul-Sep, Oct-Dec). </summary>
        Quarter
    }

    /// <summary>
    /// Meta-summary compiling the session summaries of a closed calendar period (week, month, or quarter).
    /// Stored in <see cref="Chatlog.PeriodSummaries"/>, aside from the regular session list, and used as long range history in the system prompt.
    /// </summary>
    /// <remarks>
    /// Weeks are compiled from session summaries. Months are compiled from the summaries of the weeks fully contained in the month, and from the
    /// session summaries of partial weeks at the month's edges. Quarters are compiled from month summaries. <see cref="MemoryUnit.Added"/> marks
    /// the start of the period, and <see cref="MemoryUnit.EndTime"/> the last moment of the period.
    /// </remarks>
    public class PeriodSummary : MemoryUnit
    {
        /// <summary>
        /// Type of period covered by this summary.
        /// </summary>
        public PeriodType Period { get; set; } = PeriodType.Week;

        /// <summary>
        /// Unique identifiers of the memory units (sessions or lower tier period summaries) used to compile this summary.
        /// </summary>
        public List<Guid> SourceGuids { get; set; } = [];

        /// <summary>
        /// Hash of the sources used to build this summary. Used to detect when the summary is outdated and needs to be regenerated.
        /// </summary>
        public string SourceSignature { get; set; } = string.Empty;

        /// <summary>
        /// When this summary was last generated.
        /// </summary>
        public DateTime GeneratedOn { get; set; } = DateTime.Now;

        /// <summary>
        /// Start of the period (alias of <see cref="MemoryUnit.Added"/>).
        /// </summary>
        [JsonIgnore]
        public DateTime PeriodStart => Added;

        /// <summary>
        /// Exclusive end of the period (start of the following period).
        /// </summary>
        [JsonIgnore]
        public DateTime PeriodEnd => GetPeriodEnd(Period, Added);

        public PeriodSummary()
        {
            Category = MemoryType.PeriodSummary;
            Insertion = MemoryInsertion.None;
        }

        /// <summary>
        /// Creates an empty period summary aligned on the period containing <paramref name="date"/>.
        /// </summary>
        public PeriodSummary(PeriodType period, DateTime date) : this()
        {
            Period = period;
            Added = GetPeriodStart(period, date);
            EndTime = GetPeriodEnd(period, Added).AddTicks(-1);
            Name = GetLabel(period, Added);
        }

        /// <summary>
        /// Returns the start of the calendar period containing <paramref name="date"/>.
        /// </summary>
        public static DateTime GetPeriodStart(PeriodType period, DateTime date)
        {
            return period switch
            {
                PeriodType.Week => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
                PeriodType.Month => new DateTime(date.Year, date.Month, 1),
                PeriodType.Quarter => new DateTime(date.Year, ((date.Month - 1) / 3 * 3) + 1, 1),
                _ => date.Date
            };
        }

        /// <summary>
        /// Returns the exclusive end (start of the next period) of the period starting at <paramref name="start"/>.
        /// </summary>
        public static DateTime GetPeriodEnd(PeriodType period, DateTime start)
        {
            return period switch
            {
                PeriodType.Week => start.AddDays(7),
                PeriodType.Month => start.AddMonths(1),
                PeriodType.Quarter => start.AddMonths(3),
                _ => start.AddDays(1)
            };
        }

        /// <summary>
        /// Returns the parent period type (week to month, month to quarter), or null for the top level.
        /// </summary>
        public static PeriodType? GetParentPeriod(PeriodType period)
        {
            return period switch
            {
                PeriodType.Week => PeriodType.Month,
                PeriodType.Month => PeriodType.Quarter,
                _ => null
            };
        }

        /// <summary>
        /// Returns a fixed, human readable label for the period starting at <paramref name="start"/>.
        /// </summary>
        public static string GetLabel(PeriodType period, DateTime start)
        {
            var culture = CultureInfo.GetCultureInfo("en-US");
            return period switch
            {
                PeriodType.Week => "Week of " + StringExtensions.DateToHumanString(start),
                PeriodType.Month => start.ToString("MMMM yyyy", culture),
                PeriodType.Quarter => $"Q{((start.Month - 1) / 3) + 1} {start.Year} ({start.ToString("MMMM", culture)} to {start.AddMonths(2).ToString("MMMM", culture)})",
                _ => StringExtensions.DateToHumanString(start)
            };
        }

        /// <summary>
        /// Returns a label relative to <paramref name="now"/>, like "last week" or "3 months ago".
        /// </summary>
        public string GetRelativeLabel(DateTime now)
        {
            switch (Period)
            {
                case PeriodType.Week:
                    var weeks = (int)Math.Round((GetPeriodStart(PeriodType.Week, now) - Added).TotalDays / 7);
                    return weeks switch
                    {
                        <= 1 => "last week",
                        2 => "the week before last",
                        _ => $"{weeks} weeks ago"
                    };
                case PeriodType.Month:
                    var months = (now.Year * 12 + now.Month) - (Added.Year * 12 + Added.Month);
                    return months <= 1 ? "last month" : $"{months} months ago";
                case PeriodType.Quarter:
                    var quarters = (now.Year * 4 + (now.Month - 1) / 3) - (Added.Year * 4 + (Added.Month - 1) / 3);
                    return quarters <= 1 ? "last quarter" : $"{quarters} quarters ago";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Computes a stable signature for a list of sources, used to detect changes requiring regeneration.
        /// </summary>
        public static string ComputeSignature(IEnumerable<MemoryUnit> sources, bool includeRoleplay)
        {
            var sb = new StringBuilder();
            sb.Append(includeRoleplay ? "rp1" : "rp0");
            foreach (var item in sources)
                sb.Append('|').Append(item.Guid.ToString("N")).Append(':').Append(item.Content);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }
    }
}
