using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Who an audit event says did the thing (NFR-AUD-01).
    ///
    /// Every audit writer stamped al_actorid — a GUID in a text column — and nothing ever
    /// wrote al_actorname, so the case history had no name to show and fell back to
    /// createdbyname. For a portal action that is the site's application user and for a
    /// command it is whichever account the plug-in ran as, so the log answered "who changed
    /// this?" with a service account every time.
    ///
    /// Resolution is a service-taking helper rather than something inside the writers,
    /// because IPluginExecutionContext has no fake in this codebase (see
    /// AssignUserRolePluginTests) and a rule that cannot be tested is a rule that drifts.
    /// </summary>
    public class ActorNameTests
    {
        private static readonly Guid UserId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
        private static readonly Guid ContactId = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222");
        private static readonly Guid Missing = Guid.Parse("cccccccc-3333-4333-8333-333333333333");

        [Fact]
        public void Names_the_dataverse_user_who_ran_the_command()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", UserId, "fullname", "Simunye Radingwana");

            Assert.Equal("Simunye Radingwana", CommandHelpers.ResolveActorName(svc, UserId));
        }

        [Fact]
        public void Names_the_contact_when_the_actor_is_a_portal_user()
        {
            // A Power Pages write reaches Dataverse as the site's application user, so a
            // portal path passes the signed-in Contact as the actor instead (AD-053). The
            // id is then a contact id, and no systemuser carries it.
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "fullname", "Dev Account");

            Assert.Equal("Dev Account", CommandHelpers.ResolveActorName(svc, ContactId));
        }

        [Fact]
        public void Prefers_the_dataverse_user_when_both_tables_hold_the_id()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", UserId, "fullname", "Simunye Radingwana");
            svc.Seed("contact", UserId, "fullname", "Someone Else");

            Assert.Equal("Simunye Radingwana", CommandHelpers.ResolveActorName(svc, UserId));
        }

        [Fact]
        public void Returns_null_rather_than_throwing_when_the_actor_cannot_be_named()
        {
            // The audit row must still be written. An unnameable actor costs the log a
            // display name; a throw here would cost it the whole event, and the event is
            // the part that is immutable and required.
            var svc = new FakeOrganizationService();

            Assert.Null(CommandHelpers.ResolveActorName(svc, Missing));
        }

        [Fact]
        public void Returns_null_for_an_empty_actor_without_asking_dataverse()
        {
            var svc = new FakeOrganizationService();

            Assert.Null(CommandHelpers.ResolveActorName(svc, Guid.Empty));
            Assert.Equal(0, svc.RetrieveCount);
        }

        [Fact]
        public void Treats_a_blank_name_as_no_name()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", UserId, "fullname", "   ");

            Assert.Null(CommandHelpers.ResolveActorName(svc, UserId));
        }
    }
}
