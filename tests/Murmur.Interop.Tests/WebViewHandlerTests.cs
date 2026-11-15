using Microsoft.Web.WebView2.Core;
using Murmur.WebView;
using Shouldly;

namespace Murmur.Interop.Tests;

public class WebViewHandlerTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void ShouldShowContextMenu_ForTarget_ShowsOnlyForTextFieldsAndSelections(
        bool isEditable, bool hasSelection, bool expected)
    {
        WebViewHandler.ShouldShowContextMenu(isEditable, hasSelection).ShouldBe(expected);
    }

    [Theory]
    [InlineData(CoreWebView2ProcessFailedKind.BrowserProcessExited, 0, ProcessFailureResponse.GiveUp)]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessExited, 0, ProcessFailureResponse.Reload)]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive, 0, ProcessFailureResponse.Reload)]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessExited, WebViewHandler.MaxReloads - 1, ProcessFailureResponse.Reload)]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessExited, WebViewHandler.MaxReloads, ProcessFailureResponse.GiveUp)]
    [InlineData(CoreWebView2ProcessFailedKind.FrameRenderProcessExited, 0, ProcessFailureResponse.None)]
    [InlineData(CoreWebView2ProcessFailedKind.GpuProcessExited, 0, ProcessFailureResponse.None)]
    [InlineData(CoreWebView2ProcessFailedKind.UtilityProcessExited, 0, ProcessFailureResponse.None)]
    public void RespondTo_FailedProcess_ReloadsTheRendererAFewTimesAndGivesUpOnTheBrowser(
        CoreWebView2ProcessFailedKind kind, int reloadsSoFar, ProcessFailureResponse expected)
    {
        WebViewHandler.RespondTo(kind, reloadsSoFar).ShouldBe(expected);
    }

    [Theory]
    [InlineData("https://murmur.localassets.invalid/index.html", true)]
    [InlineData("https://murmur.localassets.invalid/", true)]
    [InlineData("https://murmur.localassets.invalid:443/index.js", true)]
    [InlineData("HTTPS://MURMUR.LOCALASSETS.INVALID/index.css", true)]
    [InlineData("https://murmur.localassets.invalid/assets/x.svg?v=1#y", true)]
    [InlineData("http://murmur.localassets.invalid/index.html", false)]
    [InlineData("https://murmur.localassets.invalid:8443/index.html", false)]
    [InlineData("https://murmur.localassets.invalid.example.com/", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("http://localhost:5223/", false)]
    [InlineData("about:blank", false)]
    [InlineData("data:text/html,hi", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("not a uri", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowedOrigin_ReleaseOrigin_AllowsOnlyTheVirtualHostOverHttps(string? uri, bool expected)
    {
        WebViewHandler.IsAllowedOrigin(uri, ["https://murmur.localassets.invalid"]).ShouldBe(expected);
    }

    [Theory]
    [InlineData("http://localhost:5223/", true)]
    [InlineData("http://localhost:5223/src/main.tsx?t=1", true)]
    [InlineData("ws://localhost:5223/?token=x", false)]
    [InlineData("http://localhost/", false)]
    [InlineData("http://localhost:5224/", false)]
    [InlineData("https://localhost:5223/", false)]
    [InlineData("http://127.0.0.1:5223/", false)]
    [InlineData("https://murmur.localassets.invalid/index.html", false)]
    public void IsAllowedOrigin_DevServerOrigin_AllowsOnlyThatSchemeHostAndPort(string uri, bool expected)
    {
        WebViewHandler.IsAllowedOrigin(uri, ["http://localhost:5223"]).ShouldBe(expected);
    }

    [Fact]
    public void IsAllowedOrigin_NoAllowedOrigins_AllowsNothing()
    {
        WebViewHandler.IsAllowedOrigin("https://murmur.localassets.invalid/index.html", []).ShouldBeFalse();
    }
}
