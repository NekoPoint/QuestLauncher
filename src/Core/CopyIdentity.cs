using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace MinimalQuestLauncher
{
    internal enum FileKind
    {
        Missing,

        Foreign,

        OwnCopy,

        LauncherBuild,

        LegacyKnown,

        LegacyLikely,

        Unreadable
    }

    internal sealed class FileInspection
    {
        public string Path;
        public FileKind Kind;
        public long Size;
        public string Sha256;

        public string BuildHash;
        public string CopyId;
        public string Version;
        public string Error;
        public DateTime LastWriteUtc;

        public bool IsVerifiedOwnedCopy
        {
            get {
                return Kind == FileKind.OwnCopy || Kind == FileKind.LegacyKnown;
            }
        }
    }

    internal static class CopyIdentity
    {
        public const int MarkerLength = 40;
        public const int SignatureHexLength = 512;
        public const int SignatureSlotLength = 6 + SignatureHexLength;
        public const long MaxInspectSize = 8L * 1024 * 1024;

        private static readonly string RolePrefixStr = new string(new[] { 'Q', 'L', 'M', 'K', '1', ':' });
        private static readonly string SigPrefixStr = new string(new[] { 'Q', 'L', 'S', 'G', '1', ':' });
        private static readonly byte[] PrefixUtf16 = Encoding.Unicode.GetBytes(RolePrefixStr);
        private static readonly byte[] SigPrefixUtf16 = Encoding.Unicode.GetBytes(SigPrefixStr);

        private const string RevokedKeyModulusFragment =
            "wFmjR/rQmTieu6WWWGdGg2HvdKJdK81lqA7xJaNeYUC2J7fXYALf1UhHDZECq62k";

        public static readonly string OfficialReleasePublicKeyXml = null;

#if QL_TEST_HARNESS

        internal static string TestHarnessPublicKeyXml;
#endif

        public static readonly Dictionary<string, string> KnownLegacy =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                { "C31DCC4FBB817A0294499FF7BDDA013DDA3782BACF88285C4AF3F3DAB98848EC", "1.0" },
                { "F474A26579280FD2452B070EA77FEBB52D90867E786571CC1642457E5244BC1E", "1.1" },
                { "92E1978961F9D905DABDC6ACA921C63C51ED86F4E0AE0933053D78872A1DAAC6", "1.2" },
                { "A9E8F0811CCA5DA56C758FD9AAB3B1659818B792CD381196A4FF5993C5ADAFE4", "1.3-test" },
                { "7DC0C100E16DABBC73871D6495DE2A90ABD0F798CAE04D2D291621E9D09764C1", "1.3" }
            };

        public static readonly Dictionary<string, string> KnownOfficialBuildHashes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {};

        private static string _selfBuildHash;

        public static string SelfBuildHash
        {
            get {
                if (_selfBuildHash == null)
                {
                    try
                    {
                        byte[] raw = ReadAllBytesShared(AppInfo.ExecutablePath, MaxInspectSize);
                        List<int> m = FindMarkers(raw);
                        if (m.Count == 1)
                            _selfBuildHash = ComputeBuildHash(raw, m);
                    }
                    catch
                    {
                    }
                }
                return _selfBuildHash;
            }
        }

        public static bool IsTrustedBuildHash(string candidateBuildHash, string explicitTrustedBuildHash,
                                              out string knownVersion)
        {
            knownVersion = null;
            if (string.IsNullOrEmpty(candidateBuildHash) || candidateBuildHash.Length != 64)
                return false;
            if (!string.IsNullOrEmpty(explicitTrustedBuildHash) &&
                string.Equals(candidateBuildHash, explicitTrustedBuildHash, StringComparison.OrdinalIgnoreCase))
                return true;
            string self = SelfBuildHash;
            if (!string.IsNullOrEmpty(self) &&
                string.Equals(candidateBuildHash, self, StringComparison.OrdinalIgnoreCase))
                return true;
            if (KnownOfficialBuildHashes.TryGetValue(candidateBuildHash, out knownVersion))
                return true;
            return false;
        }

        public static string LauncherMarker
        {
            get {
                return RolePrefixStr + "L:" + new string('0', 32);
            }
        }

        public static string EmptySignatureSlot
        {
            get {
                return SigPrefixStr + new string('0', SignatureHexLength);
            }
        }

        public static string MakeCopyMarker(string id)
        {
            return RolePrefixStr + "D:" + id;
        }

        public static string NewCopyId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static bool IsValidMarker(string m)
        {
            if (m == null || m.Length != MarkerLength)
                return false;
            if (!m.StartsWith(RolePrefixStr, StringComparison.Ordinal))
                return false;
            if (m[6] != 'L' && m[6] != 'D')
                return false;
            if (m[7] != ':')
                return false;
            for (int i = 8; i < MarkerLength; i++)
            {
                char c = m[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return false;
            }
            return true;
        }

        public static bool IsValidSignatureSlot(string s)
        {
            if (s == null || s.Length != SignatureSlotLength)
                return false;
            if (!s.StartsWith(SigPrefixStr, StringComparison.Ordinal))
                return false;
            for (int i = 6; i < SignatureSlotLength; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        public static char CurrentRole
        {
            get {
                string m = AppInfo.EmbeddedMarker;
                return IsValidMarker(m) ? m[6] : 'L';
            }
        }

        public static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        public static string ToLowerHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static byte[] FromHex(string hex)
        {
            if (hex == null || (hex.Length & 1) != 0)
                return null;
            var result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = HexVal(hex[i * 2]);
                int lo = HexVal(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0)
                    return null;
                result[i] = (byte)((hi << 4) | lo);
            }
            return result;
        }

        private static int HexVal(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            if (c >= 'A' && c <= 'F')
                return c - 'A' + 10;
            return -1;
        }

        public static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(data));
        }

        public static string Sha256(Stream s)
        {
            using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(s));
        }

        public static List<int> FindMarkers(byte[] data)
        {
            var result = new List<int>();
            int markerBytes = MarkerLength * 2;
            for (int i = 0; i + markerBytes <= data.Length; i++)
            {
                if (data[i] != PrefixUtf16[0])
                    continue;
                bool match = true;
                for (int j = 1; j < PrefixUtf16.Length; j++)
                {
                    if (data[i + j] != PrefixUtf16[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (!match)
                    continue;
                string candidate;
                try
                {
                    candidate = Encoding.Unicode.GetString(data, i, markerBytes);
                }
                catch
                {
                    continue;
                }
                if (IsValidMarker(candidate))
                    result.Add(i);
            }
            return result;
        }

        public static List<int> FindSignatureSlots(byte[] data)
        {
            var result = new List<int>();
            int slotBytes = SignatureSlotLength * 2;
            for (int i = 0; i + slotBytes <= data.Length; i++)
            {
                if (data[i] != SigPrefixUtf16[0])
                    continue;
                bool match = true;
                for (int j = 1; j < SigPrefixUtf16.Length; j++)
                {
                    if (data[i + j] != SigPrefixUtf16[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (!match)
                    continue;
                string candidate;
                try
                {
                    candidate = Encoding.Unicode.GetString(data, i, slotBytes);
                }
                catch
                {
                    continue;
                }
                if (IsValidSignatureSlot(candidate))
                    result.Add(i);
            }
            return result;
        }

        public static string ReadMarker(byte[] data, int offset)
        {
            return Encoding.Unicode.GetString(data, offset, MarkerLength * 2);
        }

        public static string ReadSignatureSlot(byte[] data, int offset)
        {
            return Encoding.Unicode.GetString(data, offset, SignatureSlotLength * 2);
        }

        public static byte[] BuildCopyBytes(byte[] source, string copyId)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (copyId == null || copyId.Length != 32)
                throw new ArgumentException("copyId");
            List<int> pos = FindMarkers(source);
            if (pos.Count != 1)
                throw new InvalidOperationException(
                    "Исполняемый файл лаунчера повреждён или изменён: маркер роли найден " + pos.Count +
                    " раз(а) вместо одного.");
            byte[] result = (byte[])source.Clone();
            byte[] marker = Encoding.Unicode.GetBytes(MakeCopyMarker(copyId));
            Buffer.BlockCopy(marker, 0, result, pos[0], marker.Length);
            return result;
        }

        public static string ComputeBuildHash(byte[] data, List<int> markers)
        {
            if (markers == null || markers.Count != 1)
                return null;
            byte[] copy = (byte[])data.Clone();
            byte[] marker = Encoding.Unicode.GetBytes(LauncherMarker);
            Buffer.BlockCopy(marker, 0, copy, markers[0], marker.Length);
            return Sha256(copy);
        }

        private static byte[] ComputeCanonicalUnsignedHashBytes(byte[] data, int markerOffset, int sigSlotOffset)
        {
            byte[] copy = (byte[])data.Clone();
            byte[] normMarker = Encoding.Unicode.GetBytes(LauncherMarker);
            byte[] normSig = Encoding.Unicode.GetBytes(EmptySignatureSlot);
            Buffer.BlockCopy(normMarker, 0, copy, markerOffset, normMarker.Length);
            Buffer.BlockCopy(normSig, 0, copy, sigSlotOffset, normSig.Length);
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(copy);
            }
        }

        public static byte[] SignBuildBytes(byte[] rawExeBytes, string rsaPrivateKeyXml)
        {
            if (rawExeBytes == null)
                throw new ArgumentNullException("rawExeBytes");
            if (string.IsNullOrEmpty(rsaPrivateKeyXml))
                throw new ArgumentNullException("rsaPrivateKeyXml");
            if (rsaPrivateKeyXml.IndexOf(RevokedKeyModulusFragment, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Использование отозванного тестового ключа запрещено.");

            List<int> markers = FindMarkers(rawExeBytes);
            if (markers.Count != 1)
                throw new InvalidOperationException("Ожидался ровно 1 маркер QLMK1, найдено: " + markers.Count);
            List<int> sigSlots = FindSignatureSlots(rawExeBytes);
            if (sigSlots.Count != 1)
                throw new InvalidOperationException("Ожидался ровно 1 слот подписи QLSG1, найдено: " + sigSlots.Count);

            byte[] digest = ComputeCanonicalUnsignedHashBytes(rawExeBytes, markers[0], sigSlots[0]);
            byte[] signature;
            using (var rsa = new RSACng())
            {
                rsa.FromXmlString(rsaPrivateKeyXml);
                signature = rsa.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            string sigHex = ToLowerHex(signature);
            if (sigHex.Length != SignatureHexLength)
                throw new InvalidOperationException("Некорректная длина подписи RSA-2048: " + sigHex.Length);

            byte[] signed = (byte[])rawExeBytes.Clone();
            byte[] slotBytes = Encoding.Unicode.GetBytes(SigPrefixStr + sigHex);
            Buffer.BlockCopy(slotBytes, 0, signed, sigSlots[0], slotBytes.Length);
            return signed;
        }

        public static bool VerifyOfficialBuildSignature(byte[] data, List<int> markers)
        {
            return VerifyBuildSignatureWithKey(data, markers, OfficialReleasePublicKeyXml);
        }

        internal static bool VerifyBuildSignatureWithKey(byte[] data, List<int> markers, string publicKeyXml)
        {
            if (string.IsNullOrEmpty(publicKeyXml))
                return false;
            if (publicKeyXml.IndexOf(RevokedKeyModulusFragment, StringComparison.Ordinal) >= 0)
                return false;
            if (data == null || markers == null || markers.Count != 1)
                return false;

            List<int> sigSlots = FindSignatureSlots(data);
            if (sigSlots.Count != 1)
                return false;

            string slot = ReadSignatureSlot(data, sigSlots[0]);
            string sigHex = slot.Substring(6);

            bool allZero = true;
            for (int i = 0; i < sigHex.Length; i++)
            {
                if (sigHex[i] != '0')
                {
                    allZero = false;
                    break;
                }
            }
            if (allZero)
                return false;

            byte[] sigBytes = FromHex(sigHex);
            if (sigBytes == null || sigBytes.Length != 256)
                return false;

            byte[] digest = ComputeCanonicalUnsignedHashBytes(data, markers[0], sigSlots[0]);
            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(publicKeyXml);
                    return rsa.VerifyHash(digest, CryptoConfig.MapNameToOID("SHA256"), sigBytes);
                }
            }
            catch
            {
                return false;
            }
        }

        public static string LegacyVersion(string sha)
        {
            string v;
            return sha != null && KnownLegacy.TryGetValue(sha, out v) ? v : null;
        }

        private static bool ContainsAscii(byte[] data, string text)
        {
            byte[] p = Encoding.ASCII.GetBytes(text);
            for (int i = 0; i + p.Length <= data.Length; i++)
            {
                if (data[i] != p[0])
                    continue;
                int j = 1;
                while (j < p.Length && data[i + j] == p[j])
                    j++;
                if (j == p.Length)
                    return true;
            }
            return false;
        }

        public static bool LooksLikeLegacy(byte[] data, string path)
        {
            if (data.Length < 2 || data[0] != 'M' || data[1] != 'Z')
                return false;
            if (!ContainsAscii(data, "MinimalQuestLauncher") || !ContainsAscii(data, "DummyForm") ||
                !ContainsAscii(data, "LauncherForm"))
                return false;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;
            try
            {
                AssemblyName an = AssemblyName.GetAssemblyName(path);
                return string.Equals(an.Name, "QuestLauncher", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string AssemblyVersionOf(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                Version v = AssemblyName.GetAssemblyName(path).Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
            catch
            {
                return null;
            }
        }

        public static byte[] ReadAllBytesShared(string path, long maxSize)
        {
            using (var fs =
                       new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length > maxSize)
                    throw new IOException("Файл слишком большой для проверки (" + fs.Length + " байт).");
                var buf = new byte[fs.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n <= 0)
                        throw new IOException("Неожиданный конец файла.");
                    read += n;
                }
                return buf;
            }
        }

        public static FileInspection Inspect(string path)
        {
            return Inspect(path, null);
        }

        public static FileInspection Inspect(string path, string trustedBuildHash)
        {
            var r = new FileInspection { Path = path };
            try
            {
                if (Directory.Exists(path))
                {
                    r.Kind = FileKind.Foreign;
                    r.Error = "По этому пути находится папка.";
                    return r;
                }
                if (!File.Exists(path))
                {
                    r.Kind = FileKind.Missing;
                    return r;
                }
                var fi = new FileInfo(path);
                r.Size = fi.Length;
                r.LastWriteUtc = fi.LastWriteTimeUtc;
                if (fi.Length > MaxInspectSize)
                {
                    r.Kind = FileKind.Foreign;
                    return r;
                }
                byte[] data = ReadAllBytesShared(path, MaxInspectSize);
                return InspectBytes(path, data, r, trustedBuildHash);
            }
            catch (Exception ex)
            {
                r.Kind = FileKind.Unreadable;
                r.Error = ex.Message;
                return r;
            }
        }

        public static FileInspection InspectBytes(string path, byte[] data, FileInspection r)
        {
            return InspectBytes(path, data, r, null);
        }

        public static FileInspection InspectBytes(string path, byte[] data, FileInspection r, string trustedBuildHash)
        {
            if (r == null)
                r = new FileInspection { Path = path };
            r.Size = data.Length;
            r.Sha256 = Sha256(data);

            string legacy = LegacyVersion(r.Sha256);
            if (legacy != null)
            {
                r.Kind = FileKind.LegacyKnown;
                r.Version = legacy;
                return r;
            }

            List<int> markers = FindMarkers(data);
            if (markers.Count == 1 && data.Length > 1 && data[0] == 'M' && data[1] == 'Z')
            {
                string candidateBuildHash = ComputeBuildHash(data, markers);
                string knownVer;
                bool trustedByHash = IsTrustedBuildHash(candidateBuildHash, trustedBuildHash, out knownVer);
                bool trustedBySignature = !trustedByHash && VerifyOfficialBuildSignature(data, markers);
#if QL_TEST_HARNESS
                if (!trustedByHash && !trustedBySignature && !string.IsNullOrEmpty(TestHarnessPublicKeyXml))
                {
                    trustedBySignature = VerifyBuildSignatureWithKey(data, markers, TestHarnessPublicKeyXml);
                }
#endif

                if (trustedByHash || trustedBySignature)
                {
                    string m = ReadMarker(data, markers[0]);
                    r.BuildHash = candidateBuildHash;
                    r.Version = knownVer ?? AssemblyVersionOf(path) ?? AppInfo.Version;
                    if (m[6] == 'D')
                    {
                        r.Kind = FileKind.OwnCopy;
                        r.CopyId = m.Substring(8);
                    }
                    else
                    {
                        r.Kind = FileKind.LauncherBuild;
                    }
                    return r;
                }
            }

            if (LooksLikeLegacy(data, path))
            {
                r.Kind = FileKind.LegacyLikely;
                r.Version = "неподтверждённая сборка";
                return r;
            }

            r.Kind = FileKind.Foreign;
            return r;
        }

        public static string Describe(FileKind k)
        {
            switch (k)
            {
            case FileKind.Missing:
                return "файл отсутствует";
            case FileKind.Foreign:
                return "посторонний файл";
            case FileKind.OwnCopy:
                return "подтверждённая копия QuestLauncher";
            case FileKind.LauncherBuild:
                return "сам лаунчер QuestLauncher";
            case FileKind.LegacyKnown:
                return "подтверждённая старая копия QuestLauncher (известный релиз)";
            case FileKind.LegacyLikely:
                return "неподтверждённый кандидат (похож по сигнатуре, хеш неизвестен)";
            default:
                return "не удалось прочитать";
            }
        }
    }
}
