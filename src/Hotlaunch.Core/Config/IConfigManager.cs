namespace Hotlaunch.Core.Config;

public interface IConfigManager
{
    AppConfig Load();
    void Save(AppConfig config);
}
