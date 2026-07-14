using ImeTool.Caret;

namespace ImeTool.Tests.Caret;

public sealed class CaretTargetClassifierTests
{
    [Theory]
    [InlineData("ControlType.Button")]
    [InlineData("ControlType.List")]
    [InlineData("ControlType.ListItem")]
    [InlineData("ControlType.MenuItem")]
    [InlineData("ControlType.Window")]
    [InlineData("ControlType.Pane")]
    public void Non_Text_Controls_Are_Not_Text_Input_Targets(string controlType)
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            controlType,
            supportsTextPattern: false,
            supportsValuePattern: false,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: true,
            localizedControlType: null);

        Assert.False(result);
    }

    [Fact]
    public void Edit_Control_With_ValuePattern_Is_Text_Input_Target()
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            "ControlType.Edit",
            supportsTextPattern: false,
            supportsValuePattern: true,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: false,
            localizedControlType: "edit");

        Assert.True(result);
    }

    [Fact]
    public void Focusable_Edit_With_Unknown_ReadOnly_State_Is_Text_Input_Target()
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            "ControlType.Edit",
            supportsTextPattern: false,
            supportsValuePattern: false,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: null,
            localizedControlType: "edit");

        Assert.True(result);
    }

    [Fact]
    public void Editable_Document_With_TextPattern_Is_Text_Input_Target()
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            "ControlType.Document",
            supportsTextPattern: true,
            supportsValuePattern: false,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: false,
            localizedControlType: "document");

        Assert.True(result);
    }

    [Fact]
    public void Browser_Document_Is_Not_Input_Even_When_Provider_Reports_Editable()
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            "ControlType.Document",
            supportsTextPattern: true,
            supportsValuePattern: false,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: false,
            localizedControlType: "document",
            frameworkId: "Chrome");

        Assert.False(result);
    }

    [Fact]
    public void Editable_Browser_Group_With_TextPattern_Is_Text_Input_Target()
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            "ControlType.Group",
            supportsTextPattern: true,
            supportsValuePattern: false,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: false,
            localizedControlType: "group",
            frameworkId: "Chrome");

        Assert.True(result);
    }

    [Fact]
    public void Ordinary_Browser_Group_Without_TextPattern_Is_Not_Text_Input_Target()
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            "ControlType.Group",
            supportsTextPattern: false,
            supportsValuePattern: false,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: null,
            localizedControlType: "group",
            frameworkId: "Chrome");

        Assert.False(result);
    }

    [Fact]
    public void ReadOnly_Web_Document_With_TextPattern_Is_Not_Text_Input_Target()
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            "ControlType.Document",
            supportsTextPattern: true,
            supportsValuePattern: false,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: true,
            localizedControlType: "document");

        Assert.False(result);
    }

    [Fact]
    public void Web_Document_With_Unknown_ReadOnly_State_Is_Not_Text_Input_Target()
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            "ControlType.Document",
            supportsTextPattern: true,
            supportsValuePattern: false,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: null,
            localizedControlType: "document");

        Assert.False(result);
    }

    [Fact]
    public void Focusable_Custom_Editing_Control_Is_Text_Input_Target()
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            "ControlType.Custom",
            supportsTextPattern: false,
            supportsValuePattern: false,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: null,
            localizedControlType: "文本编辑");

        Assert.True(result);
    }

    [Fact]
    public void Password_Control_Is_Text_Input_Target()
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            "ControlType.Edit",
            supportsTextPattern: false,
            supportsValuePattern: false,
            isPassword: true,
            isKeyboardFocusable: true,
            isReadOnly: true,
            localizedControlType: "edit");

        Assert.True(result);
    }

    [Fact]
    public void Non_Focusable_Text_Control_Is_Not_Text_Input_Target()
    {
        bool result = CaretTargetClassifier.IsLikelyTextInput(
            "ControlType.Edit",
            supportsTextPattern: true,
            supportsValuePattern: true,
            isPassword: false,
            isKeyboardFocusable: false,
            isReadOnly: false,
            localizedControlType: "edit");

        Assert.False(result);
    }

    [Theory]
    [InlineData("ControlType.Button", "Chrome")]
    [InlineData("ControlType.ListItem", "Qt")]
    [InlineData("ControlType.Pane", "XAML")]
    public void Explicit_Non_Text_Focus_Rejects_Stale_Native_Caret(string controlType, string framework)
    {
        bool result = CaretTargetClassifier.ShouldTrustNativeCaret(
            controlType,
            supportsTextPattern: false,
            supportsValuePattern: false,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: null,
            localizedControlType: null,
            frameworkId: framework);

        Assert.False(result);
    }

    [Fact]
    public void Modern_Group_Without_Editing_Pattern_Rejects_Stale_Native_Caret()
    {
        bool result = CaretTargetClassifier.ShouldTrustNativeCaret(
            "ControlType.Group",
            supportsTextPattern: false,
            supportsValuePattern: true,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: false,
            localizedControlType: "组",
            frameworkId: "Qt");

        Assert.False(result);
    }

    [Fact]
    public void Legacy_Unknown_Control_Still_Trusts_Real_Native_Caret()
    {
        bool result = CaretTargetClassifier.ShouldTrustNativeCaret(
            "ControlType.Custom",
            supportsTextPattern: false,
            supportsValuePattern: false,
            isPassword: false,
            isKeyboardFocusable: true,
            isReadOnly: null,
            localizedControlType: null,
            frameworkId: "Win32");

        Assert.True(result);
    }
}
