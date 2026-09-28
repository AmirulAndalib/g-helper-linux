namespace GHelper.Linux.USB;

internal static class TufEcLighting
{
    // ASUS DMI model names may use the FA608UHI_FA608UHI form.
    internal static bool Restore(string model, Func<byte[], bool> sendFeature)
    {
        if (!model.Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Contains("FA608UHI", StringComparer.OrdinalIgnoreCase))
            return false;

        // Restore LampArrayControl autonomous mode after controller resets.
        return sendFeature([0x46, 0x01]);
    }
}
