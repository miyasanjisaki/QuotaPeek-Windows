using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace QuotaPeek.Windows
{
    internal static class CredentialStore
    {
        private const int CredentialTypeGeneric = 1;
        private const int CredentialPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;
        private const string TargetPrefix = "QuotaPeek/";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref NativeCredential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll", SetLastError = false)]
        private static extern void CredFree(IntPtr buffer);

        public static string Read(string profileId)
        {
            IntPtr pointer;
            if (!CredRead(Target(profileId), CredentialTypeGeneric, 0, out pointer))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound) return "";
                throw new Win32Exception(error, "无法读取 Windows 凭据管理器中的 API Key。");
            }

            try
            {
                var credential = (NativeCredential)Marshal.PtrToStructure(pointer, typeof(NativeCredential));
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return "";
                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                try
                {
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    Array.Clear(bytes, 0, bytes.Length);
                }
            }
            finally
            {
                CredFree(pointer);
            }
        }

        public static void Save(string profileId, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? "");
            var buffer = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                if (bytes.Length > 0) Marshal.Copy(bytes, 0, buffer, bytes.Length);
                var credential = new NativeCredential
                {
                    Type = CredentialTypeGeneric,
                    TargetName = Target(profileId),
                    CredentialBlobSize = bytes.Length,
                    CredentialBlob = buffer,
                    Persist = CredentialPersistLocalMachine,
                    UserName = "QuotaPeek"
                };
                if (!CredWrite(ref credential, 0))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法将 API Key 写入 Windows 凭据管理器。");
                }
            }
            finally
            {
                for (var index = 0; index < bytes.Length; index++) Marshal.WriteByte(buffer, index, 0);
                Marshal.FreeHGlobal(buffer);
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        public static void Delete(string profileId)
        {
            if (CredDelete(Target(profileId), CredentialTypeGeneric, 0)) return;
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "无法从 Windows 凭据管理器删除 API Key。");
            }
        }

        private static string Target(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("连接标识不能为空。", "profileId");
            return TargetPrefix + profileId;
        }
    }
}
