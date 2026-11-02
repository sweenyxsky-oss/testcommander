using System;

namespace TestCommander.Services;

public static class NavigationSecurityPolicy
{
    public const string SchemeBlockedMessage = "Only HTTP and HTTPS pages can be opened.";
    public const string RemoteHttpBlockedMessage = "For safety, non-local sites must use HTTPS.";
    public const string UserInfoBlockedMessage = "Remove embedded username/password details from the address.";
    public const string InvalidAddressMessage = "Enter a valid website address.";

    /// <summary>
    /// Evaluates whether an existing absolute URI is allowed for navigation or redirection.
    /// </summary>
    public static bool IsAllowed(Uri? uri, out string? rejectionReason)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            rejectionReason = InvalidAddressMessage;
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            rejectionReason = UserInfoBlockedMessage;
            return false;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = null;
            return true;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            if (uri.IsLoopback)
            {
                rejectionReason = null;
                return true;
            }

            rejectionReason = RemoteHttpBlockedMessage;
            return false;
        }

        rejectionReason = SchemeBlockedMessage;
        return false;
    }

    /// <summary>
    /// Parses and normalizes raw text input from the address bar.
    /// </summary>
    public static bool TryParseUserAddress(string? rawInput, out Uri? destinationUri, out string? validationError)
    {
        destinationUri = null;
        var value = rawInput?.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            validationError = InvalidAddressMessage;
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!Uri.TryCreate("https://" + value, UriKind.Absolute, out uri))
            {
                validationError = InvalidAddressMessage;
                return false;
            }
        }

        if (!IsAllowed(uri, out validationError))
        {
            return false;
        }

        destinationUri = uri;
        return true;
    }
}
