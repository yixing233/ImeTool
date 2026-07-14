using System.Runtime.InteropServices;

namespace ImeTool.Native;

internal static class TsfInterop
{
    internal static readonly Guid KeyboardOpenCloseCompartment =
        new("58273AAD-01BB-4164-95C6-755BA0B5162D");

    [DllImport("msctf.dll")]
    internal static extern int TF_CreateThreadMgr(out ITfThreadMgr threadManager);

    [ComImport]
    [Guid("AA80E801-2021-11D2-93E0-0060B067B86E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfThreadMgr
    {
        [PreserveSig] int Activate(out uint clientId);
        [PreserveSig] int Deactivate();
        [PreserveSig] int CreateDocumentMgr(out IntPtr documentManager);
        [PreserveSig] int EnumDocumentMgrs(out IntPtr enumerator);
        [PreserveSig] int GetFocus(out IntPtr documentManager);
        [PreserveSig] int SetFocus(IntPtr documentManager);
        [PreserveSig] int AssociateFocus(IntPtr hwnd, IntPtr newDocumentManager, out IntPtr previousDocumentManager);
        [PreserveSig] int IsThreadFocus([MarshalAs(UnmanagedType.Bool)] out bool isFocus);
        [PreserveSig] int GetFunctionProvider(ref Guid classId, out IntPtr functionProvider);
        [PreserveSig] int EnumFunctionProviders(out IntPtr enumerator);
        [PreserveSig] int GetGlobalCompartment(out ITfCompartmentMgr compartmentManager);
    }

    [ComImport]
    [Guid("7DCF57AC-18AD-438B-824D-979BFFB74B7C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfCompartmentMgr
    {
        [PreserveSig] int GetCompartment(ref Guid compartmentId, out ITfCompartment compartment);
        [PreserveSig] int ClearCompartment(uint clientId, ref Guid compartmentId);
        [PreserveSig] int EnumCompartments(out IntPtr enumerator);
    }

    [ComImport]
    [Guid("BB08F7A9-607A-4384-8623-056892B64371")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfCompartment
    {
        [PreserveSig] int SetValue(uint clientId, [MarshalAs(UnmanagedType.Struct)] ref object value);
        [PreserveSig] int GetValue([MarshalAs(UnmanagedType.Struct)] out object value);
        [PreserveSig] int AdviseSink(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] object sink, out uint cookie);
        [PreserveSig] int UnadviseSink(uint cookie);
    }
}
