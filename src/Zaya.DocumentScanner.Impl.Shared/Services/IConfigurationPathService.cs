namespace Zaya.DocumentScanner.Impl.Shared.Services;

/// <summary>Well-known host filesystem locations for DocumentScanner's own settings.</summary>
public interface IConfigurationPathService
{
    string GetRootAppDirectory();

    string GetProfilesDirectory();
}
