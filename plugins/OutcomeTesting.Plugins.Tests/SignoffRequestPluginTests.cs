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
        private static readonly Guid CaseId = Guid.Parse("cccccccc-2222-4222-8222-222222222222");
        private const string Adviser = "adviser@example.com";

        private static FakeOrganizationService Holding(params string[] roleNames)
        {
            return Holding(ContactId, roleNames);
        }

        /// <summary>
        /// A supervisor who may sign THIS case: they hold the role and they are the T&amp;C
        /// Manager mapped to its adviser. Both are now required (2026-09-21), so the fixture
        /// has to carry the whole chain - action, case, adviser email, mapping - and the
        /// tests below that vary one link are the interesting ones.
        /// </summary>
        private static FakeOrganizationService Holding(Guid mappedManager, params string[] roleNames)
        {
            var svc = new FakeOrganizationService();
            // The row the page wrote, which the plug-in clears back; the fake refuses an
            // update on a row it has never seen.
            svc.Seed("contact", ContactId, SignoffRequestPlugin.RequestAttr, "{}");
            svc.Seed("al_remediationaction", ActionId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-1",
                TcManagerRouting.CaseAdviserEmailAttr, Adviser);
            if (mappedManager != Guid.Empty)
            {
                if (mappedManager != ContactId)
                {
                    // TcManagerRouting reads the manager's work email to decide whether they
                    // are reachable, so the row has to exist for the routing to resolve at all.
                    svc.Seed("contact", mappedManager,
                        "fullname", "Pat Manager", "emailaddress1", "pat@example.com");
                }

                svc.Seed(TcManagerRouting.MappingEntity, Guid.NewGuid(),
                    TcManagerRouting.MappingEmailAttr, Adviser,
                    TcManagerRouting.ManagerAttr, new EntityReference("contact", mappedManager));
            }

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

        // --- the signatory must be THIS case's supervisor (2026-09-21) --------------------

        [Fact]
        public void Refuses_a_supervisor_who_is_not_mapped_to_this_cases_adviser()
        {
            // The finding that changed the rule: a service account held the T&C Supervisor
            // role and could therefore attest to a case whose adviser it supervises nothing
            // of. An attestation from outside that relationship records something that never
            // happened (BR-008).
            var someoneElse = Guid.Parse("eeeeeeee-5555-4555-8555-555555555555");
            var svc = Holding(someoneElse, WebRoleRegistry.TcSupervisorRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SignoffRequestPlugin.Apply(svc, ContactId, Request(ActionId, 120910720, null)));

            Assert.Contains("mapped to its adviser", error.Message);
            Assert.StartsWith(CommandHelpers.PreconditionPrefix, error.Message);
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void Does_not_name_the_adviser_when_somebody_else_is_the_mapped_manager()
        {
            // Anyone holding the role can reach this refusal, so a message naming the adviser
            // would let them enumerate who supervises whom one case at a time.
            var svc = Holding(
                Guid.Parse("eeeeeeee-5555-4555-8555-555555555555"), WebRoleRegistry.TcSupervisorRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SignoffRequestPlugin.Apply(svc, ContactId, Request(ActionId, 120910720, null)));

            Assert.DoesNotContain(Adviser, error.Message);
        }

        [Fact]
        public void Refuses_when_no_manager_is_mapped_at_all_and_says_which_adviser()
        {
            // Nobody may sign, deliberately. Here the adviser IS named: the gap is in
            // al_advisermapping, and the person reading this is the one who can have it
            // filled in (F58). No manager exists to be enumerated.
            var svc = Holding(Guid.Empty, WebRoleRegistry.TcSupervisorRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SignoffRequestPlugin.Apply(svc, ContactId, Request(ActionId, 120910720, null)));

            Assert.Contains("No T&C Manager is mapped to the adviser", error.Message);
            Assert.Contains(Adviser, error.Message);
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void The_role_is_checked_before_the_mapping()
        {
            // Somebody with neither is told they lack the role. Answering "you are not this
            // adviser's manager" first would send them looking for a mapping they could not
            // use even once they had it.
            var svc = Holding(Guid.Empty, WebRoleRegistry.AqsReviewerRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SignoffRequestPlugin.Apply(svc, ContactId, Request(ActionId, 120910720, null)));

            Assert.Contains("T&C Supervisor", error.Message);
            Assert.DoesNotContain("mapped to its adviser", error.Message);
        }

        [Fact]
        public void A_mapped_manager_with_no_work_email_may_still_sign()
        {
            // Whether they can be EMAILED is a routing concern and has nothing to do with
            // whether they may attest. The fixture's manager contact carries no
            // emailaddress1, so TcManagerRouting reports ManagerNotReachable - and the gate
            // compares the manager whenever the mapping resolved one at all.
            var svc = Holding(ContactId, WebRoleRegistry.TcSupervisorRole);

            SignoffRequestPlugin.Apply(svc, ContactId, Request(ActionId, 120910720, null));

            Assert.Single(svc.Creates);
        }

        [Fact]
        public void Refuses_an_action_that_is_attached_to_no_case()
        {
            // Nothing to resolve a supervisor from. Refused rather than waved through, which
            // is what "no case" would otherwise amount to.
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, SignoffRequestPlugin.RequestAttr, "{}");
            svc.Seed("al_remediationaction", ActionId, "al_name", "orphan");
            svc.FetchResults.Enqueue(new EntityCollection(new List<Entity>
            {
                Role(WebRoleRegistry.TcSupervisorRole),
            }));

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SignoffRequestPlugin.Apply(svc, ContactId, Request(ActionId, 120910720, null)));

            Assert.Contains("not attached to a case", error.Message);
            Assert.Empty(svc.Creates);
        }

        private static Entity Role(string name)
        {
            var row = new Entity("contact", ContactId);
            row["role.name"] = new AliasedValue("powerpagecomponent", "name", name);
            return row;
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
