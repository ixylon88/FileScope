using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace DuplicateFinder.Core.Abstractions;

[SupportedOSPlatform("windows")]
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16), Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
internal partial interface IShellItem
{
    void BindToHandler(nint context, in Guid handler, in Guid iid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<ITransferSource>))] out ITransferSource result);
    void GetParent([MarshalUsing(typeof(UniqueComInterfaceMarshaller<IShellItem>))] out IShellItem parent);
}

// Include the preceding vtable slots in their Windows SDK order. RecycleItem
// is distinct from RemoveItem; the latter is deliberately absent here.
[SupportedOSPlatform("windows")]
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16), Guid("00ADB003-BDE9-45C6-8E29-D09F9353E108")]
internal partial interface ITransferSource
{
    void Advise(nint sink, out uint cookie);
    void Unadvise(uint cookie);
    void SetProperties(nint properties);
    void OpenItem(IShellItem item, uint flags, in Guid iid, out nint result);
    void MoveItem(IShellItem item, IShellItem parent, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IShellItem>))] out IShellItem result);
    [PreserveSig] int RecycleItem(IShellItem item, IShellItem bin, uint flags, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IShellItem>))] out IShellItem? result);
}
