using System.Runtime.InteropServices;
using ImeTool.Ime;
using ImeTool.Native;

namespace ImeTool.Tests.Ime;

public sealed class InputModeToggleSenderTests
{
    [Fact]
    public void Shift_Fallback_Only_Targets_Current_Foreground_Root()
    {
        Assert.True(InputModeToggleSender.IsSameRootWindow(new IntPtr(10), new IntPtr(10)));
        Assert.False(InputModeToggleSender.IsSameRootWindow(new IntPtr(10), new IntPtr(20)));
        Assert.False(InputModeToggleSender.IsSameRootWindow(IntPtr.Zero, new IntPtr(20)));
    }

    [Fact]
    public void Native_Input_Structure_Has_Windows_Abi_Size()
    {
        int expected = IntPtr.Size == 8 ? 40 : 28;
        Assert.Equal(expected, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    [Fact]
    public void Modifier_State_Uses_High_Order_Key_Down_Bit()
    {
        Assert.True(InputModeToggleSender.IsKeyDown(unchecked((short)0x8000)));
        Assert.False(InputModeToggleSender.IsKeyDown(0x0001));
        Assert.False(InputModeToggleSender.IsKeyDown(0));
    }
}
