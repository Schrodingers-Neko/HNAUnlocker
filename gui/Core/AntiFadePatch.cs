using System;
using System.Collections.Generic;

namespace StarRailUnlocker.Core
{
    public sealed record AntiFadeStatistics(ulong CameraCalls, ulong OtherCalls, ulong Object,
        float Input, float Output, bool Enabled);

    /// <summary>
    /// Intercepts HNA's shared CachedRendererDitherController::SetDither(this, int kind, float alpha)
    /// and forces alpha = 1.0f ONLY for camera Elevation (kind=3) and camera Distance (kind=4) calls.
    /// Ordinary transitions (kinds 1/2/5), return values, and callbacks survive untouched.
    /// Allocations that might have been published remain valid until the target exits.
    /// </summary>
    public sealed class AntiFadePatcher : IDisposable
    {
        internal const int EnabledOffset = 0, OneOffset = 4, CameraCallsOffset = 8, OtherCallsOffset = 16;
        internal const int ObjectOffset = 24, InputOffset = 32, OutputOffset = 36, GatewayPointerOffset = 48;
        private const int AllocationSize = 0x2000, CodeOffset = 0x1000, GatewayOffset = 0x1100;
        private static readonly byte[] Prologue =
        {
            0x56,                         // push rsi
            0x57,                         // push rdi
            0x53,                         // push rbx
            0x48, 0x83, 0xEC, 0x60,       // sub rsp, 0x60
            0x0F, 0x29, 0x74, 0x24, 0x50, // movaps [rsp+0x50], xmm6
        };
        // Exact version-1 unwind record for Prologue (prolog=0x0C, 6 codes: SAVE_XMM128 xmm6; ALLOC_SMALL 0x60; PUSH RBX/RDI/RSI).
        internal static readonly byte[] GatewayUnwind =
        {
            0x01, 0x0C, 0x06, 0x00,
            0x0C, 0x68, 0x05, 0x00,
            0x07, 0xB2,
            0x03, 0x30,
            0x02, 0x70,
            0x01, 0x60,
        };
        private readonly TargetProcess _proc;
        private readonly ResolvedGameBuild _build;
        private ResolvedCameraFade _resolved;
        private ExecutableEntryPatch _entryPatch;
        private RemoteFunctionTable _unwind;
        private byte[] _gateway;
        private ulong _region, _entry, _cameraBaseline, _otherBaseline;
        private bool _published, _enabled, _disposed;
        public bool IsInstalled => _enabled && _entryPatch?.IsInstalled == true;
        public string LastError { get; private set; }
        public ulong WrapperAddress => _region == 0 ? 0 : checked(_region + CodeOffset);
        public ulong GatewayAddress => _region == 0 ? 0 : checked(_region + GatewayOffset);
        public ulong EntryAddress => _entry;

        public AntiFadePatcher(TargetProcess proc, ResolvedGameBuild build)
        {
            _proc = proc ?? throw new ArgumentNullException(nameof(proc));
            _build = build ?? throw new ArgumentNullException(nameof(build));
        }

        public bool Install(Action<string> log = null)
        {
            LastError = null;
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(AntiFadePatcher));
                if (!_proc.IsAlive) throw new InvalidOperationException(L10n.T("Runtime.AntiFade.GameProcessExited"));
                if (IsInstalled)
                {
                    RequireOwnedEntry();
                    return true;
                }
                if (_entryPatch?.WasPublished == true && !Remove(log)) return false;
                _resolved ??= CameraFadeResolver.Resolve(_build.AssemblyImage, log);
                CameraFadeResolver.VerifyLive(_proc, _build, _resolved);
                _entry = checked(_build.AssemblyBase + _resolved.Helper.Rva);
                if (!_resolved.Helper.Code.AsSpan(0, Prologue.Length).SequenceEqual(Prologue))
                    throw new NotSupportedException(L10n.T("Runtime.AntiFade.UnsupportedEntry"));
                if (_region == 0) PrepareRegion();
                _unwind ??= new RemoteFunctionTable(_proc, GatewayAddress, _gateway, GatewayUnwind);
                try { _unwind.Register(); }
                finally { _published |= _unwind.MustRetainGateway; }
                _entryPatch ??= new ExecutableEntryPatch(_proc, _entry, Prologue);
                _cameraBaseline = _proc.ReadUInt64(_region + CameraCallsOffset);
                _otherBaseline = _proc.ReadUInt64(_region + OtherCallsOffset);
                bool installed;
                try { installed = _entryPatch.Install(WrapperAddress, log); }
                finally { _published |= _entryPatch.WasPublished; }
                if (!installed) throw new InvalidOperationException(_entryPatch.LastError ?? L10n.T("Runtime.AntiFade.EntryInstallFailed"));
                RequireOwnedEntry();
                bool wroteEnabled = _proc.WriteData(_region + EnabledOffset, new byte[] { 1 });
                if (wroteEnabled) _enabled = true;
                if (!wroteEnabled || _proc.Read(_region + EnabledOffset, 1) is not { } enabled || enabled[0] != 1)
                    throw new InvalidOperationException(L10n.T("Runtime.AntiFade.EnableWrapperFailed"));
                log?.Invoke(L10n.T("Runtime.AntiFade.Enabled"));
                return true;
            }
            catch (Exception ex)
            {
                string reason = ex.Message;
                bool disabled = Disable();
                if (disabled && _entryPatch?.WasPublished == true)
                {
                    bool removed = _entryPatch.Remove(log);
                    if (!removed) reason = L10n.T("Runtime.AntiFade.EntryRestorePending", reason, _entryPatch.LastError);
                }
                if (!disabled && _proc.IsAlive) reason = L10n.T("Runtime.AntiFade.DisableUnconfirmed", reason);
                LastError = reason;
                log?.Invoke(L10n.T("Runtime.AntiFade.InstallFailed", reason));
                return false;
            }
        }

        private void PrepareRegion()
        {
            _region = _proc.AllocateNear(_entry, AllocationSize);
            if (_region == 0) throw new InvalidOperationException(L10n.T("Runtime.AntiFade.AllocateFailed"));
            try
            {
                _ = ExecutableEntryPatch.BuildEntryWord(_entry, WrapperAddress);
                byte[] gateway = BuildGateway(GatewayAddress, _entry, Prologue);
                var guards = new List<(ulong Address, byte Mask)>();
                foreach (var g in _resolved.HotfixGuards)
                    guards.Add((checked(_build.AssemblyBase + g.Rva), g.Mask));
                byte[] wrapper = BuildWrapper(WrapperAddress, _region, guards);
                if (wrapper.Length > GatewayOffset - CodeOffset || gateway.Length > 0x100)
                    throw new InvalidOperationException(L10n.T("Runtime.AntiFade.WrapperPageOverflow"));
                var state = new byte[0x1000];
                BitConverter.GetBytes(1.0f).CopyTo(state, OneOffset);
                BitConverter.GetBytes(GatewayAddress).CopyTo(state, GatewayPointerOffset);
                if (!_proc.WriteData(_region, state) || !_proc.WriteData(WrapperAddress, wrapper)
                    || !_proc.WriteData(GatewayAddress, gateway))
                    throw new InvalidOperationException(L10n.T("Runtime.AntiFade.InitializeFailed"));
                CameraFadeResolver.Equal(_proc, _region, state);
                CameraFadeResolver.Equal(_proc, WrapperAddress, wrapper);
                CameraFadeResolver.Equal(_proc, GatewayAddress, gateway);
                if (!_proc.MakeExecutable(WrapperAddress, 0x1000))
                    throw new InvalidOperationException(L10n.T("Runtime.AntiFade.ProtectFailed"));
                _gateway = gateway;
            }
            catch
            {
                _proc.Free(_region); _region = 0;
                throw;
            }
        }

        private void RequireOwnedEntry()
        {
            byte[] current = _proc.Read(_entry, Prologue.Length);
            byte[] owned = ExecutableEntryPatch.BuildEntryWord(_entry, WrapperAddress);
            if (current == null || !current.AsSpan(0, owned.Length).SequenceEqual(owned)
                || !current.AsSpan(owned.Length).SequenceEqual(Prologue.AsSpan(owned.Length)))
                throw new InvalidOperationException(L10n.T("Runtime.AntiFade.EntryOwnedByOther"));
        }

        private bool Disable()
        {
            if (_region == 0 || !_proc.IsAlive) { _enabled = false; return true; }
            bool disabled = _proc.WriteData(_region + EnabledOffset, new byte[] { 0 })
                && _proc.Read(_region + EnabledOffset, 1) is { } state && state[0] == 0;
            if (disabled) _enabled = false;
            return disabled;
        }

        public bool Remove(Action<string> log = null)
        {
            LastError = null;
            try
            {
                if (!Disable()) throw new InvalidOperationException(L10n.T("Runtime.AntiFade.DisableFailed"));
                if (_region == 0 || !_proc.IsAlive) return true;
                if (_entryPatch != null)
                {
                    _published |= _entryPatch.WasPublished;
                    if (!_entryPatch.Remove(log)) throw new InvalidOperationException(_entryPatch.LastError ?? L10n.T("Runtime.AntiFade.EntryRestoreFailed"));
                }
                if (_entry != 0 && _published)
                    CameraFadeResolver.Equal(_proc, _entry, Prologue);
                log?.Invoke(L10n.T("Runtime.AntiFade.Removed"));
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                log?.Invoke(L10n.T("Runtime.AntiFade.RestoreFailed", LastError));
                return false;
            }
        }

        public AntiFadeStatistics GetStatistics()
        {
            if (_region == 0 || !_proc.IsAlive) return new(0, 0, 0, 0, 0, false);
            byte[] data = _proc.Read(_region, 56);
            if (data == null) throw new InvalidOperationException(L10n.T("Runtime.AntiFade.StatisticsReadFailed"));
            return new(unchecked(BitConverter.ToUInt64(data, CameraCallsOffset) - _cameraBaseline),
                unchecked(BitConverter.ToUInt64(data, OtherCallsOffset) - _otherBaseline),
                BitConverter.ToUInt64(data, ObjectOffset), BitConverter.ToSingle(data, InputOffset),
                BitConverter.ToSingle(data, OutputOffset), _enabled && data[EnabledOffset] != 0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (!Remove())
                throw new InvalidOperationException(LastError ?? L10n.T("Runtime.AntiFade.RestorationPending"));
            _unwind?.Dispose();
            _published |= _unwind?.MustRetainGateway == true;
            _published |= _entryPatch?.WasPublished == true;
            if (_region != 0 && !_published) { _proc.Free(_region); _region = 0; }
            _disposed = true;
        }

        /// <summary>
        /// Changes XMM2 (alpha) to 1.0f only for enabled, unhotfixed camera Elevation (edx == 3)
        /// and camera Distance (edx == 4) calls. RSP and all GPRs survive.
        /// </summary>
        public static byte[] BuildWrapper(ulong codeAddress, ulong dataAddress,
            IReadOnlyList<(ulong Address, byte Mask)> hotfixGuards = null)
        {
            var code = new List<byte>(192); var exits = new List<int>();
            void Rip(ulong address, byte[] opcode, byte[] suffix = null)
            {
                code.AddRange(opcode);
                long next = checked((long)codeAddress + code.Count + 4 + (suffix?.Length ?? 0));
                code.AddRange(BitConverter.GetBytes(checked((int)(checked((long)address) - next))));
                if (suffix != null) code.AddRange(suffix);
            }
            int Branch(byte condition)
            {
                code.Add(0x0F); code.Add(condition); int at = code.Count; code.AddRange(new byte[4]); return at;
            }
            void Patch(int at, int target)
            {
                byte[] displacement = BitConverter.GetBytes(checked(target - (at + 4)));
                for (int i = 0; i < 4; i++) code[at + i] = displacement[i];
            }
            code.AddRange(new byte[] { 0xF3, 0x0F, 0x1E, 0xFA }); // ENDBR64; leaf wrapper
            Rip(dataAddress + EnabledOffset, new byte[] { 0x80, 0x3D }, new byte[] { 0 });
            exits.Add(Branch(0x84)); // je tail
            if (hotfixGuards != null)
                foreach (var (address, mask) in hotfixGuards)
                {
                    Rip(address, new byte[] { 0xF6, 0x05 }, new byte[] { mask });
                    exits.Add(Branch(0x85)); // jne tail: live hotfix disables modification
                }
            code.AddRange(new byte[] { 0x83, 0xFA, 0x03 }); // cmp edx, 3 (Elevation / low-angle fade)
            int isElevation = Branch(0x84);                 // je camera
            code.AddRange(new byte[] { 0x83, 0xFA, 0x04 }); // cmp edx, 4 (Distance / proximity fade)
            int other = Branch(0x85);                       // jne other
            Patch(isElevation, code.Count);
            Rip(dataAddress + ObjectOffset, new byte[] { 0x48, 0x89, 0x0D });         // mov [obj], rcx
            Rip(dataAddress + InputOffset, new byte[] { 0xF3, 0x0F, 0x11, 0x15 });    // movss [in], xmm2
            Rip(dataAddress + OneOffset, new byte[] { 0xF3, 0x0F, 0x10, 0x15 });      // movss xmm2, [1.0f]
            Rip(dataAddress + OutputOffset, new byte[] { 0xF3, 0x0F, 0x11, 0x15 });   // movss [out], xmm2
            Rip(dataAddress + CameraCallsOffset, new byte[] { 0xF0, 0x48, 0xFF, 0x05 });
            code.Add(0xE9); int afterCamera = code.Count; code.AddRange(new byte[4]);
            Patch(other, code.Count);
            Rip(dataAddress + OtherCallsOffset, new byte[] { 0xF0, 0x48, 0xFF, 0x05 });
            int tail = code.Count;
            Patch(afterCamera, tail);
            foreach (int at in exits) Patch(at, tail);
            Rip(dataAddress + GatewayPointerOffset, new byte[] { 0xFF, 0x25 });
            return code.ToArray();
        }

        public static byte[] BuildGateway(ulong gatewayAddress, ulong entryAddress, byte[] originalInstructions)
        {
            if (originalInstructions == null || !originalInstructions.AsSpan().SequenceEqual(Prologue))
                throw new NotSupportedException(L10n.T("Runtime.AntiFade.UnsupportedPrologue"));
            // An outward JMP is scanned as a tail epilogue and skips this frame's unwind codes.
            // CMP RSP,RSP; JE always transfers without being recognized as an epilogue.
            var code = new byte[Prologue.Length + 9]; Prologue.CopyTo(code, 0);
            new byte[] { 0x48, 0x39, 0xE4, 0x0F, 0x84 }.CopyTo(code, Prologue.Length);
            long next = checked((long)gatewayAddress + code.Length);
            BitConverter.GetBytes(checked((int)(checked((long)entryAddress + Prologue.Length) - next))).CopyTo(code, Prologue.Length + 5);
            return code;
        }
    }
}
