namespace Threadline.Infrastructure;

internal static class StringCompatibilityExtensions
{
    public static bool EndsWith(this string value, char valueToCompare, StringComparison comparisonType) =>
        value.EndsWith(valueToCompare.ToString(), comparisonType);
}
