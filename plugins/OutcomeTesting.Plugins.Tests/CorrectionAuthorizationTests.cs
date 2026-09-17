using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-143. `al_RegradeCase` and `al_SignOffRemediation` enforce the application rules
    /// `command.regrade` and `command.signoff` server-side.
    ///
    /// They enforced nothing. Both are public Custom APIs with no `executeprivilegename`,
    /// neither called <see cref="PermissionHelpers.EnsureAppPermission"/>, and — unlike
    /// `al_SubmitReview` and `al_CompleteRemediation`, which refuse a caller who is not the
    /// assigned checker or the owning adviser — neither carried an ownership check either.
    ///
    /// The class comments said the control was the Dataverse privilege: "the sign-off is
    /// created as the initiating user, so a caller without create-al_signoff privilege is
    /// refused". That privilege does not discriminate. `GrantSecurity` puts `al_outcome` and
    /// `al_signoff` in `writeForEveryone`, so BOTH application roles hold create on them at
    /// Global depth — verified in TEST on 2026-09-16, where `Outcome Testing App User` holds
    /// `prvCreateal_Outcome` and `prvCreateal_Signoff`. Every app user could therefore regrade
    /// an outcome or sign off a remediation by calling the API directly, while the client
    /// showed the buttons only to a T&amp;C Supervisor.
    ///
    /// That is the segregation of duties AD-031 exists for: the person who did the work is
    /// not the person who signs it off. The portal half was never the gap — `SignoffRequestPlugin`
    /// and `RegradeRequestPlugin` check the contact's web roles, because a Power Pages write
    /// arrives as the site's application user and a caller check would enforce nothing
    /// (AD-053). This closes the Code App half, which OD-041 records as the only route to
    /// these two APIs.
    ///
    /// Gated on the permission rules rather than on a hard-coded role name so the two tiers
    /// read the same rulebook: the client already gates these buttons on exactly these keys.
    /// </summary>
    public class CorrectionAuthorizationTests
    {
        private const string Email = "checker@ascotlloyd.co.uk";
        private static readonly Guid CallerId = Guid.Parse("22222222-bbbb-4bbb-8bbb-222222222222");
        private static readonly Guid ContactId = Guid.Parse("33333333-cccc-4ccc-8ccc-333333333333");

        /// <summary>
        /// A caller who holds an application role, and therefore every Dataverse privilege the
        /// two commands write with, but NOT the rule the action needs. Before AD-143 this
        /// caller succeeded.
        /// </summary>
        private static FakeOrganizationService CallerHolding(string grantedResource)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", CallerId, "internalemailaddress", Email);
            svc.Seed(
                ContactRegistry.Entity,
                ContactId,
                ContactRegistry.EmailAttr, Email,
                ContactRegistry.StateCodeAttr, new OptionSetValue(ContactRegistry.StateActive));
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", Email,
                "al_rolecode", "AL Portal - Tax Reviewer",
                "statecode", new OptionSetValue(0));

            if (grantedResource != null)
            {
                svc.Seed(
                    "al_pagepermission",
                    Guid.NewGuid(),
                    "al_resourcekey", grantedResource,
                    "al_rolecode", "AL Portal - Tax Reviewer",
                    "al_accesslevel", new OptionSetValue(PermissionHelpers.AccessEdit),
                    "statecode", new OptionSetValue(0));
            }

            // The contact-to-web-role joins the resolver issues are FetchXML, which the fake
            // does not execute; this caller's roles come from the mapping table.
            svc.FetchResults.Enqueue(new EntityCollection());
            svc.FetchResults.Enqueue(new EntityCollection());
            return svc;
        }

        private static IPluginExecutionContext Context()
        {
            return new FakePluginExecutionContext
            {
                UserId = CallerId,
                InitiatingUserId = CallerId,
            };
        }

        [Fact]
        public void RegradeIsRefusedWithoutCommandRegrade()
        {
            var svc = CallerHolding("page.cases");

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => PermissionHelpers.EnsureAppPermission(
                    svc, Context(), RegradeCasePlugin.RegradeResource, PermissionHelpers.AccessEdit));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, error.Message, StringComparison.Ordinal);
            Assert.Contains(RegradeCasePlugin.RegradeResource, error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void RegradeIsAllowedWithCommandRegrade()
        {
            var svc = CallerHolding(RegradeCasePlugin.RegradeResource);

            PermissionHelpers.EnsureAppPermission(
                svc, Context(), RegradeCasePlugin.RegradeResource, PermissionHelpers.AccessEdit);
        }

        [Fact]
        public void SignOffIsRefusedWithoutCommandSignoff()
        {
            var svc = CallerHolding("page.remediation");

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => PermissionHelpers.EnsureAppPermission(
                    svc, Context(), SignOffRemediationPlugin.SignOffResource, PermissionHelpers.AccessEdit));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void SignOffIsAllowedWithCommandSignoff()
        {
            var svc = CallerHolding(SignOffRemediationPlugin.SignOffResource);

            PermissionHelpers.EnsureAppPermission(
                svc, Context(), SignOffRemediationPlugin.SignOffResource, PermissionHelpers.AccessEdit);
        }

        /// <summary>
        /// The keys are the ones the CLIENT already gates these buttons on. If they drift
        /// apart, the UI offers work the command refuses — the failure AD-136 describes.
        /// </summary>
        [Fact]
        public void TheResourceKeysAreTheOnesTheClientGatesOn()
        {
            Assert.Equal("command.regrade", RegradeCasePlugin.RegradeResource);
            Assert.Equal("command.signoff", SignOffRemediationPlugin.SignOffResource);
        }
    }
}
