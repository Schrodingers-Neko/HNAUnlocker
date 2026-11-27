namespace HNAUnlocker.Core
{
    public enum UpdateSourcePreference
    {
        Auto = 0,
        GitHub = 1,
        // Gitee mirror disabled: releases are published on GitHub only.  A saved value of 2
        // is no longer defined, so UnlockConfig.Normalize() resets it to Auto.
        // Gitee = 2
    }
}
