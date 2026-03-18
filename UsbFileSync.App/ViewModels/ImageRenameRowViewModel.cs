using UsbFileSync.Core.Models;

namespace UsbFileSync.App.ViewModels;

public sealed class ImageRenameRowViewModel(ImageRenamePlanItem planItem, bool isSelected = false) : ObservableObject
{
    private bool _isSelected = isSelected;

    public ImageRenamePlanItem PlanItem { get; } = planItem;

    public string CurrentFileName => PlanItem.CurrentFileName;

    public string ProposedFileName => PlanItem.ProposedFileName;

    public string DisplayPath => PlanItem.SourceFullPath;

    public string MatchedFileNameMask => string.IsNullOrWhiteSpace(PlanItem.MatchedFileNameMask)
        ? "No mask match"
        : PlanItem.MatchedFileNameMask;

    public string TimestampText => PlanItem.TimestampLocal.ToString("yyyy-MM-dd HH:mm:ss");

    public string CollisionText => PlanItem.UsedCollisionSuffix ? "Sequenced" : "Ready";

    public string OpenPath => PlanItem.SourceFullPath;

    public bool HasOpenPath => !string.IsNullOrWhiteSpace(OpenPath);

    public bool IsMatchedByFileNameMask => PlanItem.IsMatchedByFileNameMask;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
