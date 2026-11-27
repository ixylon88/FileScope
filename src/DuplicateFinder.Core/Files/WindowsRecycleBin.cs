using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using DuplicateFinder.Core.Abstractions;

namespace DuplicateFinder.Core.Files;

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

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHCreateItemFromParsingName(string path, nint context, in Guid iid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IShellItem>))] out IShellItem item);

    [LibraryImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHGetKnownFolderItem(in Guid folder, uint flags, nint token, in Guid iid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IShellItem>))] out IShellItem item);
}
