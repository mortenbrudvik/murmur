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
}
