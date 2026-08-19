using System.Globalization;
using System.Text;
using AttackShield.Core.Entities;
using Microsoft.AspNetCore.WebUtilities;
using MongoDB.Bson;

namespace AttackShield.Api.Controllers;

/// <summary>
/// Opaque paging cursor for the notification feed, encoding the (createdAt, id) sort key of the
/// last row a client has seen. Kept opaque so the sort key can change without breaking clients.
/// </summary>
internal static class NotificationCursor
{
    public static string Encode(Notification notification)
        => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes($"{notification.CreatedAt.Ticks}|{notification.Id}"));

    public static bool TryDecode(string cursor, out DateTime? before, out string? beforeId)
    {
        before = null;
        beforeId = null;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        var separator = decoded.IndexOf('|');
        if (separator <= 0 || separator == decoded.Length - 1)
            return false;

        if (!long.TryParse(decoded.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks)
            return false;

        var id = decoded[(separator + 1)..];

        // Notification.Id maps to an ObjectId, so a non-ObjectId string would throw while the
        // driver builds the range filter rather than just yielding an empty page.
        if (!ObjectId.TryParse(id, out _))
            return false;

        before = new DateTime(ticks, DateTimeKind.Utc);
        beforeId = id;
        return true;
    }
}
