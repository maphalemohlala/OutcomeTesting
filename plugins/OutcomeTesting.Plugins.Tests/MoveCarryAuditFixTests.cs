using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Audit of the question move and its answer carry (2026-11-17): a second move or a
    /// retirement on top of a pending move, a move before the question starts, codes the move
    /// link cannot read back, an answer to a superseded wording carried as an answer to the
    /// moved question, and the move-time carry running as the administrator.
    /// </summary>
    public class MoveCarryAuditFixTests
    {
        private static readonly Guid CaseId = Guid.NewGuid();
        private static readonly Guid RouteId = Guid.NewGuid();
        private static readonly Guid TaxReviewId = Guid.NewGuid();
        private static readonly Guid ChecklistVersionId = Guid.NewGuid();
        private static readonly Guid TaxFrom = Guid.NewGuid();
        private static readonly Guid TaxTo = Guid.NewGuid();
        private static readonly Guid TaxTo2 = Guid.NewGuid();
        private static readonly Guid MovedQuestion = Guid.NewGuid();
        private static readonly Guid OldVersion = Guid.NewGuid();

        private static DateTime Today { get { return DateTime.UtcNow.Date; } }

        /// <summary>A Tax review that answered Q-E1-01 (v1) Pass, and three Tax sections.</summary>
        private static FakeOrganizationService Case()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", true, "al_requiresaqsreview", false);
            svc.Seed("al_outcomecase", CaseId, "al_casereference", "AP-1",
                "al_casestatus", new OptionSetValue(CaseLifecycle.ReviewInProgress),
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId));
            svc.Seed("al_checklistversion", ChecklistVersionId);
            svc.Seed("al_reviewinstance", TaxReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusInProgress),
                "al_checklistversionid", new EntityReference("al_checklistversion", ChecklistVersionId),
                "al_sequence", 1, "statecode", new OptionSetValue(0));
            foreach (var s in new[] { TaxFrom, TaxTo, TaxTo2 })
            {
                svc.Seed("al_section", s, "al_name", "S" + s.ToString("N").Substring(0, 4),
                    "al_ownerrole", new OptionSetValue(ResponseRules.OwnerRoleTaxTeam),
                    "al_checklistversionid", new EntityReference("al_checklistversion", ChecklistVersionId));
            }

            svc.Seed("al_question", MovedQuestion, "al_questioncode", "Q-E1-01", "al_name", "Moved",
                "al_displayorder", 1, "al_sectionid", new EntityReference("al_section", TaxFrom));
            svc.Seed("al_questionversion", OldVersion,
                "al_questionid", new EntityReference("al_question", MovedQuestion),
                "al_versionnumber", 1, "al_questiontext", "Was it suitable?",
                "al_effectivefrom", Today.AddDays(-30), "al_ismandatory", true, "al_displayorder", 1,
                "al_responsetype", new OptionSetValue(ResponseRules.TypePassFail));
            svc.Seed("al_response", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", TaxReviewId),
                "al_questionversionid", new EntityReference("al_questionversion", OldVersion),
                "al_answerchoice", new OptionSetValue(ResponseRules.ChoicePass));
            return svc;
        }

        private static FakeServiceProvider MoveProvider(FakeOrganizationService svc, Guid questionId, Guid toSection, string newCode, DateTime from)
        {
            var provider = new FakeServiceProvider(svc);
            Fill(provider.Context, questionId, toSection, newCode, from);
            return provider;
        }

        private static void Fill(FakePluginExecutionContext context, Guid questionId, Guid toSection, string newCode, DateTime from)
        {
            context.InputParameters["QuestionId"] = questionId.ToString("D");
            context.InputParameters["TargetSectionId"] = toSection.ToString("D");
            context.InputParameters["NewQuestionCode"] = newCode;
            context.InputParameters["Reason"] = "audit";
            context.InputParameters["EffectiveFrom"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            context.InputParameters["IdempotencyKey"] = Guid.NewGuid().ToString("D");
        }

        private static void Move(FakeOrganizationService svc, Guid questionId, Guid toSection, string newCode, DateTime from)
        {
            new MoveQuestionPlugin(null, null).Execute(MoveProvider(svc, questionId, toSection, newCode, from));
        }

        private static void Retire(FakeOrganizationService svc, Guid questionId, DateTime to)
        {
            var provider = new FakeServiceProvider(svc);
            provider.Context.InputParameters["QuestionId"] = questionId.ToString("D");
            provider.Context.InputParameters["EffectiveTo"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            provider.Context.InputParameters["Reason"] = "audit";
            provider.Context.InputParameters["IdempotencyKey"] = Guid.NewGuid().ToString("D");
            new RetireQuestionPlugin(null, null).Execute(provider);
        }

        private static Entity Question(FakeOrganizationService svc, string code)
        {
            return svc.All("al_question").Single(q => q.GetAttributeValue<string>("al_questioncode") == code);
        }

        private static Entity VersionOf(FakeOrganizationService svc, Guid questionId)
        {
            return svc.All("al_questionversion")
                .Single(v => v.GetAttributeValue<EntityReference>("al_questionid").Id == questionId);
        }

        private static bool Answered(FakeOrganizationService svc, Guid versionId)
        {
            return svc.All("al_response").Any(r => r.GetAttributeValue<EntityReference>("al_questionversionid").Id == versionId);
        }

        /// <summary>Moves every version date back, as if that many days had passed.</summary>
        private static void Pass(FakeOrganizationService svc, int days)
        {
            foreach (var v in svc.All("al_questionversion"))
            {
                foreach (var col in new[] { "al_effectivefrom", "al_effectiveto" })
                {
                    var d = v.GetAttributeValue<DateTime?>(col);
                    if (d.HasValue) v[col] = d.Value.AddDays(-days);
                }
            }
        }

        /// <summary>The question reworded yesterday: v1 (answered) ended, v2 (unanswered) in force.</summary>
        private static void Reword(FakeOrganizationService svc)
        {
            svc.Row("al_questionversion", OldVersion)["al_effectiveto"] = Today.AddDays(-1);
            svc.Seed("al_questionversion", Guid.NewGuid(),
                "al_questionid", new EntityReference("al_question", MovedQuestion),
                "al_versionnumber", 2, "al_questiontext", "Reworded: was it suitable and evidenced?",
                "al_effectivefrom", Today.AddDays(-1), "al_ismandatory", true, "al_displayorder", 1,
                "al_responsetype", new OptionSetValue(ResponseRules.TypePassFail));
        }

        // ---- 1. a question with a move pending cannot be moved again ----------------------

        [Fact]
        public void A_question_with_a_forward_move_pending_cannot_be_moved_a_second_time()
        {
            var svc = Case();
            Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today.AddDays(10));

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => Move(svc, MovedQuestion, TaxTo2, "Q-C-01", Today.AddDays(5)));

            Assert.Contains("Q-B-01", refusal.Message);
            Assert.Equal(Today.AddDays(10), svc.Row("al_questionversion", OldVersion).GetAttributeValue<DateTime?>("al_effectiveto"));
            Assert.DoesNotContain(svc.All("al_question"), q => q.GetAttributeValue<string>("al_questioncode") == "Q-C-01");
        }

        [Fact]
        public void A_second_move_dated_after_the_pending_one_names_the_move_not_a_retirement()
        {
            var svc = Case();
            Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today.AddDays(10));

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => Move(svc, MovedQuestion, TaxTo2, "Q-C-01", Today.AddDays(15)));

            Assert.Contains("already been moved to Q-B-01", refusal.Message);
            Assert.Equal(Today.AddDays(10), svc.Row("al_questionversion", OldVersion).GetAttributeValue<DateTime?>("al_effectiveto"));
        }

        [Fact]
        public void A_new_wording_on_a_question_with_a_move_pending_is_refused_and_names_the_move()
        {
            var svc = Case();
            Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today.AddDays(10));
            var provider = new FakeServiceProvider(svc);
            provider.Context.InputParameters["QuestionId"] = MovedQuestion.ToString("D");
            provider.Context.InputParameters["NewWording"] = "Reworded";
            provider.Context.InputParameters["EffectiveFrom"] = Today.AddDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            provider.Context.InputParameters["IdempotencyKey"] = Guid.NewGuid().ToString("D");

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => new RetireAndSucceedQuestionPlugin(null, null).Execute(provider));

            Assert.Contains("Q-B-01", refusal.Message);
            Assert.Equal(Today.AddDays(10), svc.Row("al_questionversion", OldVersion).GetAttributeValue<DateTime?>("al_effectiveto"));
            Assert.Single(svc.All("al_questionversion"), v => v.GetAttributeValue<EntityReference>("al_questionid").Id == MovedQuestion);
        }

        // ---- 2. nor retired separately --------------------------------------------------

        [Fact]
        public void Retiring_a_question_with_a_forward_move_pending_is_refused_and_names_the_move()
        {
            var svc = Case();
            Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today.AddDays(10));

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => Retire(svc, MovedQuestion, Today.AddDays(20)));

            Assert.Contains("Q-B-01", refusal.Message);
            Assert.Equal(Today.AddDays(10), svc.Row("al_questionversion", OldVersion).GetAttributeValue<DateTime?>("al_effectiveto"));
        }

        [Fact]
        public void The_moved_to_question_can_still_be_retired_instead()
        {
            var svc = Case();
            Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today.AddDays(10));
            var b = Question(svc, "Q-B-01");

            Retire(svc, b.Id, Today.AddDays(20));

            Assert.Equal(Today.AddDays(20), VersionOf(svc, b.Id).GetAttributeValue<DateTime?>("al_effectiveto"));
        }

        // ---- 3. only the answer to the version the move dated out is carried -------------

        [Fact]
        public void A_move_dated_today_does_not_carry_an_answer_to_a_superseded_wording()
        {
            var svc = Case();
            Reword(svc);

            Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today);

            Assert.False(Answered(svc, VersionOf(svc, Question(svc, "Q-B-01").Id).Id));
        }

        [Fact]
        public void A_forward_move_does_not_carry_an_answer_to_a_superseded_wording_at_save()
        {
            var svc = Case();
            Reword(svc);
            Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today.AddDays(10));
            Pass(svc, 10);

            Assert.Equal(0, AnswerWriter.CarryMovedAnswers(svc, TaxReviewId));
            Assert.False(Answered(svc, VersionOf(svc, Question(svc, "Q-B-01").Id).Id));
        }

        [Fact]
        public void A_wording_change_dated_on_the_move_day_leaves_the_older_wording_behind()
        {
            // v1 (answered) runs to day 10, v2 (the wording the move carries) starts that day,
            // and the move is dated that day too: v1 ends when the moved question starts, but
            // it is not the version the move dated out.
            var svc = Case();
            svc.Row("al_questionversion", OldVersion)["al_effectiveto"] = Today.AddDays(10);
            svc.Seed("al_questionversion", Guid.NewGuid(),
                "al_questionid", new EntityReference("al_question", MovedQuestion),
                "al_versionnumber", 2, "al_questiontext", "Reworded: was it suitable and evidenced?",
                "al_effectivefrom", Today.AddDays(10), "al_ismandatory", true, "al_displayorder", 1,
                "al_responsetype", new OptionSetValue(ResponseRules.TypePassFail));
            Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today.AddDays(10));
            Pass(svc, 10);

            Assert.Equal(0, AnswerWriter.CarryMovedAnswers(svc, TaxReviewId));
            Assert.False(Answered(svc, VersionOf(svc, Question(svc, "Q-B-01").Id).Id));
        }

        [Fact]
        public void An_answer_still_follows_a_chain_of_moves_to_the_question_in_force()
        {
            var svc = Case();
            Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today.AddDays(10));
            Move(svc, Question(svc, "Q-B-01").Id, TaxTo2, "Q-C-01", Today.AddDays(20));
            Pass(svc, 20);

            Assert.Equal(1, AnswerWriter.CarryMovedAnswers(svc, TaxReviewId));
            Assert.True(Answered(svc, VersionOf(svc, Question(svc, "Q-C-01").Id).Id));
        }

        // ---- 4. codes the move link cannot read back -------------------------------------

        [Fact]
        public void A_move_to_a_code_with_a_space_is_refused()
        {
            var svc = Case();

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => Move(svc, MovedQuestion, TaxTo, "Q E2 09", Today.AddDays(10)));

            Assert.Contains("space", refusal.Message);
            Assert.Null(svc.Row("al_questionversion", OldVersion).GetAttributeValue<DateTime?>("al_effectiveto"));
        }

        [Fact]
        public void A_new_question_code_with_a_space_is_refused_wherever_codes_are_checked()
        {
            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => ChecklistQueries.EnsureQuestionCodesAreFree(new FakeOrganizationService(), new[] { "Q-E1-09", "Q\tE1 10" }));

            Assert.Contains("space", refusal.Message);
        }

        [Fact]
        public void A_question_whose_own_code_has_a_space_is_refused_a_move()
        {
            var svc = Case();
            svc.Row("al_question", MovedQuestion)["al_questioncode"] = "Q E1 01";

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today.AddDays(10)));

            Assert.Contains("space", refusal.Message);
            Assert.Null(svc.Row("al_questionversion", OldVersion).GetAttributeValue<DateTime?>("al_effectiveto"));
            Assert.Empty(svc.All("al_auditevent"));
        }

        [Fact]
        public void A_move_whose_codes_overflow_the_audit_name_is_refused_with_a_sentence()
        {
            var svc = Case();
            var tooLong = new string('X', 100 - ChecklistQueries.MoveAuditName("Q-E1-01", string.Empty).Length + 1);

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => Move(svc, MovedQuestion, TaxTo, tooLong, Today.AddDays(10)));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal.Message);
            Assert.Contains("too long", refusal.Message);
            Assert.Null(svc.Row("al_questionversion", OldVersion).GetAttributeValue<DateTime?>("al_effectiveto"));
        }

        [Fact]
        public void The_longest_code_that_fits_is_moved_and_read_back()
        {
            var svc = Case();
            var longest = new string('X', 100 - ChecklistQueries.MoveAuditName("Q-E1-01", string.Empty).Length);

            Move(svc, MovedQuestion, TaxTo, longest, Today.AddDays(10));

            var audit = svc.All("al_auditevent").Single(a => a.GetAttributeValue<OptionSetValue>("al_command").Value == ChecklistQueries.CommandMoveQuestion);
            Assert.Equal(100, audit.GetAttributeValue<string>("al_name").Length);
            Assert.Equal(longest, ChecklistQueries.QuestionMoves(svc)["Q-E1-01"].Key);
        }

        // ---- 5. a move target not yet started cannot be moved before its start -----------

        [Fact]
        public void A_question_not_yet_started_cannot_be_moved_before_its_start()
        {
            var svc = Case();
            Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today.AddDays(10));
            var b = Question(svc, "Q-B-01");

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => Move(svc, b.Id, TaxTo2, "Q-C-01", Today.AddDays(3)));

            Assert.Contains(Today.AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), refusal.Message);
            Assert.Null(VersionOf(svc, b.Id).GetAttributeValue<DateTime?>("al_effectiveto"));
        }

        // ---- 6. the move-time carry runs as SYSTEM ---------------------------------------

        [Fact]
        public void The_move_time_carry_writes_as_the_system_user_not_the_administrator()
        {
            var svc = Case();
            var provider = new RecordingProvider(svc);
            Fill(provider.Context, MovedQuestion, TaxTo, "Q-B-01", Today);

            new MoveQuestionPlugin(null, null).Execute(provider);

            var creates = provider.Log.Where(e => e.Item2 == "create al_response").ToList();
            Assert.Single(creates);
            Assert.Null(creates[0].Item1);
            Assert.All(provider.Log.Where(e => e.Item2 == "query al_response"), e => Assert.Null(e.Item1));
        }

        // ---- 7. the carry reads only what it can use -------------------------------------

        [Fact]
        public void The_save_time_carry_reads_only_answers_to_moved_questions()
        {
            var svc = Case();
            Move(svc, MovedQuestion, TaxTo, "Q-B-01", Today.AddDays(10));
            var counting = new CountingOrganizationService(svc);

            AnswerWriter.CarryMovedAnswers(counting, TaxReviewId);

            var query = counting.Queries.Single(q => q.EntityName == "al_response");
            var codes = AllLinks(query).SelectMany(l => l.LinkCriteria.Conditions)
                .Single(c => c.AttributeName == "al_questioncode");
            Assert.Equal(ConditionOperator.In, codes.Operator);
            Assert.Equal(new[] { "Q-B-01", "Q-E1-01" }, codes.Values.Select(v => (string)v).OrderBy(v => v, StringComparer.Ordinal));
        }

        [Fact]
        public void With_no_moves_the_save_time_carry_costs_one_query()
        {
            var counting = new CountingOrganizationService(Case());

            Assert.Equal(0, AnswerWriter.CarryMovedAnswers(counting, TaxReviewId));

            Assert.Equal("al_auditevent", Assert.Single(counting.Queries).EntityName);
        }

        [Fact]
        public void The_move_time_carry_does_not_read_submitted_reviews()
        {
            var svc = Case();
            var provider = new RecordingProvider(svc);
            Fill(provider.Context, MovedQuestion, TaxTo, "Q-B-01", Today);

            new MoveQuestionPlugin(null, null).Execute(provider);

            var query = provider.Queries.Single(q => q.EntityName == "al_response");
            var review = AllLinks(query).Single(l => l.LinkToEntityName == "al_reviewinstance");
            Assert.Contains(Flatten(review.LinkCriteria), c => c.AttributeName == "al_reviewstatus"
                && c.Operator == ConditionOperator.NotEqual && (int)c.Values[0] == ResponseRules.StatusSubmitted);
            Assert.Null(query.TopCount);
            Assert.NotNull(query.PageInfo);
        }

        private static IEnumerable<LinkEntity> AllLinks(QueryExpression query)
        {
            var pending = new Stack<LinkEntity>(query.LinkEntities);
            while (pending.Count > 0)
            {
                var link = pending.Pop();
                yield return link;
                foreach (var child in link.LinkEntities) pending.Push(child);
            }
        }

        private static IEnumerable<ConditionExpression> Flatten(FilterExpression filter)
        {
            return filter.Conditions.Concat(filter.Filters.SelectMany(Flatten));
        }

        /// <summary>Hands out one service per user id asked for, logging who did what.</summary>
        private sealed class RecordingProvider : IServiceProvider, IOrganizationServiceFactory
        {
            private readonly FakeServiceProvider _inner = new FakeServiceProvider();
            private readonly FakeOrganizationService _service;

            public RecordingProvider(FakeOrganizationService service)
            {
                _service = service;
            }

            public FakePluginExecutionContext Context { get { return _inner.Context; } }

            public List<Tuple<Guid?, string>> Log { get; } = new List<Tuple<Guid?, string>>();

            public List<QueryExpression> Queries { get; } = new List<QueryExpression>();

            public object GetService(Type serviceType)
            {
                return serviceType == typeof(IOrganizationServiceFactory) ? this : _inner.GetService(serviceType);
            }

            public IOrganizationService CreateOrganizationService(Guid? userId)
            {
                return new As(this, userId);
            }

            private sealed class As : IOrganizationService
            {
                private readonly RecordingProvider _owner;
                private readonly Guid? _user;

                public As(RecordingProvider owner, Guid? user)
                {
                    _owner = owner;
                    _user = user;
                }

                private IOrganizationService Inner { get { return _owner._service; } }

                public Guid Create(Entity entity)
                {
                    _owner.Log.Add(Tuple.Create(_user, "create " + entity.LogicalName));
                    return Inner.Create(entity);
                }

                public EntityCollection RetrieveMultiple(QueryBase query)
                {
                    var expression = query as QueryExpression;
                    if (expression != null)
                    {
                        _owner.Log.Add(Tuple.Create(_user, "query " + expression.EntityName));
                        _owner.Queries.Add(expression);
                    }

                    return Inner.RetrieveMultiple(query);
                }

                public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) { return Inner.Retrieve(entityName, id, columnSet); }

                public void Update(Entity entity) { Inner.Update(entity); }

                public void Delete(string entityName, Guid id) { Inner.Delete(entityName, id); }

                public OrganizationResponse Execute(OrganizationRequest request) { return Inner.Execute(request); }

                public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
                {
                    Inner.Associate(entityName, entityId, relationship, relatedEntities);
                }

                public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
                {
                    Inner.Disassociate(entityName, entityId, relationship, relatedEntities);
                }
            }
        }
    }
}
