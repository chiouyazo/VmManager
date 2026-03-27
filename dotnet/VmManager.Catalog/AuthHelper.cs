using System.Net.Http.Headers;
using System.Text;
using VmManager.Contracts.Models;

namespace VmManager.Catalog;

/// <summary>Builds HTTP authentication headers from feed credentials.</summary>
public static class AuthHelper
{
    public static AuthenticationHeaderValue? BuildBasicAuth(FeedConfiguration feed)
    {
        if (string.IsNullOrWhiteSpace(feed.Username) || string.IsNullOrWhiteSpace(feed.Password))
            return null;

        string encoded = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{feed.Username}:{feed.Password}")
        );
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    public static AuthenticationHeaderValue? BuildBasicAuth(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return new AuthenticationHeaderValue("Basic", encoded);
    }
}
