using System.Globalization;

namespace BackgroundJobs.Core.Imports;

/// <summary>
/// The order file format: a header row naming the columns (any order, any case) and one order line per row.
/// <code>order_no,ordered_at,customer_email,sku,quantity,unit_price</code>
/// Dates are ISO 8601 (a date alone means midnight in Türkiye time); prices use a dot and at most two decimals.
/// </summary>
public static class OrderFile
{
    public static readonly string[] Columns = ["order_no", "ordered_at", "customer_email", "sku", "quantity", "unit_price"];

    public const int MaxQuantity = 10_000;
    public const int MaxCodeLength = 40;
    public static readonly decimal MaxUnitPrice = 1_000_000m;

    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    /// <summary>Maps each required column to its position, or explains what is wrong with the header.</summary>
    public static bool TryReadHeader(string[] header, out Dictionary<string, int> positions, out string? problem)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        positions = found;
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim().TrimStart('﻿').ToLowerInvariant();
            if (!string.IsNullOrEmpty(name) && !found.TryAdd(name, i))
            {
                problem = $"Column '{name}' appears twice.";
                return false;
            }
        }

        var missing = Columns.Where(c => !found.ContainsKey(c)).ToList();
        problem = missing.Count == 0 ? null : $"Missing column(s): {string.Join(", ", missing)}. Expected: {string.Join(",", Columns)}.";
        return problem is null;
    }

    /// <summary>Parses one data row; returns <c>null</c> and a reason when the row cannot be imported.</summary>
    public static OrderRow? Parse(string[] fields, IReadOnlyDictionary<string, int> positions, out string? reason)
    {
        string Field(string column) => positions[column] < fields.Length ? fields[positions[column]].Trim() : string.Empty;

        var orderNo = Field("order_no");
        if (orderNo.Length is 0 or > MaxCodeLength || !orderNo.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            reason = "order_no must be 1-40 letters, digits, '-' or '_'";
            return null;
        }

        if (!TryParseDate(Field("ordered_at"), out var orderedAt))
        {
            reason = "ordered_at is not an ISO 8601 date";
            return null;
        }

        var email = Field("customer_email");
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (email.Length > 254 || at <= 0 || at == email.Length - 1 || email.Contains(' ', StringComparison.Ordinal))
        {
            reason = "customer_email is not an email address";
            return null;
        }

        var sku = Field("sku").ToUpperInvariant();
        if (sku.Length is 0 or > MaxCodeLength)
        {
            reason = "sku must be 1-40 characters";
            return null;
        }

        if (!int.TryParse(Field("quantity"), NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity is < 1 or > MaxQuantity)
        {
            reason = $"quantity must be a whole number from 1 to {MaxQuantity}";
            return null;
        }

        if (!decimal.TryParse(Field("unit_price"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price)
            || price <= 0 || price > MaxUnitPrice || decimal.Round(price, 2) != price)
        {
            reason = "unit_price must be a positive amount with at most two decimals";
            return null;
        }

        reason = null;
        return new OrderRow(orderNo, orderedAt, email.ToLowerInvariant(), sku, quantity, (long)(price * 100));
    }

    public static bool IsBlank(string[] fields) => fields.All(string.IsNullOrWhiteSpace);

    private static bool TryParseDate(string value, out DateTimeOffset date)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            date = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TurkeyOffset);
            return true;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date)
            && value.Length >= 16 && value[4] == '-' && value[7] == '-')
        {
            return true;
        }

        date = default;
        return false;
    }
}
