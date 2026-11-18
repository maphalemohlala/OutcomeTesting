using System;
using System.IO;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A submit's gates read the review's reference day before the submit stamps it; everything
    /// after the stamp must read against the stamp, as a re-read of al_submittedon would. The
    /// two differ when a submit straddles 00:00 UTC. Submit reads DateTime.UtcNow directly, with
    /// no seam for a clock, so the memo is pinned here and the call order by reading the source,
    /// the way SubmitReviewStampsCheckDateFirstTests does.
    /// </summary>
    public class SubmitReadsStampTests
    {
        private static readonly Guid Review = Guid.NewGuid();
        private static readonly Guid Other = Guid.NewGuid();

        private static SubmitReviewPlugin.SubmitReads GateReadsAt(DateTime gateMoment)
        {
            var reads = new SubmitReviewPlugin.SubmitReads();
            reads.ReferenceDays[Review] = gateMoment;
            reads.ReferenceDays[Other] = gateMoment;
            reads.Choices[Review.ToString("N") + "|Q-GR-01"] = 120910300;
            reads.Choices[Other.ToString("N") + "|Q-TAX-01"] = 120910301;
            return reads;
        }

        [Fact]
        public void The_stamp_replaces_the_day_the_gates_read()
        {
            var reads = GateReadsAt(new DateTime(2026, 11, 16, 23, 59, 59, DateTimeKind.Utc));
            var stamp = new DateTime(2026, 11, 17, 0, 0, 1, DateTimeKind.Utc);

            reads.Stamp(Review, stamp);

            Assert.Equal(stamp, reads.ReferenceDays[Review]);
            Assert.Equal(new DateTime(2026, 11, 16, 23, 59, 59, DateTimeKind.Utc), reads.ReferenceDays[Other]);
        }

        [Fact]
        public void A_stamp_on_a_later_day_forgets_that_reviews_choices_only()
        {
            var reads = GateReadsAt(new DateTime(2026, 11, 16, 23, 59, 59, DateTimeKind.Utc));

            reads.Stamp(Review, new DateTime(2026, 11, 17, 0, 0, 1, DateTimeKind.Utc));

            Assert.False(reads.Choices.ContainsKey(Review.ToString("N") + "|Q-GR-01"));
            Assert.True(reads.Choices.ContainsKey(Other.ToString("N") + "|Q-TAX-01"));
        }

        [Fact]
        public void A_stamp_on_the_same_day_keeps_the_choices_already_read()
        {
            var reads = GateReadsAt(new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc));

            reads.Stamp(Review, new DateTime(2026, 11, 16, 10, 0, 2, DateTimeKind.Utc));

            Assert.Equal(120910300, reads.Choices[Review.ToString("N") + "|Q-GR-01"]);
        }

        [Fact]
        public void Submit_stamps_the_memo_with_the_value_it_wrote_before_finalising()
        {
            var source = File.ReadAllText(Path.Combine(PluginsPath(), "SubmitReviewPlugin.cs"));

            var written = source.IndexOf("[ReviewSubmittedOn] = stamp,", StringComparison.Ordinal);
            var memo = source.IndexOf("reads.Stamp(targetId, stamp);", StringComparison.Ordinal);
            var finalise = source.IndexOf("FinaliseReview(service, review, targetId, correlationId, reads, writer);", StringComparison.Ordinal);

            Assert.True(written >= 0, "Submit no longer writes al_submittedon from the captured stamp.");
            Assert.True(memo > written, "Submit must hand the stamp it wrote to the memo.");
            Assert.True(finalise > memo, "The memo must hold the stamp before FinaliseReview reads against it.");
        }

        private static string PluginsPath()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "OutcomeTesting.Plugins")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory.FullName, "OutcomeTesting.Plugins");
        }
    }
}
