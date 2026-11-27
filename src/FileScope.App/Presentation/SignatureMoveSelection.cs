using FileScope.App.Localization;
using FileScope.App.ViewModels;
using FileScope.Core.Models;

namespace FileScope.App.Presentation;

public static class SignatureMoveSelection
{
    /// <summary>Use the same group as the displayed code; unchecked or incomplete rows cannot move.</summary>
    public static SignatureStatus? GetStatus(IReadOnlyList<FileRowViewModel> rows)
    {
        if (rows.Count == 0 || rows[0].Result is not { } first) return null;
        var group = SignatureGroup.From(rows[0].SignatureState, first.Signature.RevocationMode);
        if (group is not { } selected || selected.Status == SignatureStatus.NotChecked) return null;
        return rows.All(row => row.State == RowStatus.Completed && row.Result is not null
            && SignatureGroup.From(row.SignatureState, row.Result.Signature.RevocationMode) == selected) ? selected.Status : null;
    }

    public static string FolderName(SignatureStatus status, LocalizationService strings) => strings["MoveFolder_" + status];
}
