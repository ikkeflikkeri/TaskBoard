using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace TaskBoard.Api.Features.Tasks;

internal static class TaskCursor
{
    private const int MaxEncodedLength = 256;

    public static string Encode(DateTime createdAtUtc, Guid id)
    {
        var payload = FormattableString.Invariant(
            $"1|{createdAtUtc.Ticks}|{id:N}");

        return WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(payload));
    }

    public static bool TryDecode(
        string encoded,
        out DateTime createdAtUtc,
        out Guid id)
    {
        createdAtUtc = default;
        id = default;

        if (string.IsNullOrWhiteSpace(encoded) ||
            encoded.Length > MaxEncodedLength)
        {
            return false;
        }

        string payload;

        try
        {
            payload = Encoding.UTF8.GetString(
                WebEncoders.Base64UrlDecode(encoded));
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = payload.Split('|');

        if (parts.Length != 3 || parts[0] != "1")
            return false;

        if (!long.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var ticks))
        {
            return false;
        }

        if (ticks < DateTime.MinValue.Ticks ||
            ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        if (!Guid.TryParseExact(parts[2], "N", out id) ||
            id == Guid.Empty)
        {
            return false;
        }

        createdAtUtc = new DateTime(ticks, DateTimeKind.Utc);

        return true;
    }
}
