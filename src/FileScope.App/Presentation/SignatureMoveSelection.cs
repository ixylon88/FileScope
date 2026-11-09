using FileScope.App.Localization;
using FileScope.App.ViewModels;
using FileScope.Core.Models;

namespace FileScope.App.Presentation;

public static class SignatureMoveSelection
{
    /// <summary>Use the displayed signature, including online/offline distinctions for valid signatures.</summary>
    public static SignatureStatus? GetStatus(IReadOnlyList<FileRowViewModel> rows)
    {
        if (rows.Count == 0) return null;
        var status = rows[0].SignatureState;
        if (!Enum.IsDefined(status) || status == SignatureStatus.NotChecked) return null;
        var mode = rows[0].Result?.Signature.RevocationMode;
        return rows.All(row => row.State == RowStatus.Completed && row.Result is not null && row.SignatureState == status
            && (status != SignatureStatus.Valid || row.Result.Signature.RevocationMode == mode)) ? status : null;
    }

    public static string FolderName(SignatureStatus status, LocalizationService strings) => strings["MoveFolder_" + status];
}
