using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// An in-memory IOrganizationService, enough of one to drive the plug-in helpers that
    /// take a service rather than a plain value.
    ///
    /// The codebase had no fake, so everything reachable only through IOrganizationService
    /// was untested. AD-065 is what that costs: SubmitReviewPlugin read a multi-select
    /// column as a string and threw on every Tax submit, and no unit test could see it
    /// because the read sat behind a service call.
    ///
    /// Deliberately partial. It supports what the plug-ins actually issue - equality and
    /// inequality conditions, link-entity filtering, TopCount and Orders - and throws on
    /// anything else rather than quietly returning a wrong answer, because a fake that
    /// guesses is worse than no fake.
    /// </summary>
    public sealed class FakeOrganizationService : IOrganizationService
    {
        private readonly Dictionary<string, Dictionary<Guid, Entity>> _store =
            new Dictionary<string, Dictionary<Guid, Entity>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every Update the code under test issued, in order.</summary>
        public List<Entity> Updates { get; } = new List<Entity>();

        /// <summary>Every Create the code under test issued, in order.</summary>
        public List<Entity> Creates { get; } = new List<Entity>();

        public int RetrieveMultipleCount { get; private set; }

        /// <summary>Seeds a row directly, bypassing the Create log.</summary>
        public Entity Seed(string logicalName, Guid id, params object[] attributePairs)
        {
            var e = new Entity(logicalName, id);
            for (var i = 0; i + 1 < attributePairs.Length; i += 2)
            {
                e[(string)attributePairs[i]] = attributePairs[i + 1];
            }

            Table(logicalName)[id] = e;
            return e;
        }

        public Entity Row(string logicalName, Guid id)
        {
            Entity found;
            return Table(logicalName).TryGetValue(id, out found) ? found : null;
        }

        private Dictionary<Guid, Entity> Table(string logicalName)
        {
            Dictionary<Guid, Entity> table;
            if (!_store.TryGetValue(logicalName, out table))
            {
                table = new Dictionary<Guid, Entity>();
                _store[logicalName] = table;
            }

            return table;
        }

        public Guid Create(Entity entity)
        {
            var id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
            var stored = new Entity(entity.LogicalName, id);
            foreach (var pair in entity.Attributes)
            {
                stored[pair.Key] = pair.Value;
            }

            Table(entity.LogicalName)[id] = stored;
            Creates.Add(stored);
            return id;
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            var row = Row(entityName, id);
            if (row == null)
            {
                throw new InvalidOperationException(
                    entityName + " " + id.ToString("D") + " does not exist.");
            }

            if (columnSet == null || columnSet.AllColumns)
            {
                return row;
            }

            var projected = new Entity(entityName, id);
            foreach (var column in columnSet.Columns)
            {
                if (row.Contains(column))
                {
                    projected[column] = row[column];
                }
            }

            // FormattedValues drive the AD-039 grade labels, so a projection keeps them.
            foreach (var pair in row.FormattedValues)
            {
                if (columnSet.Columns.Contains(pair.Key))
                {
                    projected.FormattedValues[pair.Key] = pair.Value;
                }
            }

            return projected;
        }

        public void Update(Entity entity)
        {
            var row = Row(entity.LogicalName, entity.Id);
            if (row == null)
            {
                throw new InvalidOperationException(
                    entity.LogicalName + " " + entity.Id.ToString("D") + " does not exist.");
            }

            foreach (var pair in entity.Attributes)
            {
                row[pair.Key] = pair.Value;
            }

            Updates.Add(entity);
        }

        public void Delete(string entityName, Guid id)
        {
            Table(entityName).Remove(id);
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            RetrieveMultipleCount++;

            var q = query as QueryExpression;
            if (q == null)
            {
                throw new NotSupportedException("Only QueryExpression is supported.");
            }

            IEnumerable<Entity> rows = Table(q.EntityName).Values;

            if (q.Criteria != null)
            {
                rows = rows.Where(r => Matches(r, q.Criteria));
            }

            foreach (var link in q.LinkEntities)
            {
                var captured = link;
                rows = rows.Where(r => SatisfiesLink(r, captured));
            }

            foreach (var order in Enumerable.Reverse(q.Orders))
            {
                var captured = order;
                rows = order.OrderType == OrderType.Descending
                    ? rows.OrderByDescending(r => SortKey(r, captured.AttributeName))
                    : rows.OrderBy(r => SortKey(r, captured.AttributeName));
            }

            var list = rows.ToList();
            if (q.TopCount.HasValue)
            {
                list = list.Take(q.TopCount.Value).ToList();
            }

            return new EntityCollection(list);
        }

        // Walks a link chain, requiring at least one related row that satisfies every
        // LinkCriteria along the way. The plug-ins reach a question's section and a
        // response's review this way, so the chain is followed, not just the first hop.
        private bool SatisfiesLink(Entity row, LinkEntity link)
        {
            object fromValue;
            if (row.Contains(link.LinkFromAttributeName))
            {
                fromValue = Scalar(row[link.LinkFromAttributeName]);
            }
            else if (string.Equals(
                link.LinkFromAttributeName, row.LogicalName + "id", StringComparison.OrdinalIgnoreCase))
            {
                fromValue = row.Id;
            }
            else
            {
                return false;
            }

            foreach (var candidate in Table(link.LinkToEntityName).Values)
            {
                object toValue;
                if (candidate.Contains(link.LinkToAttributeName))
                {
                    toValue = Scalar(candidate[link.LinkToAttributeName]);
                }
                else if (string.Equals(
                    link.LinkToAttributeName, candidate.LogicalName + "id", StringComparison.OrdinalIgnoreCase))
                {
                    toValue = candidate.Id;
                }
                else
                {
                    continue;
                }

                if (!Equals(fromValue, toValue))
                {
                    continue;
                }

                if (link.LinkCriteria != null && !Matches(candidate, link.LinkCriteria))
                {
                    continue;
                }

                var childrenSatisfied = true;
                foreach (var child in link.LinkEntities)
                {
                    if (!SatisfiesLink(candidate, child))
                    {
                        childrenSatisfied = false;
                        break;
                    }
                }

                if (childrenSatisfied)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Matches(Entity row, FilterExpression filter)
        {
            var results = new List<bool>();
            foreach (var condition in filter.Conditions)
            {
                results.Add(Matches(row, condition));
            }

            foreach (var child in filter.Filters)
            {
                results.Add(Matches(row, child));
            }

            if (results.Count == 0)
            {
                return true;
            }

            return filter.FilterOperator == LogicalOperator.Or
                ? results.Any(x => x)
                : results.All(x => x);
        }

        private static bool Matches(Entity row, ConditionExpression condition)
        {
            object actual = null;
            if (row.Contains(condition.AttributeName))
            {
                actual = Scalar(row[condition.AttributeName]);
            }
            else if (string.Equals(
                condition.AttributeName, row.LogicalName + "id", StringComparison.OrdinalIgnoreCase))
            {
                actual = row.Id;
            }

            var expected = condition.Values.Count > 0 ? Scalar(condition.Values[0]) : null;

            switch (condition.Operator)
            {
                case ConditionOperator.Equal:
                    return Equals(actual, expected);
                case ConditionOperator.NotEqual:
                    return !Equals(actual, expected);
                case ConditionOperator.Null:
                    return actual == null;
                case ConditionOperator.NotNull:
                    return actual != null;
                default:
                    throw new NotSupportedException(
                        "ConditionOperator." + condition.Operator + " is not supported by this fake. "
                        + "Add it deliberately rather than letting a query silently match the wrong rows.");
            }
        }

        // Normalises the Dataverse wrapper types so a comparison against a raw Guid, int or
        // bool behaves the way the real service does.
        private static object Scalar(object value)
        {
            var reference = value as EntityReference;
            if (reference != null)
            {
                return reference.Id;
            }

            var option = value as OptionSetValue;
            if (option != null)
            {
                return option.Value;
            }

            var money = value as Money;
            if (money != null)
            {
                return money.Value;
            }

            return value;
        }

        private static IComparable SortKey(Entity row, string attribute)
        {
            if (!row.Contains(attribute))
            {
                return null;
            }

            return Scalar(row[attribute]) as IComparable;
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            throw new NotSupportedException("Execute is not supported by this fake.");
        }

        public void Associate(
            string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            throw new NotSupportedException("Associate is not supported by this fake.");
        }

        public void Disassociate(
            string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            throw new NotSupportedException("Disassociate is not supported by this fake.");
        }
    }
}
