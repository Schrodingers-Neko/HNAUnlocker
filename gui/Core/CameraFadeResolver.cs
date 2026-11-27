using System;
using System.Collections.Generic;
using System.IO;

namespace StarRailUnlocker.Core
{
    public sealed record CameraFadeFunction(ulong Rva, byte[] Code, ulong TableEntryRva, byte[] TableEntry,
        ulong UnwindRva, byte[] Unwind);
    public sealed record CameraFadeLiteral(ulong Rva, byte[] Bytes);
    public sealed record CameraFadeHotfixGuard(ulong Rva, byte Mask);

    /// <summary>A complete, unique native camera-fade cohort (Distance Kind 4 + Elevation Kind 3 + shared Helper).</summary>
    public sealed class ResolvedCameraFade
    {
        public CameraFadeFunction Distance { get; internal init; }
        public CameraFadeFunction DistanceUpdate { get; internal init; }
        public CameraFadeFunction Elevation { get; internal init; }
        public CameraFadeFunction Helper { get; internal init; }
        public int ControllerField { get; internal init; }
        public int ElevationKind { get; internal init; }
        public int DistanceKind { get; internal init; }
        public IReadOnlyList<CameraFadeLiteral> Literals { get; internal init; }
        public IReadOnlyList<CameraFadeHotfixGuard> HotfixGuards { get; internal init; }
        internal byte[] DosHeader { get; init; }
        internal byte[] NtHeaders { get; init; }
        internal uint NtOffset { get; init; }
        internal string ImagePath { get; init; }
        internal uint Timestamp { get; init; }
        internal uint ImageSize { get; init; }
        public IEnumerable<CameraFadeFunction> Functions
        {
            get { yield return Distance; yield return DistanceUpdate; yield return Elevation; yield return Helper; }
        }
    }

    /// <summary>
    /// Resolves HNA's 4-function camera-fade cohort from GameAssembly.dll:
    ///   - Distance (Kind 4): camera proximity character fade
    ///   - DistanceUpdate: camera world-position bounds check calling Distance
    ///   - Elevation (Kind 3): camera low-angle / pitch curve character fade
    ///   - Helper: shared CachedRendererDitherController::SetDither(kind 1..5, alpha)
    /// Verifies cross-function calls, 1.0f literals, struct offsets, hotfix guards, and x64 unwind roots.
    /// </summary>
    public static class CameraFadeResolver
    {
        private const int MaximumExceptionBytes = 32 * 1024 * 1024;
        private const int MaximumFunctionBytes = 8192;

        internal static readonly byte[] HelperPrologue =
        {
            0x56,                         // push rsi
            0x57,                         // push rdi
            0x53,                         // push rbx
            0x48, 0x83, 0xEC, 0x60,       // sub rsp, 0x60
            0x0F, 0x29, 0x74, 0x24, 0x50, // movaps [rsp+0x50], xmm6
        };
        private static readonly byte[] HelperSaveThis = { 0x48, 0x89, 0xCE }; // mov rsi, rcx

        private sealed class Pattern
        {
            private readonly byte[] _bytes;
            private readonly bool[] _fixed;
            internal readonly byte[] Anchor;
            internal readonly int AnchorOffset;
            internal int Length => _bytes.Length;
            internal Pattern(string text)
            {
                string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                _bytes = new byte[parts.Length]; _fixed = new bool[parts.Length];
                int best = 0, bestStart = 0, run = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    _fixed[i] = parts[i] != "??";
                    if (_fixed[i])
                    {
                        _bytes[i] = Convert.ToByte(parts[i], 16); run++;
                        if (run > best) { best = run; bestStart = i + 1 - run; }
                    }
                    else run = 0;
                }
                if (best == 0) throw new ArgumentException(L10n.T("Resolver.CameraFade.SignatureRequiresFixedAnchor"));
                AnchorOffset = bestStart; Anchor = _bytes.AsSpan(bestStart, best).ToArray();
            }
            internal bool Matches(ReadOnlySpan<byte> bytes)
            {
                if (bytes.Length < Length) return false;
                for (int i = 0; i < Length; i++) if (_fixed[i] && bytes[i] != _bytes[i]) return false;
                return true;
            }
            internal List<int> Find(ReadOnlySpan<byte> bytes, int scanLength = int.MaxValue)
            {
                var matches = new List<int>(); int from = 0;
                while (from <= bytes.Length - Anchor.Length)
                {
                    int hit = bytes.Slice(from).IndexOf(Anchor);
                    if (hit < 0) break;
                    hit += from; int start = hit - AnchorOffset; from = hit + 1;
                    if (start >= 0 && start < scanLength && start + Length <= bytes.Length && Matches(bytes.Slice(start)))
                        matches.Add(start);
                }
                return matches;
            }
        }

        // Semantic nuclei for HNA's Distance (Kind 4), Elevation (Kind 3), and shared SetDither Helper.
        private static readonly Pattern DistanceEntry = new(
            "0F B6 41 ?? 08 D0 3C 01 0F 85 ?? ?? ?? ?? 88 51 ?? 84 D2 0F 84 ?? ?? ?? ??");
        private static readonly Pattern DistanceCallKind4 = new(
            "49 8B 81 ?? ?? 00 00 48 85 C0 74 ?? 48 89 CE 48 89 C1 BA 04 00 00 00 0F 28 D0 E8 ?? ?? ?? ??");
        private static readonly Pattern DistanceResetKind4 = new(
            "49 8B 81 ?? ?? 00 00 48 85 C0 74 ?? F3 0F 10 15 ?? ?? ?? ?? 48 89 CE 48 89 C1 BA 04 00 00 00 E8 ?? ?? ?? ?? 48 89 F1 C7 41 ?? 00 00 80 3F");
        private static readonly Pattern DistanceUpdateNucleus = new(
            "F3 0F 10 15 ?? ?? ?? ?? 48 89 F1 31 D2 E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 48 89 F1 E8 ?? ?? ?? ??");
        private static readonly Pattern ElevationEvalCurve = new(
            "F3 0F 10 4C 24 44 FF 15 ?? ?? ?? ?? 0F 28 F0");
        private static readonly Pattern ElevationCallKind3 = new(
            "48 8B 88 ?? ?? 00 00 48 85 C9 74 ?? BA 03 00 00 00 0F 28 D6 E8 ?? ?? ?? ??");
        private static readonly Pattern ElevationResetKind3 = new(
            "48 8B 88 ?? ?? 00 00 48 85 C9 74 ?? F3 0F 10 15 ?? ?? ?? ?? BA 03 00 00 00 E8 ?? ?? ?? ??");
        private static readonly Pattern HelperRangeCheck = new(
            "FF CA 83 FA 04 0F 87 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 85 C0 0F 84");

        private sealed class FunctionTable
        {
            private readonly PeImage _image;
            private readonly byte[] _table;
            private readonly uint _rva;
            private readonly int _count;
            internal readonly byte[] Dos, Nt;
            internal readonly uint NtOffset;
            internal FunctionTable(PeImage image)
            {
                _image = image;
                using var stream = File.OpenRead(image.Path);
                Dos = new byte[64]; stream.ReadExactly(Dos);
                NtOffset = BitConverter.ToUInt32(Dos, 60);
                if (BitConverter.ToUInt16(Dos, 0) != 0x5A4D || NtOffset < 64 || NtOffset > 1024 * 1024
                    || (ulong)NtOffset + 24 > (ulong)stream.Length) throw new InvalidDataException(L10n.T("Resolver.CameraFade.InvalidDosHeader"));
                stream.Position = NtOffset; var fixedHeader = new byte[24]; stream.ReadExactly(fixedHeader);
                int sectionCount = BitConverter.ToUInt16(fixedHeader, 6), optionalSize = BitConverter.ToUInt16(fixedHeader, 20);
                if (BitConverter.ToUInt32(fixedHeader, 0) != 0x4550 || BitConverter.ToUInt16(fixedHeader, 4) != 0x8664
                    || sectionCount < 1 || sectionCount > 96 || optionalSize < 144 || optionalSize > 4096)
                    throw new InvalidDataException(L10n.T("Resolver.CameraFade.UnsupportedX64Header"));
                Nt = new byte[checked(24 + optionalSize + sectionCount * 40)];
                stream.Position = NtOffset; stream.ReadExactly(Nt);
                if (BitConverter.ToUInt16(Nt, 24) != 0x20B || BitConverter.ToUInt32(Nt, 24 + 108) < 4
                    || BitConverter.ToUInt32(Nt, 8) != image.Timestamp || BitConverter.ToUInt32(Nt, 24 + 56) != image.SizeOfImage)
                    throw new InvalidDataException(L10n.T("Resolver.CameraFade.ImageIdentityChangedDuringResolution"));
                int directory = 24 + 112 + 3 * 8;
                _rva = BitConverter.ToUInt32(Nt, directory); uint size = BitConverter.ToUInt32(Nt, directory + 4);
                if (size < 12 || size > MaximumExceptionBytes || size % 12 != 0)
                    throw new NotSupportedException(L10n.T("Resolver.CameraFade.BoundedExceptionTableRequired"));
                RequireRange(image, _rva, checked((int)size));
                PeSection section = SectionFor(image, _rva, checked((int)size));
                stream.Position = checked((long)section.Raw + _rva - section.Rva);
                _table = new byte[checked((int)size)]; stream.ReadExactly(_table);
                int count = _table.Length / 12; uint previousEnd = 0; bool padding = false;
                for (int i = 0; i < count; i++)
                {
                    uint begin = U32(i, 0), end = U32(i, 4), unwind = U32(i, 8);
                    if ((begin | end | unwind) == 0) { padding = true; continue; }
                    if (padding || begin < previousEnd || end <= begin || end > image.SizeOfImage || unwind >= image.SizeOfImage)
                        throw new InvalidDataException(L10n.T("Resolver.CameraFade.UnsortedOrOverlappingFunctionTable"));
                    previousEnd = end; _count++;
                }
                if (_count == 0) throw new InvalidDataException(L10n.T("Resolver.CameraFade.EmptyFunctionTable"));
            }
            private uint U32(int index, int offset) => BitConverter.ToUInt32(_table, checked(index * 12 + offset));
            internal CameraFadeFunction TryRead(ulong site)
            {
                int low = 0, high = _count - 1, found = -1;
                while (low <= high)
                {
                    int mid = low + (high - low) / 2;
                    if (U32(mid, 0) <= site) { found = mid; low = mid + 1; } else high = mid - 1;
                }
                if (found < 0 || site >= U32(found, 4)) return null;
                uint start = U32(found, 0), end = U32(found, 4), unwind = U32(found, 8);
                int length = checked((int)(end - start));
                if (length < 24 || length > MaximumFunctionBytes || !_image.IsExecutableRva(start, length)) return null;
                byte[] code = _image.ReadRva(start, length);
                RequireRange(_image, unwind, 4); byte[] header = _image.ReadRva(unwind, 4);
                int version = header[0] & 7, flags = header[0] >> 3;
                if ((version != 1 && version != 2) || (flags & ~3) != 0 || header[1] > code.Length)
                    throw new NotSupportedException(L10n.T("Resolver.CameraFade.MemberNotIndependentUnwindRoot"));
                int unwindSize = (4 + header[2] * 2 + 3) & ~3;
                if ((flags & 3) != 0) unwindSize += 4;
                RequireRange(_image, unwind, unwindSize);
                return new(start, code, _rva + checked((uint)found * 12), _table.AsSpan(found * 12, 12).ToArray(),
                    unwind, _image.ReadRva(unwind, unwindSize));
            }
        }

        public static ResolvedCameraFade Resolve(PeImage image, Action<string> log = null)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            var table = new FunctionTable(image);
            var distanceCandidates = new Dictionary<ulong, CameraFadeFunction>();
            var updateCandidates = new Dictionary<ulong, CameraFadeFunction>();
            var elevationCandidates = new Dictionary<ulong, CameraFadeFunction>();

            foreach (var chunk in image.ReadExecutableChunks(overlap: 64))
            {
                foreach (int offset in DistanceEntry.Find(chunk.Bytes, chunk.ScanLength))
                {
                    var fn = table.TryRead(chunk.Rva + (ulong)offset);
                    if (fn != null) distanceCandidates.TryAdd(fn.Rva, fn);
                }
                foreach (int offset in DistanceUpdateNucleus.Find(chunk.Bytes, chunk.ScanLength))
                {
                    var fn = table.TryRead(chunk.Rva + (ulong)offset);
                    if (fn != null) updateCandidates.TryAdd(fn.Rva, fn);
                }
                foreach (int offset in ElevationCallKind3.Find(chunk.Bytes, chunk.ScanLength))
                {
                    var fn = table.TryRead(chunk.Rva + (ulong)offset);
                    if (fn != null) elevationCandidates.TryAdd(fn.Rva, fn);
                }
            }

            if (distanceCandidates.Count > 1 || updateCandidates.Count > 1 || elevationCandidates.Count > 1)
                throw new NotSupportedException(L10n.T("Resolver.CameraFade.AmbiguousCohort"));
            if (distanceCandidates.Count != 1 || updateCandidates.Count != 1 || elevationCandidates.Count != 1)
                throw new NotSupportedException(L10n.T("Resolver.CameraFade.CompleteCohortNotFound"));

            CameraFadeFunction distance = SingleValue(distanceCandidates);
            CameraFadeFunction distanceUpdate = SingleValue(updateCandidates);
            CameraFadeFunction elevation = SingleValue(elevationCandidates);

            // Verify Distance (Kind 4) internal fields, 1.0f literal, and calls to shared Helper.
            int dEntry = Unique(DistanceEntry, distance.Code);
            int dFlag = distance.Code[dEntry + 3];
            if (dFlag < 0x10 || distance.Code[dEntry + 16] != dFlag)
                throw new InvalidDataException(L10n.T("Resolver.CameraFade.EnabledOrResetFieldMismatch"));

            int dCall = Unique(DistanceCallKind4, distance.Code);
            int dReset = Unique(DistanceResetKind4, distance.Code);
            int ctrlField = BitConverter.ToInt32(distance.Code, dCall + 3);
            if (ctrlField < 0x10 || BitConverter.ToInt32(distance.Code, dReset + 3) != ctrlField
                || distance.Code[dReset + 41] != dFlag + 4)
                throw new InvalidDataException(L10n.T("Resolver.CameraFade.ScalarOrPriorityMaskMismatch"));

            ulong helperRva = Relative(distance.Rva + (ulong)(dCall + 26) + 5, BitConverter.ToInt32(distance.Code, dCall + 27));
            RequireCall(distance, dReset + 31, helperRva, 0xE8);

            // Verify DistanceUpdate calls Distance with 1.0f reset.
            var literals = new List<CameraFadeLiteral>();
            AddLiteral(image, literals, Relative(distance.Rva + (ulong)(dReset + 12) + 8, BitConverter.ToInt32(distance.Code, dReset + 16)), 4);
            int uNuc = Unique(DistanceUpdateNucleus, distanceUpdate.Code);
            AddLiteral(image, literals, Relative(distanceUpdate.Rva + (ulong)uNuc + 8, BitConverter.ToInt32(distanceUpdate.Code, uNuc + 4)), 4);
            RequireCall(distanceUpdate, uNuc + 13, distance.Rva, 0xE8);

            // Verify Elevation (Kind 3) evaluates pitch curve and calls the EXACT SAME shared Helper & ControllerField.
            _ = Unique(ElevationEvalCurve, elevation.Code);
            int eCall = Unique(ElevationCallKind3, elevation.Code);
            int eReset = Unique(ElevationResetKind3, elevation.Code);
            if (BitConverter.ToInt32(elevation.Code, eCall + 3) != ctrlField
                || BitConverter.ToInt32(elevation.Code, eReset + 3) != ctrlField)
                throw new InvalidDataException(L10n.T("Resolver.CameraFade.ScalarOrPriorityMaskMismatch"));
            RequireCall(elevation, eCall + 20, helperRva, 0xE8);
            RequireCall(elevation, eReset + 25, helperRva, 0xE8);
            AddLiteral(image, literals, Relative(elevation.Rva + (ulong)(eReset + 12) + 8, BitConverter.ToInt32(elevation.Code, eReset + 16)), 4);

            // Resolve and verify shared Helper (CachedRendererDitherController::SetDither).
            CameraFadeFunction helper = table.TryRead(helperRva);
            if (helper == null || helper.Rva != helperRva
                || helper.Code.Length < HelperPrologue.Length + HelperSaveThis.Length + 7
                || !helper.Code.AsSpan(0, HelperPrologue.Length).SequenceEqual(HelperPrologue)
                || !helper.Code.AsSpan(HelperPrologue.Length, HelperSaveThis.Length).SequenceEqual(HelperSaveThis)
                || HelperRangeCheck.Find(helper.Code).Count != 1)
                throw new InvalidDataException(L10n.T("Resolver.CameraFade.CommonPriorityHelperNotCalled"));

            if ((helper.Rva & 7) != 0)
                throw new NotSupportedException(L10n.T("Resolver.CameraFade.HelperEntryNotAligned"));

            var distinct = new HashSet<ulong> { distance.Rva, distanceUpdate.Rva, elevation.Rva, helper.Rva };
            if (distinct.Count != 4)
                throw new InvalidDataException(L10n.T("Resolver.CameraFade.MemberFunctionsOverlap"));

            // Extract writable hotfix guard(s) at the entry of Helper (test byte ptr [rip+disp32], imm8).
            var guards = new List<CameraFadeHotfixGuard>();
            int guardOffset = HelperPrologue.Length + HelperSaveThis.Length;
            if (helper.Code.Length >= guardOffset + 7
                && helper.Code[guardOffset] == 0xF6 && helper.Code[guardOffset + 1] == 0x05)
            {
                ulong flagRva = Relative(helper.Rva + (ulong)guardOffset + 7, BitConverter.ToInt32(helper.Code, guardOffset + 2));
                byte mask = helper.Code[guardOffset + 6];
                if (mask == 0 || !image.IsWritableRva(flagRva, 1))
                    throw new InvalidDataException(L10n.T("Resolver.CameraFade.HotfixGuardOutsideWritableData"));
                guards.Add(new CameraFadeHotfixGuard(flagRva, mask));
            }
            if (guards.Count == 0)
                throw new InvalidDataException(L10n.T("Resolver.CameraFade.HotfixGuardNotEstablished"));

            log?.Invoke(L10n.T("Resolver.CameraFade.FunctionsLocated"));
            return new ResolvedCameraFade
            {
                Distance = distance,
                DistanceUpdate = distanceUpdate,
                Elevation = elevation,
                Helper = helper,
                ControllerField = ctrlField,
                ElevationKind = 3,
                DistanceKind = 4,
                Literals = literals.AsReadOnly(),
                HotfixGuards = guards.AsReadOnly(),
                DosHeader = table.Dos,
                NtHeaders = table.Nt,
                NtOffset = table.NtOffset,
                ImagePath = Path.GetFullPath(image.Path),
                Timestamp = image.Timestamp,
                ImageSize = image.SizeOfImage,
            };
        }

        public static void VerifyLive(TargetProcess process, ResolvedGameBuild build, ResolvedCameraFade fade)
        {
            if (process == null || build?.AssemblyImage == null || fade == null) throw new ArgumentNullException(nameof(build));
            var module = TargetProcess.FindModule(process.Pid, "GameAssembly.dll");
            if (module.Base == 0 || module.Base != build.AssemblyBase || module.Size != build.AssemblySize
                || module.Size != fade.ImageSize || build.AssemblyImage.Timestamp != fade.Timestamp
                || !string.Equals(Path.GetFullPath(module.Path), fade.ImagePath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFullPath(build.AssemblyImage.Path), fade.ImagePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L10n.T("Resolver.CameraFade.AssemblyModuleIdentityChanged"));
            var disk = new PeImage(fade.ImagePath);
            if (disk.Timestamp != fade.Timestamp || disk.SizeOfImage != fade.ImageSize)
                throw new InvalidOperationException(L10n.T("Resolver.CameraFade.AssemblyFileChangedAfterResolution"));
            using (var stream = File.OpenRead(disk.Path))
            {
                byte[] dos = new byte[fade.DosHeader.Length]; stream.ReadExactly(dos);
                stream.Position = fade.NtOffset; byte[] nt = new byte[fade.NtHeaders.Length]; stream.ReadExactly(nt);
                if (!dos.AsSpan().SequenceEqual(fade.DosHeader) || !nt.AsSpan().SequenceEqual(fade.NtHeaders))
                    throw new InvalidOperationException(L10n.T("Resolver.CameraFade.AssemblyFileHeadersChangedAfterResolution"));
            }
            Equal(process, build.AssemblyBase, fade.DosHeader);
            if (!MatchesMappedHeaders(fade.NtHeaders,
                process.Read(checked(build.AssemblyBase + fade.NtOffset), fade.NtHeaders.Length), build.AssemblyBase))
                throw new InvalidOperationException(L10n.T("Resolver.CameraFade.AssemblyMappedHeaderIdentityMismatch"));
            void Range(ulong rva, byte[] expected)
            {
                if (!disk.ReadRva(rva, expected.Length).AsSpan().SequenceEqual(expected))
                    throw new InvalidOperationException(L10n.T("Resolver.CameraFade.AssemblyFileChangedAfterResolution"));
                Equal(process, checked(build.AssemblyBase + rva), expected);
            }
            foreach (var member in fade.Functions)
            {
                Range(member.Rva, member.Code);
                Range(member.TableEntryRva, member.TableEntry);
                Range(member.UnwindRva, member.Unwind);
            }
            foreach (var literal in fade.Literals) Range(literal.Rva, literal.Bytes);
            foreach (var guard in fade.HotfixGuards)
            {
                byte[] live = process.Read(checked(build.AssemblyBase + guard.Rva), 1);
                if (live == null || (live[0] & guard.Mask) != 0)
                    throw new NotSupportedException(L10n.T("Resolver.CameraFade.HotfixedOrNotInitialized"));
            }
        }

        internal static void Equal(TargetProcess process, ulong address, byte[] expected)
        {
            byte[] actual = process.Read(address, expected.Length);
            if (actual == null || !actual.AsSpan().SequenceEqual(expected))
                throw new InvalidOperationException(L10n.T("Resolver.CameraFade.VerifiedDiskIdentityMismatch"));
        }

        internal static bool MatchesMappedHeaders(byte[] expected, byte[] actual, ulong mappedBase)
        {
            const int imageBaseOffset = 48, afterImageBase = imageBaseOffset + 8;
            if (expected == null || actual == null || expected.Length < afterImageBase
                || actual.Length != expected.Length) return false;
            if (actual.AsSpan().SequenceEqual(expected)) return true;
            return BitConverter.ToUInt64(actual, imageBaseOffset) == mappedBase
                && actual.AsSpan(0, imageBaseOffset).SequenceEqual(expected.AsSpan(0, imageBaseOffset))
                && actual.AsSpan(afterImageBase).SequenceEqual(expected.AsSpan(afterImageBase));
        }

        private static CameraFadeFunction SingleValue(Dictionary<ulong, CameraFadeFunction> map)
        {
            foreach (var v in map.Values) return v;
            throw new NotSupportedException(L10n.T("Resolver.CameraFade.CompleteCohortNotFound"));
        }

        private static int Unique(Pattern pattern, byte[] code)
        {
            var matches = pattern.Find(code);
            if (matches.Count != 1) throw new InvalidDataException(L10n.T("Resolver.CameraFade.AmbiguousInstructionRelationship"));
            return matches[0];
        }

        private static void RequireCall(CameraFadeFunction function, int offset, ulong target, byte opcode)
        {
            if (offset < 0 || offset > function.Code.Length - 5 || function.Code[offset] != opcode
                || Relative(function.Rva + (ulong)offset + 5, BitConverter.ToInt32(function.Code, offset + 1)) != target)
                throw new InvalidDataException(L10n.T("Resolver.CameraFade.CommonPriorityHelperNotCalled"));
        }

        private static ulong Relative(ulong next, int displacement)
        {
            long rva = checked((long)next + displacement);
            if (rva <= 0 || rva > uint.MaxValue) throw new InvalidDataException(L10n.T("Resolver.CameraFade.RelativeReferenceOutsideRva"));
            return (ulong)rva;
        }

        private static void AddLiteral(PeImage image, List<CameraFadeLiteral> literals, ulong rva, int size)
        {
            RequireRange(image, rva, size); byte[] bytes = image.ReadRva(rva, size);
            for (int at = 0; at < size; at += 4)
                if (BitConverter.ToUInt32(bytes, at) != 0x3F800000) throw new InvalidDataException(L10n.T("Resolver.CameraFade.UnityAlphaLiteralMismatch"));
            foreach (var prior in literals) if (prior.Rva == rva && prior.Bytes.Length == size) return;
            literals.Add(new(rva, bytes));
        }

        private static PeSection SectionFor(PeImage image, ulong rva, int size)
        {
            PeSection result = null; int overlaps = 0;
            foreach (var section in image.Sections)
            {
                ulong end = (ulong)section.Rva + section.RawSize;
                if (rva < end && section.Rva < rva + (ulong)size) overlaps++;
                if (rva >= section.Rva && rva + (ulong)size <= end) result = section;
            }
            if (overlaps != 1 || result == null) throw new InvalidDataException(L10n.T("Resolver.CameraFade.AmbiguousFileBackedRange"));
            return result;
        }

        private static void RequireRange(PeImage image, ulong rva, int size)
        {
            if (size <= 0 || rva == 0 || rva >= image.SizeOfImage || (ulong)size > image.SizeOfImage - rva)
                throw new InvalidDataException(L10n.T("Resolver.CameraFade.RangeOutsideImage"));
            SectionFor(image, rva, size);
        }
    }
}
