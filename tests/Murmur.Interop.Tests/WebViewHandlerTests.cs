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
}
