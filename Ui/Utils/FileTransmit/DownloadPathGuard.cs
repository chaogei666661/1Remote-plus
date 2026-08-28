using System;
using System.Collections.Generic;
using System.IO;

namespace _1RM.Utils.FileTransmit
{
    /// <summary>
    /// Raised when a name an SFTP or FTP server sent would put a downloaded file somewhere the user did not
    /// choose. Carries the name so the caller can put it in a message; the message on the exception itself is
    /// deliberately plain English, because this type is thrown from a class that must stay free of the app.
    /// </summary>
    public class UnsafeRemoteNameException : Exception
    {
        public string RemoteName { get; }

        public UnsafeRemoteNameException(string remoteName)
            : base($"the server returned a name that would write outside the download folder: {remoteName}")
        {
            RemoteName = remoteName;
        }

        public UnsafeRemoteNameException(string remoteName, string message) : base(message)
        {
            RemoteName = remoteName;
        }
    }

    /// <summary>
    /// Decides where a remote directory entry is allowed to land on this machine.
    ///
    /// A recursive download asks the server for a listing and then writes each entry under the folder the
    /// user picked. Both halves of the local path came from the server: the entry's name, and the part of its
    /// full path below the directory being copied. <see cref="Path.Combine(string,string)"/> gives that away
    /// twice over — it drops everything before a rooted second argument, so a listing containing
    /// <c>C:\Windows\System32\evil.dll</c> writes exactly there, and it does not resolve <c>..</c>, so a
    /// listing containing <c>..\..\Startup\evil.exe</c> climbs out of the download folder. Nothing in either
    /// protocol stops a server from answering that way. It is the same flaw the SSH.NET maintainers fixed in
    /// their own recursive download for CVE-2026-48798, and this application recurses itself rather than
    /// calling theirs, so their fix does not reach it.
    ///
    /// Two independent checks, because either alone has a hole. Segment inspection is authoritative and
    /// behaves identically on every platform: a remote path is split on both separators, since <c>\</c> is an
    /// ordinary character in a POSIX file name and a separator to Win32, and that mismatch is precisely what
    /// an attacker reaches for. The full-path containment test that follows is the backstop for whatever a
    /// future platform quirk normalises differently.
    /// </summary>
    public static class DownloadPathGuard
    {
        private static readonly char[] Separators = { '/', '\\' };

        /// <summary>
        /// The local path <paramref name="remotePath"/> should be written to, guaranteed to sit inside
        /// <paramref name="destinationDirectory"/>.
        ///
        /// A rooted path is re-rooted under the destination rather than refused: the caller strips the parent
        /// prefix off an entry's full remote path, so what it passes here legitimately begins with a
        /// separator most of the time, and there is no way to tell that apart from a server answering
        /// <c>/etc/cron.d/x</c>. Both end up under the destination, which is the property that matters.
        /// Refusal is reserved for a name that cannot be placed there at all.
        /// </summary>
        /// <param name="destinationDirectory">The folder the user chose. An absolute local path.</param>
        /// <param name="remotePath">A name, or a path relative to the destination, as the server reported it.</param>
        /// <exception cref="UnsafeRemoteNameException">The name cannot be placed inside the destination.</exception>
        public static string Resolve(string destinationDirectory, string remotePath)
        {
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                throw new ArgumentException("a destination directory is required", nameof(destinationDirectory));

            var combined = destinationDirectory.TrimEnd(Separators);
            foreach (var segment in SafeSegments(remotePath))
                combined = combined + Path.DirectorySeparatorChar + segment;

            if (!IsContained(destinationDirectory, combined))
                throw new UnsafeRemoteNameException(Describe(remotePath));

            return combined;
        }

        /// <summary><see cref="Resolve"/> without the exception, for callers that only want to know.</summary>
        public static bool TryResolve(string destinationDirectory, string remotePath, out string localPath)
        {
            try
            {
                localPath = Resolve(destinationDirectory, remotePath);
                return true;
            }
            catch (UnsafeRemoteNameException)
            {
                localPath = "";
                return false;
            }
        }

        /// <summary>
        /// Whether a single listing entry is usable as one local path component. A separator is rejected
        /// rather than split: an entry's <c>Name</c> is by definition one component, so a separator inside it
        /// is either a broken server or a deliberate one.
        ///
        /// Only names that could put bytes somewhere unexpected are refused here. <c>* ? " &lt; &gt; |</c> are
        /// illegal in a Win32 name but harmless — they cannot redirect a write — so they are left to fail at
        /// the file system, which reports them more accurately than this class could.
        /// </summary>
        public static bool IsSafeSegment(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name!.IndexOfAny(Separators) >= 0)
                return false;
            // "." and ".." navigate; a longer run of dots is stripped to one of those by Win32 name
            // normalisation, so it navigates too.
            if (IsDotsOnly(name))
                return false;
            // A colon is either a drive qualifier or an NTFS alternate data stream: `notes.txt:evil.exe`
            // writes a payload that does not appear in the folder listing at all.
            return name.IndexOf(':') < 0;
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> resolves to something at or below <paramref name="root"/>.
        /// Public because a caller that builds a path some other way still wants the last word before it
        /// opens a stream.
        /// </summary>
        public static bool IsContained(string root, string candidate)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
                return false;

            string fullRoot;
            string fullCandidate;
            try
            {
                fullRoot = Path.GetFullPath(NormalizeRoot(root)).TrimEnd(Separators);
                fullCandidate = Path.GetFullPath(candidate);
            }
            catch (Exception)
            {
                // A path the platform refuses to normalise is not one to write to.
                return false;
            }

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (string.Equals(fullRoot, fullCandidate.TrimEnd(Separators), comparison))
                return true;

            // The separator matters: without it `C:\downloads-elsewhere` passes as a child of `C:\downloads`.
            return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
        }

        private static List<string> SafeSegments(string remotePath)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
                throw new UnsafeRemoteNameException(Describe(remotePath));
            if (HasDriveQualifier(remotePath))
                throw new UnsafeRemoteNameException(Describe(remotePath));

            var segments = new List<string>();
            foreach (var raw in remotePath.Split(Separators))
            {
                // A leading, trailing or doubled separator produces an empty segment that means nothing.
                // Dropping it is what every path parser does, and it cannot escape anything.
                if (raw.Length == 0 || raw == ".")
                    continue;
                if (!IsSafeSegment(raw))
                    throw new UnsafeRemoteNameException(Describe(remotePath));
                segments.Add(raw);
            }

            if (segments.Count == 0)
                throw new UnsafeRemoteNameException(Describe(remotePath));

            return segments;
        }

        /// <summary>
        /// <c>C:\x</c> and <c>C:x</c> — the second is drive-relative, resolves against that drive's working
        /// directory, and counts as rooted for <see cref="Path.Combine(string,string)"/>. Recognised by shape
        /// rather than by <see cref="Path.IsPathRooted(string)"/>, which answers for the platform the code
        /// happens to be running on and not for the one the file will be written on.
        /// </summary>
        private static bool HasDriveQualifier(string value)
        {
            return value.Length >= 2 && value[1] == ':' && char.IsLetter(value[0]);
        }

        /// <summary>
        /// The callers hold a destination with its trailing separator already stripped, which turns a
        /// download into the root of a drive from <c>D:\</c> into <c>D:</c> — and that is drive-relative, so
        /// <see cref="Path.GetFullPath(string)"/> would answer with that drive's working directory and every
        /// file in the transfer would look like an escape.
        /// </summary>
        private static string NormalizeRoot(string root)
        {
            return root.Length == 2 && HasDriveQualifier(root) ? root + Path.DirectorySeparatorChar : root;
        }

        private static bool IsDotsOnly(string value)
        {
            foreach (var c in value)
                if (c != '.')
                    return false;
            return true;
        }

        private static string Describe(string? remoteName)
        {
            return string.IsNullOrEmpty(remoteName) ? "(empty)" : remoteName!;
        }
    }
}
