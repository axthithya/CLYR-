using Clyr.Contracts;

namespace Clyr.Core;

public interface IClassificationProvider
{
    IClassificationSession Start(ScanRequest request, DriveSummary drive);
}

public interface IClassificationSession
{
    void Observe(FileSystemEntry entry);
    ClassificationResult Complete(ScanCoverage coverage, long? unaccountedDriveBytes);
}
