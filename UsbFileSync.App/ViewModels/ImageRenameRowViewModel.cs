using UsbFileSync.Core.Models;

namespace UsbFileSync.App.ViewModels;

public sealed class ImageRenameRowViewModel(ImageRenamePlanItem planItem) : ObservableObject
{
    public ImageRenamePlanItem PlanItem { get; } = planItem;

    public string CurrentFileName => PlanItem.CurrentFileName;

    public string ProposedFileName => PlanItem.ProposedFileName;

    public string DisplayPath => PlanItem.SourceFullPath;

    public string MatchedFileNameMask => PlanItem.MatchedFileNameMask;

    public string TimestampText => PlanItem.TimestampLocal.ToString("yyyy-MM-dd HH:mm:ss");

    public string CollisionText => PlanItem.UsedCollisionSuffix ? "Sequenced" : "Ready";

    public string OpenPath => PlanItem.SourceFullPath;

    public bool HasOpenPath => !string.IsNullOrWhiteSpace(OpenPath);
}
