#if NETFRAMEWORK
namespace DeployTool.Core.Polyfills;

/// <summary>Dictionary.GetValueOrDefault was added in .NET Core 2.0 — not available on .NET Framework.</summary>
public static class DictionaryExtensions
{
    public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue) where TKey : notnull =>
        dictionary.TryGetValue(key, out var value) ? value : defaultValue;
}
#endif
