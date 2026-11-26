using FileScope.Core.Models;

namespace FileScope.App.Presentation;

public static class AnalysisStageText
{
    public static string Format(AnalysisStage stage, RevocationMode mode, LocalizationService strings) => strings[stage switch
    {
        AnalysisStage.OpeningFile => "OpeningFile",
        AnalysisStage.Reading => "Reading",
        AnalysisStage.ReadingMetadata => "ReadingMetadata",
        AnalysisStage.ReadingPeHeader => "ReadingPeHeader",
        AnalysisStage.CheckingSignature when mode == RevocationMode.Online => "CheckingSignatureOnline",
        AnalysisStage.CheckingSignature => "CheckingSignature",
        AnalysisStage.CheckingCatalogs when mode == RevocationMode.Online => "CheckingCatalogsOnline",
        AnalysisStage.CheckingCatalogs => "CheckingCatalogs",
        _ => "Processing"
    }];
}
