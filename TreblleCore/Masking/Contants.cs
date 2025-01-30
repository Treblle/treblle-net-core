namespace Treblle.Net.Core.Masking;

public static class Constants
{
    public const string EmailPattern = @"^[a-zA-Z0-9._-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,4}$";
    public const string DatePatternSlashes = @"^((0?[1-9]|1[0-2])\/(0?[1-9]|[12][0-9]|3[01])\/)(19|20)\d{2}$";
    public const string DatePatternSlashesYearFirst = @"^(19|20)\d{2}\/(0[1-9]|1[0-2])\/(0[1-9]|[12][0-9]|3[01])$";
    public const string DatePatternDashes = @"^(0?[1-9]|[12][0-9]|3[01])-(0?[1-9]|1[0-2])-(19|20)\d{2}$";
    public const string DatePatternDashesYearFirst = @"^(19|20)\d{2}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])$";
    public const string CreditCardPattern = @"\d{4}-?\d{4}-?\d{4}-?\d{4}";
    public const string SocialSecurityPattern = @"^\d{3}-\d{2}-\d{4}$";
    public const string PostalCodePattern = @"^(GIR 0AA|[A-PR-UWYZ]([0-9]{1,2}|([A-HK-Y][0-9]([0-9ABEHMNPRV-Y])?)|[0-9][A-HJKPS-UW]) [0-9][ABD-HJLNP-UW-Z]{2})$";
}
