namespace UsbFileSync.Core.Models;

public sealed record DuplicateAnalysisResult(
    IReadOnlyList<DuplicateFileCandidate> Candidates,
    int DuplicateGroupCount,
    int HashedFileCount);
