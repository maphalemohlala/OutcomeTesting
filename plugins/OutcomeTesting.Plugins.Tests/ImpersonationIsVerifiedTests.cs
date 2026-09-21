using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A verb that offers to run as somebody else proves it is running as somebody else.
    ///
    /// <para>
    /// Found on 2026-09-21 building the second identity the twelve role-separation rows of
    /// the app checklist need. Every gated command in this solution reads the CALLER —
    /// <c>PermissionHelpers.EnsureAppPermission</c> resolves roles from the caller's work
    /// email, and the Dataverse layer beneath it applies the caller's own privileges — so a
    /// negative test run as the operator proves nothing. Dataverse impersonation looked like
    /// the way to test a refusal without holding that person's password.
    /// </para>
    /// <para>
    /// It is not, and the way it is not is the hazard. Caller impersonation is honoured only
    /// for a service principal authenticating with a client secret. The registration tool
    /// signs in as a licensed human over interactive OAuth, and on that token Dataverse does
    /// not reject <c>MSCRMCallerID</c> — it IGNORES it. <c>WhoAmI</c> came back HTTP 200,
    /// with a normal body, naming the operator, with no warning anywhere. Had the flag been
    /// trusted, every one of those twelve rows would have been driven as a System
    /// Administrator, succeeded, and been written down as a rule that does not exist.
    /// </para>
    /// <para>
    /// So this is F33 and F41's shape a third time — an option that silently does nothing
    /// when it cannot do its job — and the answer is the same one: make the silence
    /// impossible. Both verbs now ask the server who is calling, over the same transport and
    /// with the same headers as the request that follows, and refuse to continue unless the
    /// answer is the impersonated user.
    /// </para>
    /// <para>
    /// The guard is over the CLASS rather than those two verbs, because the flag is designed
    /// to start working the day this solution has its own application user, and the third
    /// verb wired to it must not be the one that forgets.
    /// </para>
    /// </summary>
    public class ImpersonationIsVerifiedTests
    {
        [Fact]
        public void Every_verb_that_accepts_a_caller_checks_the_server_agreed()
        {
            var unverified = VerbsAcceptingACaller()
                .Where(verb => !Regex.IsMatch(
                    verb.Value, @"ImpersonationLanded\s*\(|new\s+WhoAmIRequest\s*\("))
                .Select(verb => verb.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                unverified.Length == 0,
                "These verbs take --as and never confirm the impersonation actually landed. "
                + "Dataverse ignores the impersonation header on a licensed user's token "
                + "rather than refusing it, so the request runs as the operator and reports "
                + "success — which is indistinguishable from the rule under test being "
                + "absent. Call ImpersonationLanded before the request: "
                + string.Join(", ", unverified));
        }

        [Fact]
        public void The_check_is_looking_at_something()
        {
            // Without this the assertion above passes by finding no verbs at all, which is
            // the same blind spot it exists to close.
            var found = VerbsAcceptingACaller().Keys.ToArray();

            Assert.NotEmpty(found);
            Assert.Contains("WebApi", found);
            Assert.Contains("CallApi", found);
        }

        /// <summary>
        /// The registration tool's verbs that parse <c>--as</c>, keyed by name and carrying
        /// their own body.
        ///
        /// <para>
        /// Program.cs is one file of top-level statements whose verbs are local functions
        /// declared at column zero, so splitting on those declarations is enough to read one
        /// verb at a time — and reading them one at a time is the point. Matching the whole
        /// file would let a check written in <c>WebApi</c> vouch for <c>CallApi</c>.
        /// </para>
        /// </summary>
        private static Dictionary<string, string> VerbsAcceptingACaller()
        {
            var source = File.ReadAllText(RegistrationProgramPath());
            var declarations = Regex.Matches(source, @"^\w[\w<>\[\],\. ]*\s+(\w+)\s*\(",
                RegexOptions.Multiline);

            var verbs = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < declarations.Count; i++)
            {
                var start = declarations[i].Index;
                var end = i + 1 < declarations.Count ? declarations[i + 1].Index : source.Length;
                var body = source.Substring(start, end - start);

                if (body.Contains("TakeCaller("))
                {
                    verbs[declarations[i].Groups[1].Value] = body;
                }
            }

            // TakeCaller itself declares the flag rather than using it, and would otherwise
            // be reported as a verb that forgot to check its own return value.
            verbs.Remove("TakeCaller");
            return verbs;
        }

        private static string RegistrationProgramPath()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null
                && !File.Exists(Path.Combine(
                    directory.FullName, "plugins", "OutcomeTesting.Registration", "Program.cs")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(
                directory.FullName, "plugins", "OutcomeTesting.Registration", "Program.cs");
        }
    }
}
