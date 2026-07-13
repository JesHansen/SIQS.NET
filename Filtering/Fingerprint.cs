namespace Filtering;

/// <summary>
/// A 128-bit FNV-1a fingerprint of a key string. Duplicate sets store these instead of the
/// multi-hundred-byte key strings; a false match needs a full 128-bit collision, far below the error
/// rates tolerated elsewhere.
/// </summary>
internal static class Fingerprint
{
    public static (ulong, ulong) Of(string key)
    {
        var h1 = 0xcbf29ce484222325UL;
        var h2 = 0x9E3779B97F4A7C15UL;
        foreach (var ch in key)
        {
            h1 = (h1 ^ ch) * 0x100000001B3UL;
            h2 = (h2 ^ ch) * 0xD1B54A32D192ED03UL;
        }

        return (h1, h2);
    }
}
