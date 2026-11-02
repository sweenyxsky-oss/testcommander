using System;
using TestCommander.Services;
using Xunit;

namespace TestCommander.Tests;

public sealed class NavigationSecurityPolicyTests
{
    [Theory]
    [InlineData("https://ts1.x1.international.travian.com")]
    [InlineData("https://travian.com/login?step=1")]
    [InlineData("https://127.0.0.1:8443")]
    [InlineData("https://sub.domain.example.org:9000/path")]
    public void IsAllowed_Permits_ValidHttpsUris(string url)
    {
        var uri = new Uri(url);
        var allowed = NavigationSecurityPolicy.IsAllowed(uri, out var reason);

        Assert.True(allowed);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://[::1]:3000")]
    public void IsAllowed_Permits_LoopbackHttp(string url)
    {
        var uri = new Uri(url);
        var allowed = NavigationSecurityPolicy.IsAllowed(uri, out var reason);

        Assert.True(allowed);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("http://ts1.travian.com")]
    [InlineData("http://192.168.1.100")]
    [InlineData("http://10.0.0.1:8080")]
    [InlineData("http://example.com/login")]
    public void IsAllowed_Blocks_RemoteHttp(string url)
    {
        var uri = new Uri(url);
        var allowed = NavigationSecurityPolicy.IsAllowed(uri, out var reason);

        Assert.False(allowed);
        Assert.Equal(NavigationSecurityPolicy.RemoteHttpBlockedMessage, reason);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("data:text/html,<h1>Test</h1>")]
    [InlineData("ftp://files.example.com")]
    [InlineData("custom-protocol://token")]
    public void IsAllowed_Blocks_UnsafeAndUnexpectedSchemes(string url)
    {
        var uri = new Uri(url, UriKind.Absolute);
        var allowed = NavigationSecurityPolicy.IsAllowed(uri, out var reason);

        Assert.False(allowed);
        Assert.Equal(NavigationSecurityPolicy.SchemeBlockedMessage, reason);
    }

    [Theory]
    [InlineData("https://user:password@travian.com")]
    [InlineData("http://admin:secret@127.0.0.1:8080")]
    public void IsAllowed_Blocks_UrisWithUserInfo(string url)
    {
        var uri = new Uri(url);
        var allowed = NavigationSecurityPolicy.IsAllowed(uri, out var reason);

        Assert.False(allowed);
        Assert.Equal(NavigationSecurityPolicy.UserInfoBlockedMessage, reason);
    }

    [Theory]
    [InlineData("ts1.travian.com", "https://ts1.travian.com/")]
    [InlineData("https://ts1.travian.com", "https://ts1.travian.com/")]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:8080/")]
    public void TryParseUserAddress_NormalizesAndValidates(string input, string expectedUri)
    {
        var success = NavigationSecurityPolicy.TryParseUserAddress(input, out var uri, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(expectedUri, uri!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("http://remote.travian.com")]
    [InlineData("javascript:void(0)")]
    public void TryParseUserAddress_Rejects_InvalidInputs(string? input)
    {
        var success = NavigationSecurityPolicy.TryParseUserAddress(input, out var uri, out var error);

        Assert.False(success);
        Assert.Null(uri);
        Assert.NotNull(error);
    }
}
