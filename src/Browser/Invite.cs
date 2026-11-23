namespace Ultima.Browser;

public static class Invite
{
    public static string Link(string current, string room)
    {
        var uri = new UriBuilder(current) { Fragment = "" };
        var parts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => Uri.UnescapeDataString(part.Split('=', 2)[0]) != "sala");
        uri.Query = string.Join('&', parts.Append("sala=" + Uri.EscapeDataString(room)));
        return uri.Uri.AbsoluteUri;
    }
}
