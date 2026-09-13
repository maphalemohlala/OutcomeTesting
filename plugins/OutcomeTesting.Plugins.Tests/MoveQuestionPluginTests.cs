using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-122. A move retires the question where it was and creates it where it is going,
    /// so history stays attached to the section it was answered in. Protected codes are
    /// refused, because a move retires the original just as surely as a retire does.
    /// </summary>
    public class MoveQuestionPluginTests
    {
        private static readonly Guid Tax = Guid.Parse("11111111-1111-4111-8111-111111111111");
        private static readonly Guid Aqs = Guid.Parse("22222222-2222-4222-8222-222222222222");

        [Fact]
        public void An_ordinary_question_may_move()
        {
            Assert.Null(MoveQuestionPlugin.RefusalFor("Q-E1-01", Tax, Aqs));
        }

        [Fact]
        public void A_protected_question_may_not_move()
        {
            var refusal = MoveQuestionPlugin.RefusalFor("Q-GR-01", Tax, Aqs);

            Assert.NotNull(refusal);
            Assert.Contains("Q-GR-01", refusal);
        }

        [Fact]
        public void A_move_to_the_section_it_is_already_in_is_refused()
        {
            // Otherwise the question is retired and recreated for no change, and the old
            // code is burned - codes are unique and never freed.
            var refusal = MoveQuestionPlugin.RefusalFor("Q-E1-01", Tax, Tax);

            Assert.NotNull(refusal);
            Assert.Contains("already in", refusal);
        }

        private static readonly DateTime Today = new DateTime(2026, 9, 13);

        [Fact]
        public void A_retired_question_cannot_be_moved()
        {
            // Moving it would date the old version out again - later than it was - and so
            // bring it back into force for the gap.
            var refusal = MoveQuestionPlugin.RetiredRefusal(new DateTime(2026, 8, 1), Today);

            Assert.NotNull(refusal);
            Assert.Contains("already retired", refusal);
        }

        [Fact]
        public void A_question_dated_out_in_the_future_may_still_move()
        {
            Assert.Null(MoveQuestionPlugin.RetiredRefusal(new DateTime(2026, 10, 1), Today));
        }

        [Fact]
        public void A_live_question_may_move()
        {
            Assert.Null(MoveQuestionPlugin.RetiredRefusal(null, Today));
        }
    }
}
