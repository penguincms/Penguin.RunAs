using Meziantou.Framework.Win32;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Penguin

{
    public static partial class RunAs
    {
        [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessWithLogonW(string userName, string domain, string password, int logonFlags, string applicationName, string commandLine, int creationFlags, IntPtr environment, string currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

        public static Process Start(string userName, bool v1, bool v2, bool v3, object pENGUINCOPY_PATH, string arg, string v4) => throw new NotImplementedException();

        [DllImport("userenv")]
        private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

        [DllImport("userenv")]
        private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

        [DllImport("kernel32")]
        private static extern bool CloseHandle(IntPtr hObject);

        private static void SplitUsername(string username, out string user, out string domain)
        {
            string[] parts = username.Split('\\', '@');
            if (parts.Length == 1)
            {
                user = parts[0];
                domain = Environment.UserDomainName;
            }
            else if (username.Contains("@"))
            {
                user = parts[0];
                domain = parts[1];
            }
            else
            {
                user = parts[1];
                domain = parts[0];
            }
        }

        [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern uint AssocQueryString(
            AssocF flags,
            AssocStr str,
            string pszAssoc,
            string pszExtra,
            [Out] StringBuilder pszOut,
            ref uint pcchOut
        );

        //https://stackoverflow.com/questions/162331/finding-the-default-application-for-opening-a-particular-file-type-on-windows
        public static string AssocQueryString(AssocStr association, string extension)
        {
            const int S_OK = 0;
            const int S_FALSE = 1;

            uint length = 0;
            uint ret = AssocQueryString(AssocF.None, association, extension, null, null, ref length);
            if (ret != S_FALSE)
            {
                throw new InvalidOperationException("Could not determine associated string");
            }

            StringBuilder sb = new((int)length); // (length-1) will probably work too as the marshaller adds null termination
            ret = AssocQueryString(AssocF.None, association, extension, null, sb, ref length);
            return ret != S_OK ? throw new InvalidOperationException("Could not determine associated string") : sb.ToString();
        }

        public static string GetShellOpen(string path)
        {
            string extension = System.IO.Path.GetExtension(path);

            string openFile = (string)Registry.GetValue(@$"HKEY_CLASSES_ROOT\{extension}", "", null);

            string commandLine = (string)Registry.GetValue(@$"HKEY_CLASSES_ROOT\{openFile}\Shell\Open\Command", "", null);

            return commandLine.Replace("%1", path);
        }

        public static Process Start(string UserName, string Password, bool noProfile, bool env, bool netOnly, string applicationName, string commandLine, string currentDirectory)
        {
            if (!applicationName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string exe = AssocQueryString(AssocStr.Executable, Path.GetExtension(applicationName));

                commandLine = $"\"{applicationName}\" {commandLine}";

                applicationName = exe;
            }

            //Add space?
            commandLine = $" {commandLine}";

            STARTUPINFO s = new()
            {
                dwFlags = 1, // STARTF_USESHOWWINDOW
                wShowWindow = 1, // SW_SHOWNORMAL
                cb = Marshal.SizeOf(typeof(STARTUPINFO))
            };

            int logonFlags = 1; // LOGON_WITH_PROFILE

            if (noProfile)
            {
                logonFlags = 0;
            }

            if (netOnly)
            {
                logonFlags = 2; // LOGON_NETCREDENTIALS_ONLY
            }

            IntPtr lpEnvironment = IntPtr.Zero;
            int creationFlags = 0x04000000; // CREATE_DEFAULT_ERROR_MODE
            if (env)
            {
                _ = CreateEnvironmentBlock(out lpEnvironment, IntPtr.Zero, false);
                creationFlags |= 0x00000400; // CREATE_UNICODE_ENVIRONMENT
            }

            try
            {
                SplitUsername(UserName, out string username, out string domain);
                if (!CreateProcessWithLogonW(username, domain, Password, logonFlags, applicationName, commandLine, creationFlags, lpEnvironment, currentDirectory, ref s, out PROCESS_INFORMATION p))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                _ = CloseHandle(p.hProcess);
                _ = CloseHandle(p.hThread);

                return Process.GetProcessById((int)p.dwProcessId);
            }
            finally
            {
                if (env)
                {
                    _ = DestroyEnvironmentBlock(lpEnvironment);
                }
            }
        }

        public static Process Start(string UserName, bool noProfile, bool env, bool netOnly, string applicationName, string commandLineArguments, string currentDirectory) => Start(UserName, RetrievePassword(UserName), noProfile, env, netOnly, applicationName, commandLineArguments, currentDirectory);

        public static string RetrievePassword(string username)
        {
            string applicationName = $"RunAs:{username}";

            Credential credentials = CredentialManager.ReadCredential(applicationName);

            do
            {
                if (credentials is null)
                {
                    CredentialResult creds = CredentialManager.PromptForCredentials(
                       captionText: "Enter Credentials",
                       saveCredential: CredentialSaveOption.Selected,
                       userName: username // Pre-fill the UI with a default username
                       );

                    credentials = new Credential(CredentialType.DomainPassword, applicationName, creds.UserName, creds.Password, string.Empty);

                    CredentialManager.WriteCredential(applicationName, creds.UserName, creds.Password, CredentialPersistence.Session);
                }

                string testDomain = string.Empty;

                if (username.Contains('\\'))
                {
                    testDomain = username.Split('\\')[0];
                }

                if (!DomainAuthentication.IsValid(credentials.UserName, credentials.Password, testDomain))
                {
                    Console.WriteLine("Authentication Failed.");

                    credentials = null;

                    CredentialManager.DeleteCredential(applicationName);

                    continue;
                }

                return credentials.Password;
            } while (true);
        }
    }
}