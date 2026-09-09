using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The T&amp;C attestation through the contact's request column (AD-099): the sign-off
    /// is created server-side from the signed-in contact's own row, with the supervisor role
    /// checked against the platform rather than a permission the browser's create never
    /// reached.
    /// </summary>
    public class SignoffRequestPluginTests
    {
        private static readonly Guid ContactId = Guid.Parse("dddddddd-4444-4444-8444-444444444444");
        private static readonly Guid ActionId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");

        private static FakeOrganizationService Holding(params string[] roleNames)
        {
            var svc = new FakeOrganizationService();
            // The row the page wrote, which the plug-in clears back; the fake refuses an
            // update on a row it has never seen.
            svc.Seed("contact", ContactId, SignoffRequestPlugin.RequestAttr, "{}");
            var rows = new List<Entity>();
            foreach (var name in roleNames)
            {
                var row = new Entity("contact", ContactId);
                row["role.name"] = new AliasedValue("powerpagecomponent", "name", name);
                rows.Add(row);
            }

            svc.FetchResults.Enqueue(new EntityCollection(rows));
            return svc;
        }

        private static string Request(Guid actionId, int decision, string notes)
        {
            return "{\"actionId\":\"" + actionId.ToString("D") + "\",\"decision\":" + decision
                + (notes == null ? string.Empty : ",\"notes\":\"" + notes + "\"") + "}";
        }

        [Fact]
        public void Creates_the_sign_off_with_only_the_decision_the_notes_and_the_action()
        {
            var svc = Holding(WebRoleRegistry.TcSupervisorRole);

            SignoffRequestPlugin.Apply(svc, ContactId, Request(ActionId, SignoffProgressPlugin.DecisionRejectedValue, "TOB still missing."));

            var created = Assert.Single(svc.Creates);
            Assert.Equal("al_signoff", created.LogicalName);
            Assert.Equal(SignoffProgressPlugin.DecisionRejectedValue, created.GetAttributeValue<OptionSetValue>("al_signoffdecision").Value);
            Assert.Equal("TOB still missing.", created.GetAttributeValue<string>("al_notes"));
            Assert.Equal(ActionId, created.GetAttributeValue<EntityReference>("al_remediationactionid").Id);
            // Name, code, case and timestamp are SignoffGuardPlugin's to stamp.
            Assert.False(created.Contains("al_name"));
            Assert.False(created.Contains("al_signoffcode"));
            Assert.False(created.Contains("al_signedoffon"));
        }

        [Fact]
        public void Clears_the_request_column_before_creating()
        {
            var svc = Holding(WebRoleRegistry.TcSupervisorRole);

            SignoffRequestPlugin.Apply(svc, ContactId, Request(ActionId, 120910720, null));

            var clear = Assert.Single(svc.Updates);
            Assert.Equal("contact", clear.LogicalName);
            Assert.Equal(ContactId, clear.Id);
            Assert.True(clear.Contains(SignoffRequestPlugin.RequestAttr));
            Assert.Null(clear[SignoffRequestPlugin.RequestAttr]);
        }

        [Fact]
        public void Refuses_a_contact_without_the_supervisor_role_and_writes_nothing()
        {
            // The contact permission the page writes through is shared with the reviewer
            // roles' claim request, so a reviewer can reach this column. The role is the
            // boundary, and it is checked here (BR-008).
            var svc = Holding(WebRoleRegistry.AqsReviewerRole, "Administrators");

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SignoffRequestPlugin.Apply(svc, ContactId, Request(ActionId, 120910720, null)));

            Assert.Contains("T&C Supervisor", error.Message);
            Assert.StartsWith(CommandHelpers.PreconditionPrefix, error.Message);
            Assert.Empty(svc.Creates);
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Refuses_a_request_that_names_no_action()
        {
            var svc = Holding(WebRoleRegistry.TcSupervisorRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SignoffRequestPlugin.Apply(svc, ContactId, "{\"decision\":120910720}"));

            Assert.StartsWith(CommandHelpers.ValidationPrefix, error.Message);
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void Refuses_a_request_with_no_decision()
        {
            var svc = Holding(WebRoleRegistry.TcSupervisorRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SignoffRequestPlugin.Apply(svc, ContactId, "{\"actionId\":\"" + ActionId.ToString("D") + "\"}"));

            Assert.StartsWith(CommandHelpers.ValidationPrefix, error.Message);
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void Refuses_a_payload_that_is_not_json()
        {
            var svc = Holding(WebRoleRegistry.TcSupervisorRole);

            Assert.Throws<InvalidPluginExecutionException>(
                () => SignoffRequestPlugin.Apply(svc, ContactId, "not json"));
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void Parses_the_shape_the_page_sends()
        {
            var payload = SignoffRequestPayload.Parse(
                "{\"actionId\":\"" + ActionId.ToString("D") + "\",\"decision\":120910721,\"notes\":\"Say what to fix.\"}");

            Assert.NotNull(payload);
            Assert.Equal(ActionId.ToString("D"), payload.ActionId);
            Assert.Equal(120910721, payload.Decision);
            Assert.Equal("Say what to fix.", payload.Notes);
            Assert.Null(SignoffRequestPayload.Parse(string.Empty));
            Assert.Null(SignoffRequestPayload.Parse("{"));
        }

        [Fact]
        public void The_supervisor_role_name_matches_the_web_role_seed()
        {
            Assert.Equal("AL Portal - T&C Supervisor", WebRoleRegistry.TcSupervisorRole);
            Assert.DoesNotContain(WebRoleRegistry.TcSupervisorRole, new[] { WebRoleRegistry.TaxReviewerRole, WebRoleRegistry.AqsReviewerRole }.ToList());
        }
    }
}
