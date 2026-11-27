using System.Globalization;
using System.Windows.Data;
using FileScope.App.Controls;
using FileScope.App.ViewModels;
using FileScope.Core.Models;

namespace FileScope.App.Infrastructure;

public sealed class StatusToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        RowStatus.Completed or SignatureStatus.Valid => StatusTone.Success,
        RowStatus.Running => StatusTone.Information,
        RowStatus.Cancelled or SignatureStatus.Untrusted or SignatureStatus.RevocationUnknown
            or SignatureStatus.UnsupportedFormat or SignatureStatus.UnsupportedPlatform => StatusTone.Warning,
        RowStatus.Failed or SignatureStatus.InvalidSignature or SignatureStatus.Expired
            or SignatureStatus.Revoked or SignatureStatus.CheckFailed => StatusTone.Error,
        _ => StatusTone.Neutral // Queued, not checked, no embedded signature, or unknown.
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
