using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Naming the actor of an audit event must not absorb an OrganizationService fault.
    ///
    /// An actor is a systemuser on a command and a Contact on a portal path (AD-053), so
    /// one of the two probes is always for a row that does not exist. Retrieve faults on a
    /// row that is not there, and a plug-in that catches an OrganizationService fault and
    /// carries on has its whole transaction aborted by the platform - "ISV code reduced the
    /// open transaction count" - the rule OptionLabels and NotificationOutbox already
    /// record, and the fault that killed a case edit and a portal submit on 2026-09-10.
    /// </summary>
    public class ActorNameTests
    {
        private static readonly Guid ActorId = Guid.Parse("3c6f441a-2fa1-4111-b8dd-e4fade069307");

        [Fact]
        public void Names_a_systemuser_actor()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", ActorId, "fullname", "Sims Rad");

            Assert.Equal("Sims Rad", CommandHelpers.ResolveActorName(svc, ActorId));
        }

        [Fact]
        public void Names_a_contact_actor()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ActorId, "fullname", "Dev Account");

            Assert.Equal("Dev Account", CommandHelpers.ResolveActorName(svc, ActorId));
        }

        [Fact]
        public void Never_retrieves_a_row_it_cannot_know_exists()
        {
            // The regression guard. A contact actor means the systemuser probe is for a row
            // that is not there; issuing it as a Retrieve is what produced a fault to
            // swallow. Asking by query answers "no such row" without one.
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ActorId, "fullname", "Dev Account");

            CommandHelpers.ResolveActorName(svc, ActorId);

            Assert.Equal(0, svc.RetrieveCount);
        }

        [Fact]
        public void Returns_null_when_the_actor_is_in_neither_table()
        {
            var svc = new FakeOrganizationService();

            Assert.Null(CommandHelpers.ResolveActorName(svc, ActorId));
            Assert.Equal(0, svc.RetrieveCount);
        }

        [Fact]
        public void Returns_null_for_a_row_whose_name_is_blank()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ActorId, "fullname", "   ");

            Assert.Null(CommandHelpers.ResolveActorName(svc, ActorId));
        }

        [Fact]
        public void Answers_without_a_service_call_when_there_is_no_actor()
        {
            var svc = new FakeOrganizationService();

            Assert.Null(CommandHelpers.ResolveActorName(svc, Guid.Empty));
            Assert.Null(CommandHelpers.ResolveActorName(null, ActorId));
            Assert.Equal(0, svc.RetrieveMultipleCount);
        }
    }
}
