namespace OpenCode.Core.Utilities;

/// <summary>
/// 管理功能开关和实验性选项。
/// </summary>
public class FeatureManager
{
    private readonly Dictionary<string, bool> _features = new();

    public FeatureManager(Dictionary<string, bool>? initialFeatures = null)
    {
        if (initialFeatures != null)
        {
            foreach (var kvp in initialFeatures)
            {
                _features[kvp.Key] = kvp.Value;
            }
        }
    }

    public bool IsEnabled(string featureName, bool defaultValue = false)
    {
        return _features.TryGetValue(featureName, out var enabled) ? enabled : defaultValue;
    }

    public void SetFeature(string featureName, bool enabled)
    {
        _features[featureName] = enabled;
    }
}
