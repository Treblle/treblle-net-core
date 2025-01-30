using System;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using Treblle.Net.Core.Masking;

namespace Treblle.Runtime.Masking
{
    public sealed class DateMasker : DefaultStringMasker, IStringMasker
    {
        string IStringMasker.Mask(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input switch
            {
                _ when Regex.IsMatch(input, Constants.DatePatternSlashes) =>
                    Regex.Replace(input, Constants.DatePatternDashes, "$1****"),

                _ when Regex.IsMatch(input, Constants.DatePatternSlashesYearFirst) =>
                    Regex.Replace(input, Constants.DatePatternSlashesYearFirst, "$1****"),

                _ when Regex.IsMatch(input, Constants.DatePatternDashes) =>
                    Regex.Replace(input, Constants.DatePatternDashes, "$1****"),

                _ when Regex.IsMatch(input, Constants.DatePatternDashesYearFirst) =>
                    Regex.Replace(input, Constants.DatePatternDashesYearFirst, "$1****"),

                _ => base.Mask(input) // Default case
            };
        }
    }
}