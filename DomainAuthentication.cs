using System;
using System.DirectoryServices.AccountManagement;

namespace Penguin
{
    public static class DomainAuthentication
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
        public static bool IsValid(string username, string password, string domain)
        {
            Credentials credentials = new()
            {
                Password = password,
                Username = username,
            };

            try
            {
                using PrincipalContext pc = new(ContextType.Domain, domain);
                // validate the credentials
                return pc.ValidateCredentials(credentials.Username, credentials.Password);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unable to validate credentials:");
                Console.WriteLine("\t" + ex.Message);
                return true;
            }
        }
    }
}