using HNAUnlocker.Core;

internal static class CameraBodyVerification
{
    public static void Run(Action<bool, string> check)
    {
        byte[] expectedPrologue =
        {
            0x56, 0x57, 0x53, 0x48, 0x83, 0xEC, 0x60,
            0x0F, 0x29, 0x74, 0x24, 0x50,
        };
        check(CameraFadeResolver.HelperPrologue.Length == 12
            && CameraFadeResolver.HelperPrologue.AsSpan().SequenceEqual(expectedPrologue),
            "HNA camera fade resolver defines the exact 12-byte CachedRendererDitherController::SetDither prologue");
        check(AntiFadePatcher.GatewayUnwind.Length == 16
            && AntiFadePatcher.GatewayUnwind[0] == 0x01
            && AntiFadePatcher.GatewayUnwind[1] == 0x0C
            && AntiFadePatcher.GatewayUnwind[2] == 0x06,
            "HNA anti-fade gateway unwind record is version 1, prolog size 0x0C, with 6 unwind code slots");
    }
}
