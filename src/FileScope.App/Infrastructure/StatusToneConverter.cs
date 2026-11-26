using System.Globalization;
using System.Windows.Data;
using FileScope.App.ViewModels;
using FileScope.Core.Models;

namespace FileScope.App.Infrastructure;

public sealed class StatusToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        SignatureStatus status => SignatureGroup.ToneFor(status),
        RowStatus.Completed => StatusTone.Success,
        RowStatus.Running => StatusTone.Information,
        RowStatus.Cancelled => StatusTone.Warning,
        RowStatus.Failed => StatusTone.Error,
        _ => StatusTone.Neutral
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
