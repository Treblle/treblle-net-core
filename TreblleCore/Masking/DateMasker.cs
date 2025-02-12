using System.Text.RegularExpressions;
using Treblle.Net.Core.Masking;

namespace Treblle.Runtime.Masking
{
    public sealed class DateMasker : DefaultStringMasker, IStringMasker
    {
        private const string _datePatternSlashes = @"^((0?[1-9]|1[0-2])\/(0?[1-9]|[12][0-9]|3[01])\/)(19|20)\d{2}$";
        private const string _datePatternSlashesYearFirst = @"^(19|20)\d{2}\/(0[1-9]|1[0-2])\/(0[1-9]|[12][0-9]|3[01])$";
        private const string _datePatternDashes = @"^(0?[1-9]|[12][0-9]|3[01])-(0?[1-9]|1[0-2])-(19|20)\d{2}$";
        private const string _datePatternDashesYearFirst = @"^(19|20)\d{2}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])$";
        private const string _dateMask = "$1****";

        public override bool IsPatternMatch(string input)
        {
            return Regex.IsMatch(input, _datePatternSlashes) || Regex.IsMatch(input, _datePatternSlashesYearFirst) ||
                            (Regex.IsMatch(input, _datePatternDashes) || Regex.IsMatch(input, _datePatternDashesYearFirst));
        }

        string IStringMasker.Mask(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input switch
            {
                _ when Regex.IsMatch(input, _datePatternSlashes) =>
                    Regex.Replace(input, _datePatternDashes, _dateMask),

                _ when Regex.IsMatch(input, _datePatternSlashesYearFirst) =>
                    Regex.Replace(input, _datePatternSlashesYearFirst, _dateMask),

                _ when Regex.IsMatch(input, _datePatternDashes) =>
                    Regex.Replace(input, _datePatternDashes, _dateMask),

                _ when Regex.IsMatch(input, _datePatternDashesYearFirst) =>
                    Regex.Replace(input, _datePatternDashesYearFirst, _dateMask),

                _ => base.Mask(input) // Default case
            };
        }
    }
}