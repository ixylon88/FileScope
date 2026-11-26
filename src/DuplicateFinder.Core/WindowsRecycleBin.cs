using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Runtime.InteropServices.Marshalling;

namespace DuplicateFinder.Core;

/// <summary>Recycles an ordinary filesystem item on an STA using the shell's recycle-only operation.</summary>
/// <remarks>The destination is FOLDERID_RecycleBinFolder. An unsupported provider or volume produces an
/// error. The caller never requests a general delete operation or retries with permanent removal.</remarks>
/// <seealso href="https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-itransfersource-recycleitem"/>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsRecycleBin : IRecycleBin
{
    private static readonly Guid ShellItemId = typeof(IShellItem).GUID;
    private static readonly Guid TransferId = typeof(ITransferSource).GUID;
    private static readonly Guid TransferHandler = new("D5E346A1-F753-4932-B403-4574800E2498");
    private static readonly Guid RecycleFolder = new("B7534046-3ECB-4C18-BE4E-64CD4CB7D6AC");

    public void Recycle(string path, Action validate)
    {
        IShellItem? item = null;
        IShellItem? parent = null;
        IShellItem? bin = null;
        IShellItem? recycled = null;
        ITransferSource? transfer = null;
        try
        {
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ShellItemId, out item));
            item.GetParent(out parent);
            parent.BindToHandler(0, TransferHandler, TransferId, out transfer);
            Marshal.ThrowExceptionForHR(SHGetKnownFolderItem(RecycleFolder, 0, 0, ShellItemId, out bin));

            validate();
            var status = transfer.RecycleItem(item, bin, 0, out recycled);
            Marshal.ThrowExceptionForHR(status);
            if (recycled == null) throw new IOException("The shell did not return a recycled item.");
        }
        finally
        {
            Release(recycled);
            Release(transfer);
            Release(bin);
            Release(parent);
            Release(item);
        }
    }

    private static void Release(object? value)
    {
        if (value is ComObject wrapper) wrapper.FinalRelease();
    }

    [GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16), Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    internal partial interface IShellItem
    {
        void BindToHandler(nint context, in Guid handler, in Guid iid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<ITransferSource>))] out ITransferSource result);
        void GetParent([MarshalUsing(typeof(UniqueComInterfaceMarshaller<IShellItem>))] out IShellItem parent);
    }

    // Include the preceding vtable slots in their Windows SDK order. RecycleItem
    // is distinct from RemoveItem; the latter is deliberately absent here.
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

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHCreateItemFromParsingName(string path, nint context, in Guid iid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IShellItem>))] out IShellItem item);

    [LibraryImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHGetKnownFolderItem(in Guid folder, uint flags, nint token, in Guid iid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IShellItem>))] out IShellItem item);
}
