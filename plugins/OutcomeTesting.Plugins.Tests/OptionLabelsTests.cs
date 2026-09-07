using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Naming a choice in the prose an Audit Event carries (FR-033).
    ///
    /// The case history screen renders al_details verbatim, and an Audit Event is immutable
    /// (NFR-AUD-01), so a number written into that line is permanent. These tests are about
    /// the line a person ends up reading, not about metadata plumbing.
    /// </summary>
    public class OptionLabelsTests
    {
        private const string Entity = "al_outcomecase";
        private const string Status = "al_casestatus";

        private static FakeOrganizationService WithStatuses()
        {
            var svc = new FakeOrganizationService();
            svc.SeedOptionSet(Entity, Status, 120910583, "Queued", 120910584, "Assigned");
            return svc;
        }

        [Fact]
        public void NamesAnOptionRatherThanNumberingIt()
        {
            var labels = new OptionLabels(WithStatuses());

            Assert.Equal("Queued", labels.Label(Entity, Status, 120910583));
            Assert.Equal("Assigned", labels.Label(Entity, Status, 120910584));
        }

        [Fact]
        public void FallsBackToTheNumberForAnOptionTheSetDoesNotHold()
        {
            // An option minted after this row was written, or since removed. The number still
            // identifies it; "(unknown)" would not.
            var labels = new OptionLabels(WithStatuses());

            Assert.Equal("120910599", labels.Label(Entity, Status, 120910599));
        }

        [Fact]
        public void FallsBackToTheNumberForAColumnThatIsNotAChoiceAtAll()
        {
            var labels = new OptionLabels(new FakeOrganizationService());

            Assert.Equal("120910583", labels.Label(Entity, "al_clientname", 120910583));
        }

        [Fact]
        public void DescribesAnEmptyChoiceTheWayTheRestOfTheLineReads()
        {
            var labels = new OptionLabels(WithStatuses());

            Assert.Equal("(none)", labels.Describe(Entity, Status, null));
            Assert.Equal("Queued", labels.Describe(Entity, Status, new OptionSetValue(120910583)));
        }

        [Fact]
        public void ReadsTheMetadataOncePerColumnHoweverOftenItIsAsked()
        {
            // One command can change several choice columns and reads both sides of each. A
            // metadata call per mention would put six round trips inside the transaction where
            // one belongs.
            var svc = WithStatuses();
            var labels = new OptionLabels(svc);

            labels.Label(Entity, Status, 120910583);
            labels.Label(Entity, Status, 120910584);
            labels.Describe(Entity, Status, new OptionSetValue(120910583));

            Assert.Equal(1, svc.MetadataReads);
        }
    }
}
